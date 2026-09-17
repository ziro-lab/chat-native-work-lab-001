import json,os,subprocess,sys,tempfile,unittest
from pathlib import Path
import numpy as np
import soundfile as sf
from musicfit.core import Config,Edge,analyze,plans
from fixtures import song

class WorkerTests(unittest.TestCase):
    def test_protected_prefix_not_clamped(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'in.wav';x,h=song();sf.write(p,x,16000)
            a=analyze(p);a.edges=[Edge(4*16000,12*16000,.98,.98)]
            self.assertFalse(plans(a,50,Config(keep_intro_seconds=7)))
    def test_noise_has_no_strong_recurrence(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'in.wav';rng=np.random.default_rng(998)
            sf.write(p,rng.normal(0,.05,(16000*12,2)),16000)
            self.assertFalse(analyze(p).edges)
    def test_cli_utf8_paths_even_with_legacy_pipe_encoding(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'音楽 入力.wav';x,h=song();sf.write(p,x,16000)
            out=Path(tmp)/'試聴 出力'
            req={'schema':'musicfit/v1','input':str(p),'output_dir':str(out),'target_seconds':15.375}
            env={**os.environ,'PYTHONIOENCODING':'cp1252'}
            r=subprocess.run([sys.executable,'-m','musicfit'],input=json.dumps(req,ensure_ascii=False),
                encoding='utf-8',capture_output=True,env=env,timeout=30)
            self.assertEqual(r.returncode,0,r.stderr+r.stdout)
            answer=json.loads(r.stdout)
            self.assertTrue(Path(answer['result_file']).is_file())
    def test_cli_error_is_json(self):
        r=subprocess.run([sys.executable,'-m','musicfit'],input='{"schema":"bad"}',
            encoding='utf-8',capture_output=True,timeout=10)
        self.assertEqual(r.returncode,2)
        self.assertEqual(json.loads(r.stdout)['status'],'error')
