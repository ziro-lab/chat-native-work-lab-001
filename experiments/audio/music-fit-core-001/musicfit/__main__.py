"""JSON CLI for an out-of-process YMM4 adapter. No network, no shell commands."""
from __future__ import annotations
import json
import signal
import sys
from dataclasses import fields
from pathlib import Path
from .core import Budget, Cancelled, Config, FitError, SCHEMA
from .render import fit


def main():
    cancelled=False
    def cancel(_sig,_frame):
        nonlocal cancelled
        cancelled=True
    signal.signal(signal.SIGINT,cancel)
    if hasattr(signal,'SIGTERM'): signal.signal(signal.SIGTERM,cancel)
    try:
        if len(sys.argv)>2: raise FitError('usage:python_-m_musicfit_[request.json]')
        text=Path(sys.argv[1]).read_text(encoding='utf-8') if len(sys.argv)==2 else sys.stdin.read(65537)
        if len(text)>65536: raise FitError('request_too_large')
        request=json.loads(text)
        allowed={'schema','input','target_seconds','output_dir','phase','config','budget_seconds','cancel_file'}
        if not isinstance(request,dict) or set(request)-allowed or request.get('schema')!=SCHEMA:
            raise FitError('invalid_request_schema')
        options=request.get('config',{})
        if not isinstance(options,dict) or set(options)-{f.name for f in fields(Config)}:
            raise FitError('invalid_config_fields')
        c=Config(**options); c.validate()
        timeout=request.get('budget_seconds',120)
        if isinstance(timeout,bool) or not isinstance(timeout,(int,float)) or not 1<=timeout<=600:
            raise FitError('invalid_time_budget')
        cancel_file=Path(request['cancel_file']) if request.get('cancel_file') else None
        b=Budget(timeout,lambda:cancelled or (cancel_file is not None and cancel_file.exists()))
        report=fit(Path(request['input']),request['target_seconds'],Path(request['output_dir']),c,b,request.get('phase',4))
        # Full portable edit map/results live beside the audio; keep IPC response compact.
        print(json.dumps({'schema':SCHEMA,'status':report['status'],'candidate_count':len(report['candidates']),
                          'result_file':str(Path(request['output_dir'])/'result.json'),
                          'review_file':str(Path(request['output_dir'])/'review.html')},ensure_ascii=False,allow_nan=False))
        return 0
    except Cancelled as e:
        print(json.dumps({'schema':SCHEMA,'status':'cancelled','error':str(e)})); return 130
    except (FitError,ValueError,KeyError,TypeError,OSError,RuntimeError) as e:
        print(json.dumps({'schema':SCHEMA,'status':'error','error':str(e)},ensure_ascii=False)); return 2

if __name__=='__main__': sys.exit(main())
