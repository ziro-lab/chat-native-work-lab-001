"""Bounded synthetic desktop-use smoke: ~2min stereo 48k -> 10min output."""
import json,sys,time,tempfile
from pathlib import Path
import soundfile as sf
from musicfit.core import Budget, Config, analyze, plans
from musicfit.render import render
sys.path.insert(0,str(Path(__file__).parent/'tests'))
from fixtures import song

def main():
    with tempfile.TemporaryDirectory() as tmp:
        src=Path(tmp)/'source.wav';dest=Path(tmp)/'output.wav'
        x,h=song(sr=48000,cycles=14)
        sf.write(src,x,48000,subtype='PCM_24');del x
        b=Budget(90);t=time.monotonic();a=analyze(src,budget=b);analyze_s=time.monotonic()-t
        t=time.monotonic();p=plans(a,600.137,budget=b);plan_s=time.monotonic()-t
        assert p
        t=time.monotonic();m=render(src,a,p[0],dest,budget=b);render_s=time.monotonic()-t
        assert m['frames']==round(600.137*48000) and m['channels']==2
        result={'source_seconds':a.frames/a.sample_rate,'target_seconds':600.137,'sample_rate':48000,
            'channels':2,'analysis_seconds':analyze_s,'plan_seconds':plan_s,'render_seconds':render_s,
            'candidate_count':len(p),'selected_spans':len(p[0].spans),'output_frames':m['frames'],
            'output_bytes':dest.stat().st_size,'sample_exact':True,'synthetic_only':True}
        try:
            import resource
            result['process_peak_rss_kib']=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
        except ImportError: pass
        print(json.dumps(result,indent=2))
        Path('scale-report.json').write_text(json.dumps(result,indent=2)+'\n')
if __name__=='__main__':main()
