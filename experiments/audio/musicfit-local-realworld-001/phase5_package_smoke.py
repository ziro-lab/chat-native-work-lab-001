"""Exercise the assembled package, not just the repository import path."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

import numpy as np
import scipy
import soundfile as sf


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--package',type=Path,required=True)
    args=parser.parse_args();package=args.package.resolve()
    here=Path(__file__).resolve().parent
    sys.path.insert(0,str(here.parent/'music-fit-core-001'/'tests'))
    from fixtures import two_sections
    root=here/'_phase5_smoke';inputs=root/'input'
    inputs.mkdir(parents=True,exist_ok=False)
    source=inputs/'synthetic-bgm.wav';samples,_=two_sections()
    sf.write(source,samples,16000,subtype='PCM_24')
    before=hashlib.sha256(source.read_bytes()).hexdigest()
    (inputs/'broken.mp3').write_bytes(b'not-an-mp3')
    settings=root/'settings.json'
    settings.write_text(json.dumps({'targets_seconds':[30],'phase':5}),encoding='utf-8')
    run_env={**os.environ,'PYTHONDONTWRITEBYTECODE':'1'}
    subprocess.run([sys.executable,str(package/'run_local_test.py'),
                    '--input',str(inputs),'--output',str(root/'output'),
                    '--settings',str(settings),'--no-open'],check=True,timeout=180,env=run_env)
    runs=list((root/'output').glob('run-*'));assert len(runs)==1
    run=runs[0];manifest=json.loads((run/'manifest.json').read_text(encoding='utf-8'))
    assert manifest['success_count']==1 and manifest['error_count']==1
    assert manifest['core_version']=='0.2.0a1'
    task=next(t for t in manifest['tasks'] if t['status']=='ok')
    bad=next(t for t in manifest['tasks'] if t['status']=='error')
    assert bad['error_stage']=='input_decode'
    assert len(task['candidates'])==1
    report_path=run/task['core_result']
    report=json.loads(report_path.read_text(encoding='utf-8'))
    assert report['planner']['implementation']=='shared_graph_forward_only'
    assert report['planner']['selected_ending']['source_end_frame']==46*16000
    assert report['structure_hints']['boundaries']
    for row in report['candidates']:
        wav=report_path.parent/row['render']['path'];info=sf.info(wav)
        assert info.frames==30*16000 and info.channels==2 and info.subtype=='PCM_24'
        assert row['plan']['ending']=='source_end'
        for clip in row['previews']:
            assert (report_path.parent/clip['path']).is_file()
    assert hashlib.sha256(source.read_bytes()).hexdigest()==before
    assert not list(package.rglob('*.pyc'))
    assert (package/'run_phase5_windows.bat').is_file()
    assert json.loads((package/'settings.phase5.json').read_text())['phase']==5
    def git(*args):
        p=subprocess.run(['git',*args],capture_output=True,text=True)
        return p.stdout.strip() if p.returncode==0 else None
    evidence={'schema':'musicfit-phase5-package-smoke/v1',
              'source_head':os.environ.get('SOURCE_HEAD_SHA'),
              'actual_checkout':git('rev-parse','HEAD'),
              'actual_tree':git('rev-parse','HEAD^{tree}'),
              'run_id':os.environ.get('GITHUB_RUN_ID'),
              'runtime':{'python':platform.python_version(),'platform':platform.system(),
                         'numpy':np.__version__,'scipy':scipy.__version__,'soundfile':sf.__version__},
              'assertions':{'packaged_phase5_worker':True,'exact_480000_frames':True,
                            'source_end':True,'stereo_pcm24':True,'only_one_family':True,
                            'retained_report_paths':True,'bad_input_isolated':True,
                            'source_unchanged':True,'no_pyc_in_package':True},
              'planner':report['planner'],'human_acceptance':None}
    (here/'phase5-package-evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
    build={k:evidence[k] for k in ('source_head','actual_checkout','actual_tree','run_id','runtime')}
    build['version']='0.2.0a1';build['scope']='Phase5 Checkpoint A, not full Phase5'
    (package/'BUILD_INFO.json').write_text(json.dumps(build,indent=2)+'\n',encoding='utf-8')
    print('PASS_MUSICFIT_PACKAGED_PHASE5_A')


if __name__=='__main__': main()
