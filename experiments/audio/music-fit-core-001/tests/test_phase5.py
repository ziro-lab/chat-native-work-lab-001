"""Checkpoint A contracts; synthetic evidence is NOT human acceptance."""
from __future__ import annotations
import importlib.util
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

import numpy as np
import soundfile as sf

from musicfit.core import Analysis, Budget, Cancelled, Config, Edge, FitError, Span, analyze, plans
from musicfit.phase5 import arrange, validate_shortening
from musicfit.render import fit, render
from musicfit.structure import build_structure_hints, _contrast, MAX_BOUNDARIES_PER_SCALE, MAX_ENDINGS
from fixtures import two_sections


def flat_analysis(seconds=80, sr=8000):
    count = round(seconds / .05)
    return Analysis('0'*64,sr,2,round(seconds*sr),.5,.05,
                    np.ones((count,37),np.float32),np.full(count,.1))


def legacy_fixture():
    a=flat_analysis(40)
    idx=np.arange(len(a.energy)) % 160
    a.features=(1+((idx[:,None]+np.arange(37)[None,:])%17)/17).astype(np.float32)
    sr=a.sample_rate
    a.edges=[Edge(4*sr,12*sr,.98,.98),Edge(14*sr,22*sr,.98,.98),Edge(6*sr,22*sr,.98,.98)]
    return a


def canonical(value):
    if isinstance(value,float): return round(value,9)
    if isinstance(value,list): return [canonical(v) for v in value]
    if isinstance(value,dict): return {k:canonical(v) for k,v in value.items()}
    return value


def edit_map_only(plans_dict):
    # Cross-platform BLAS/NumPy can move diagnostic transition scores by tiny
    # floating-point amounts without changing the chosen edit map. The legacy
    # golden is an arrangement-regression guard, so hash only route semantics.
    return [
        {
            'id': p['id'],
            'target_frames': p['target_frames'],
            'spans': p['spans'],
            'ending': p['ending'],
            'strategy': p['strategy'],
        }
        for p in plans_dict
    ]


class StructureTests(unittest.TestCase):
    def test_checkerboard_matches_brute_force_dot_kernel(self):
        x=np.random.default_rng(51).normal(size=(50,8)); width=4
        novelty=_contrast(x,width)
        weights=np.r_[-np.ones(width),np.ones(width)]/width
        for i in range(width,len(x)-width):
            block=x[i-width:i+width]
            expected=weights @ (block @ block.T) @ weights / x.shape[1]
            self.assertAlmostEqual(novelty[i],expected,places=12)

    def test_boundaries_detect_single_modality(self):
        for cue in ('chroma','timbre','energy'):
            with self.subTest(cue=cue):
                a=flat_analysis(40); i=400
                if cue=='energy': a.energy[i:]=.4
                elif cue=='chroma': a.features[i:,:12]+=2
                else: a.features[i:,12:25]+=2
                hints=build_structure_hints(a)
                for scale in ('fine','coarse'):
                    found=[h for h in hints.boundaries if h.scale==scale]
                    self.assertTrue(any(abs(h.frame-20*a.sample_rate)<a.sample_rate
                                        and cue in h.cues for h in found),found)

    def test_ambient_does_not_invent_salient_boundaries(self):
        a=flat_analysis()
        a.features[:,:12]=np.linspace(0,1,len(a.energy))[:,None]
        a.features[:,12:]=0
        self.assertEqual(build_structure_hints(a).boundaries,[])

    def test_silence_no_semantic_claim(self):
        a=flat_analysis();a.features[:]=0;a.energy[:]=0
        h=build_structure_hints(a)
        self.assertFalse(h.boundaries);self.assertFalse(h.highlights)
        self.assertFalse(h.to_dict()['calibrated_confidence'])

    def test_hints_do_not_mutate_analysis(self):
        a=legacy_fixture();x=a.features.copy();e=a.energy.copy();edges=list(a.edges)
        first=build_structure_hints(a).to_dict();second=build_structure_hints(a).to_dict()
        self.assertEqual(first,second);self.assertTrue(np.array_equal(a.features,x))
        self.assertTrue(np.array_equal(a.energy,e));self.assertEqual(a.edges,edges)
        json.dumps(first,allow_nan=False)

    def test_ending_fixed_candidates_and_caller_protection(self):
        a=flat_analysis();h=build_structure_hints(a)
        for seconds in (8,12,16):
            self.assertTrue(any(abs(e.frame-(a.frames-seconds*a.sample_rate))<.08*a.sample_rate
                                for e in h.ending_entries))
        h=build_structure_hints(a,Config(keep_outro_seconds=20))
        self.assertTrue(h.ending_entries)
        self.assertTrue(all(a.frames-e.frame>=20*a.sample_rate for e in h.ending_entries))

    def test_bounded_long_hint_output(self):
        a=flat_analysis(300);a.features=np.random.default_rng(81).normal(size=(6000,37))
        h=build_structure_hints(a)
        self.assertLessEqual(len(h.boundaries),2*MAX_BOUNDARIES_PER_SCALE)
        self.assertLessEqual(len(h.ending_entries),MAX_ENDINGS)
        self.assertLessEqual(len(h.sections),MAX_BOUNDARIES_PER_SCALE+1)

    def test_invalid_features_and_cancel(self):
        a=flat_analysis();a.features[3,4]=np.nan
        with self.assertRaises(FitError): build_structure_hints(a)
        with self.assertRaises(Cancelled): build_structure_hints(flat_analysis(),budget=Budget(cancel=lambda:True))


class PlannerTests(unittest.TestCase):
    def test_one_family_exact_source_ending_and_forward_only(self):
        a=legacy_fixture();a.features[:]=1
        result=arrange(a,26.713)
        self.assertEqual(len(result.candidates),1)
        p=result.candidates[0]
        self.assertEqual(p.target_frames,round(26.713*a.sample_rate))
        self.assertEqual(sum(s.end-s.start for s in p.spans),p.target_frames)
        self.assertEqual(p.ending,'source_end')
        self.assertEqual(p.spans[-1].end,a.frames)
        self.assertTrue(all(r.start>l.end for l,r in zip(p.spans,p.spans[1:])))
        self.assertGreaterEqual(a.frames-p.spans[-1].start,8*a.sample_rate)
        self.assertEqual(result.diagnostics['implemented_families'],['structure_preserve'])
        self.assertEqual(p.strategy,'structure_preserve')

    def test_fade_only_after_source_end_search_fails(self):
        a=flat_analysis();a.features=np.random.default_rng(84).normal(size=a.features.shape)
        result=arrange(a,35.713)
        self.assertTrue(result.candidates)
        self.assertEqual(result.candidates[0].ending,'fade')
        self.assertEqual(result.diagnostics['tier'],'fade_fallback')
        self.assertTrue(all(not step['found'] for step in result.diagnostics['search_attempts'][:-1]))
        self.assertIn('bounded_search',result.diagnostics['fallback_reason'])

    def test_short_target_uses_shorter_tail_explicitly(self):
        result=arrange(flat_analysis(),5)
        self.assertEqual(result.candidates[0].ending,'source_end')
        self.assertEqual(result.diagnostics['tier'],'shorter_source_tail')
        self.assertTrue(result.diagnostics['fallback_reason'])

    def test_impossible_protection_is_not_clamped(self):
        with self.assertRaisesRegex(FitError,'protected_prefix_suffix'):
            arrange(flat_analysis(),5,Config(keep_intro_seconds=4,keep_outro_seconds=4))

    def test_cancellation_and_timeout_never_become_fade(self):
        for b,kind in [(Budget(cancel=lambda:True),Cancelled),
                       (Budget(seconds=.001,started=time.monotonic()-5),FitError)]:
            with self.assertRaises(kind): arrange(flat_analysis(),30,budget=b)

    def test_extend_is_explicit_legacy_delegation(self):
        a=legacy_fixture();result=arrange(a,49.35)
        self.assertEqual([p.to_dict() for p in result.candidates],
                        [p.to_dict() for p in plans(a,49.35,phase=4)])
        self.assertEqual(result.diagnostics['implementation'],'legacy_phase4_delegate')
        self.assertIsNone(result.hints)

    def test_exact_same_length_does_not_reconstruct(self):
        a=flat_analysis();result=arrange(a,80)
        self.assertEqual(result.candidates[0].spans,[Span(0,a.frames)])
        self.assertEqual(result.diagnostics['mode'],'unchanged')

    def test_not_approximately_equal_duration(self):
        a=flat_analysis();p=arrange(a,80-1/a.sample_rate).candidates[0]
        self.assertEqual(p.target_frames,a.frames-1)
        self.assertEqual(p.ending,'source_end')
        self.assertEqual(sum(s.end-s.start for s in p.spans),a.frames-1)

    def test_invalid_phase_values_rejected(self):
        for phase in (True,4.0,5.0,1,2,6,'5'):
            with self.assertRaises(FitError): plans(flat_analysis(),40,phase=phase)

    def test_legacy_golden_edit_maps(self):
        data=json.loads((Path(__file__).with_name('legacy_phase34_golden.json')).read_text(encoding='utf-8'))
        self.assertEqual(data['baseline_blob'],'9a73c5bc1c29196df594e20b8b25fd8d6ab41c95')
        a=legacy_fixture();c=Config(max_jumps=8,beam_width=8)
        for row in data['cases']:
            result=[p.to_dict() for p in plans(a,row['seconds'],c,phase=row['phase'])]
            actual=hashlib.sha256(json.dumps(edit_map_only(result),sort_keys=True,separators=(',',':')).encode()).hexdigest()
            self.assertEqual(actual,row['edit_map_sha256'],result)


class Phase5IntegrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
        self.path=self.root/'音源.wav';x,_=two_sections()
        sf.write(self.path,x,16000,subtype='PCM_24')
    def tearDown(self): self.tmp.cleanup()

    def test_waveform_to_original_ending_and_reopened_wav(self):
        before=self.path.read_bytes();r=fit(self.path,30,self.root/'fit',phase=5)
        self.assertEqual(r['status'],'needs_listening_review')
        self.assertIsNone(r['quality']['human_acceptance_rate'])
        self.assertEqual(len(r['candidates']),1)
        p=r['candidates'][0];self.assertEqual(p['plan']['ending'],'source_end')
        info=sf.info(self.root/'fit'/p['render']['path'])
        self.assertEqual(info.frames,30*16000);self.assertEqual(info.channels,2)
        self.assertEqual(info.subtype,'PCM_24')
        samples,_=sf.read(self.root/'fit'/p['render']['path'],always_2d=True)
        self.assertTrue(np.isfinite(samples).all());self.assertLessEqual(abs(samples).max(),.98)
        self.assertLess(abs(samples[:,1]-.89*samples[:,0]).max(),1e-5)
        self.assertEqual(before,self.path.read_bytes())
        self.assertTrue(r['structure_hints']['boundaries'])
        self.assertEqual(r['planner']['selected_ending']['source_end_frame'],46*16000)
        self.assertIn('解析・構成',(self.root/'fit'/'review.html').read_text(encoding='utf-8'))
        self.assertEqual(json.loads((self.root/'fit'/'result.json').read_text(encoding='utf-8'))['phase'],5)

    def test_worker_phase5_utf8(self):
        req={'schema':'musicfit/v1','input':str(self.path),'target_seconds':30,
             'output_dir':str(self.root/'cli'),'phase':5}
        proc=subprocess.run([sys.executable,'-m','musicfit'],input=json.dumps(req,ensure_ascii=False),
                            encoding='utf-8',capture_output=True,timeout=30)
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        self.assertEqual(json.loads(proc.stdout)['candidate_count'],1)

    def test_source_change_and_cancel_cleanup(self):
        a=analyze(self.path);p=plans(a,30,phase=5)[0]
        output=self.root/'cancel.wav'
        calls=[0]
        def stop():
            calls[0]+=1;return calls[0]>8
        with self.assertRaises(Cancelled): render(self.path,a,p,output,budget=Budget(cancel=stop))
        self.assertFalse(output.exists());self.assertFalse(list(self.root.glob('.render-*')))
        x,_=sf.read(self.path);sf.write(self.path,x*.5,16000)
        with self.assertRaisesRegex(FitError,'source_changed'): render(self.path,a,p,output)
        self.assertFalse(output.exists())

    def test_no_success_directory_on_cancel(self):
        output=self.root/'failed'
        with self.assertRaises(Cancelled):
            fit(self.path,30,output,phase=5,budget=Budget(cancel=lambda:True))
        self.assertFalse(output.exists());self.assertFalse(list(self.root.glob('.musicfit-*')))

    def test_local_runner_preserves_report_and_bad_input_isolated(self):
        path=Path(__file__).resolve().parents[2]/'musicfit-local-realworld-001'/'run_local_test.py'
        spec=importlib.util.spec_from_file_location('local_runner_phase5_test',path)
        runner=importlib.util.module_from_spec(spec);spec.loader.exec_module(runner)
        settings=runner.validate_settings({'targets_seconds':[30],'phase':5})
        for phase in (1,2,True,5.0):
            with self.assertRaises(ValueError): runner.validate_settings({'phase':phase})
        run=self.root/'run';work=self.root/'work';work.mkdir()
        task=runner.build_task(self.path,30,1,run,work,settings)
        self.assertEqual(task['status'],'ok',task)
        report=json.loads((run/task['core_result']).read_text(encoding='utf-8'))
        self.assertIn('structure_hints',report)
        report_dir=(run/task['core_result']).parent
        for row in report['candidates']:
            self.assertTrue((report_dir/row['render']['path']).is_file())
            for preview in row['previews']:
                self.assertTrue((report_dir/preview['path']).is_file())
        self.assertEqual(task['ending_preview_seconds'],24)
        self.assertFalse(list(work.iterdir()))
        bad=self.root/'broken.mp3';bad.write_bytes(b'not-an-mp3')
        failure=runner.build_task(bad,30,2,run,work,settings)
        self.assertEqual(failure['status'],'error')
        self.assertEqual(failure['error_stage'],'input_decode')
        page=runner.review_html({'tasks':[task,failure],'core_version':'test','schema':'test'})
        self.assertIn('core-result.json',page);self.assertIn('Ending 24秒',page)


if __name__=='__main__': unittest.main()
