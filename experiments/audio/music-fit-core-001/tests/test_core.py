from __future__ import annotations
import json
import tempfile
import unittest
from dataclasses import replace
from pathlib import Path
import numpy as np
import soundfile as sf
from musicfit.core import *
from musicfit.render import fit,render
from fixtures import song

class CoreTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        self.path=self.root/'source.wav'
        self.samples,self.hidden=song()
        sf.write(self.path,self.samples,16000,subtype='PCM_24')
    def tearDown(self): self.tmp.cleanup()
    def test_analysis_recovery(self):
        a=analyze(self.path)
        self.assertTrue(a.edges)
        h=self.hidden
        self.assertTrue(any(abs((e.end-e.start)-h['period'])<=.07*16000 for e in a.edges[:3]))
    def test_exact_short_and_long(self):
        a=analyze(self.path)
        for phase in [3,4]:
            for seconds in [8.125,26.7,49.35,84.02]:
                result=plans(a,seconds,phase=phase)
                self.assertTrue(result,(phase,seconds))
                self.assertLessEqual(len(result),3)
                for p in result:
                    validate_plan(a,p,Config())
                    self.assertEqual(p.target_frames,sum(s.end-s.start for s in p.spans))
    def test_stream_render_preserves_stereo_and_timing(self):
        a=analyze(self.path); p=plans(a,49.35)[0]
        out=self.root/'render.wav'; report=render(self.path,a,p,out)
        x,sr=sf.read(out,always_2d=True)
        self.assertEqual(x.shape,(round(49.35*sr),2))
        self.assertLessEqual(np.max(np.abs(x)),.98)
        self.assertLess(np.max(np.abs(x[-1])),1e-6)
        self.assertTrue(np.all(np.isfinite(x)))
        self.assertLess(np.max(np.abs(x[:,1]-.91*x[:,0])),1e-5)
        self.assertEqual(report['frames'],len(x))
    def test_distinct_multijump_route(self):
        a=analyze(self.path)
        # A contract fixture with several legitimate recurrence endpoints. Not a perceptual test.
        a.edges=[Edge(4*16000,12*16000,.98,.98),Edge(14*16000,22*16000,.98,.98),Edge(6*16000,22*16000,.98,.98)]
        result=plans(a,55.35,phase=4)
        self.assertTrue(result)
        self.assertTrue(any(len({(l.end,r.start) for l,r in zip(p.spans,p.spans[1:])})>=2 for p in result))
    def test_phase4_discovery_to_render_two_periods(self):
        from fixtures import two_sections
        samples, periods = two_sections()
        sf.write(self.path,samples,16000,subtype='PCM_24')
        a=analyze(self.path)
        result=plans(a,(len(samples)+sum(periods))/16000,phase=4)
        self.assertTrue(result)
        p=result[0]
        self.assertEqual(p.ending,'source_end')
        used={l.end-r.start for l,r in zip(p.spans,p.spans[1:])}
        self.assertTrue(set(periods).issubset(used),used)
        m=render(self.path,a,p,self.root/'phase4.wav')
        self.assertEqual(m['frames'],len(samples)+sum(periods))
        self.assertEqual(len(m['seams']),2)
        self.assertEqual(p.spans[0].start,0)
        self.assertEqual(p.spans[-1].end,len(samples))
    def test_mid_render_cancel_removes_partial(self):
        a=analyze(self.path);p=plans(a,49.35)[0]
        calls=[0]
        def cancel():
            calls[0]+=1
            return calls[0]>8
        with self.assertRaises(Cancelled):
            render(self.path,a,p,self.root/'cancelled.wav',budget=Budget(cancel=cancel))
        self.assertFalse((self.root/'cancelled.wav').exists())
        self.assertFalse(list(self.root.glob('.render-*')))
    def test_integer_config_guard(self):
        with self.assertRaises(FitError): Config(max_jumps=1.5).validate()
    def test_deterministic_edit_map(self):
        a=analyze(self.path)
        self.assertEqual([p.to_dict() for p in plans(a,49.35)],[p.to_dict() for p in plans(a,49.35)])
    def test_refuse_changed_source(self):
        a=analyze(self.path); p=plans(a,10)[0]
        sf.write(self.path,self.samples*.5,16000)
        with self.assertRaisesRegex(FitError,'source_changed'):
            render(self.path,a,p,self.root/'out.wav')
        self.assertFalse((self.root/'out.wav').exists())
    def test_refuse_overwrite(self):
        a=analyze(self.path); p=plans(a,10)[0]
        original=self.path.read_bytes()
        with self.assertRaises(FitError): render(self.path,a,p,self.path)
        self.assertEqual(self.path.read_bytes(),original)
    def test_cancel_no_success_output(self):
        with self.assertRaises(Cancelled):
            fit(self.path,50,self.root/'fit',budget=Budget(cancel=lambda:True))
        self.assertFalse((self.root/'fit').exists())
    def test_silence_not_recurrence(self):
        sf.write(self.path,np.zeros((160000,2)),16000)
        a=analyze(self.path)
        self.assertFalse(a.edges)
        self.assertFalse(plans(a,20))
    def test_antiphase_not_silence(self):
        self.samples[:,1]=-self.samples[:,0];sf.write(self.path,self.samples,16000)
        a=analyze(self.path)
        self.assertTrue(a.edges)
    def test_negative_bad_spans_and_targets(self):
        a=analyze(self.path)
        for value in [float('nan'),float('inf'),-1,0,True,5000]:
            with self.assertRaises(FitError): plans(a,value)
        p=plans(a,10)[0]
        p.spans=[Span(-1,160000)]
        with self.assertRaises(FitError): render(self.path,a,p,self.root/'bad.wav')
    def test_three_auditionable_candidates(self):
        r=fit(self.path,49.35,self.root/'fit')
        self.assertEqual(r['status'],'needs_listening_review')
        self.assertIsNone(r['quality']['human_acceptance_rate'])
        self.assertTrue((self.root/'fit'/'review.html').is_file())
        for row in r['candidates']:
            self.assertTrue((self.root/'fit'/row['render']['path']).is_file())
            for clip in row['previews']:
                self.assertTrue((self.root/'fit'/clip['path']).is_file())
        json.loads((self.root/'fit'/'result.json').read_text())
    def test_no_plan_does_not_make_directory(self):
        sf.write(self.path,np.zeros((160000,2)),16000)
        with self.assertRaisesRegex(FitError,'no_suitable_plan'): fit(self.path,20,self.root/'fit')
        self.assertFalse((self.root/'fit').exists())

if __name__=='__main__': unittest.main()
