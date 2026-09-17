"""Diagnostic measurements, not a listening-acceptance benchmark.
Use --real only in a full checkout: six existing hash-pinned sources are runtime-only.
"""
from __future__ import annotations
import argparse
import importlib.util
import json
import platform
import sys
import time
from dataclasses import asdict
from pathlib import Path
import numpy as np
import scipy
import soundfile as sf
from musicfit.core import Budget, Config, analyze, plans, digest
from musicfit.render import render, fit

ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'tests'))
from fixtures import song


def pinned_real_builder():
    path=ROOT.parent/'audio-real-loop-self-discovery-001'/'run_self_discovery.py'
    spec=importlib.util.spec_from_file_location('pinned_real_fixture',path)
    module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def run(out:Path, real=False):
    if out.exists(): raise ValueError('fresh_output_required')
    out.mkdir(parents=True)
    config=Config()
    source_rows=[]; measurements=[]; started=time.monotonic()
    if real:
        builder=pinned_real_builder()
        inputs=[]
        for source in builder.SOURCES:
            pack=builder.load_pack(source)  # Exact ZIP digest rejection is inherited, not weakened.
            audio,meta=builder.build_pseudo_song(source,pack)
            path=out/(source['id']+'.wav')
            sf.write(path,audio,pack['sr'],subtype='PCM_24')
            inputs.append((source['id'],path,{'sr':pack['sr'],'period':meta['period_frames'],
              'body_start':meta['body_start_frame'],'body_end':meta['body_end_frame']},source['license'],pack['zip_sha256']))
    else:
        inputs=[]
        # Separate generated songs, not random split of derivatives of the same source.
        for index,(speed,drums) in enumerate([(0.37,True),(0.5,True),(0.63,True),(0.5,False)]):
            audio,h=song(seed=index+20,seconds_per_note=speed,drums=drums)
            path=out/f'synthetic-{index}.wav';sf.write(path,audio,h['sr'],subtype='PCM_24')
            inputs.append((f'synthetic-{index}',path,h,'repository-owned',None))
    for ident,path,h,license_id,source_digest in inputs:
        b=Budget(180)
        before=time.monotonic();a=analyze(path,config,b)
        elapsed=time.monotonic()-before
        valid=[e for e in a.edges if abs((e.end-e.start)-h['period'])<=.07*h['sr'] and
               e.start>=h['body_start'] and e.end<=h['body_end']]
        source_rows.append({'id':ident,'source_sha256':a.source_sha256,'source_zip_sha256':source_digest,
            'license':license_id,'source_seconds':a.frames/a.sample_rate,'analysis_seconds':elapsed,
            'edges':len(a.edges),'analysis_warnings':a.warnings,'edge_kinds':sorted({e.kind for e in a.edges}),'strict_known_pair_top1':bool(a.edges and a.edges[0] in valid),
            'strict_known_pair_top3':any(e in valid for e in a.edges[:3]),
            'known_period_seconds':h['period']/h['sr'],
            'top_periods_seconds':[(e.end-e.start)/h['sr'] for e in a.edges[:3]]})
        for factor in (0.68,1.6,2.4):
            seconds=round(a.frames/a.sample_rate*factor+0.137,3)
            before=time.monotonic();candidate_plans=plans(a,seconds,config,b,phase=4)
            plan_seconds=time.monotonic()-before
            rows=[]
            for p in candidate_plans:
                dest=out/f'{ident}-{factor}-{p.id}.wav'
                before=time.monotonic();m=render(path,a,p,dest,config,b);render_seconds=time.monotonic()-before
                rows.append({'plan':p.to_dict(),'render':m,'render_seconds':render_seconds,
                    'distinct_jump_count':len({(l.end,r.start) for l,r in zip(p.spans,p.spans[1:])})})
                # Audio remains runtime-only for real sources. Do not upload it in CI.
            measurements.append({'source':ident,'target_seconds':seconds,'factor':factor,
                'status':'rendered_needs_listening' if rows else 'no_suitable_plan',
                'plan_seconds':plan_seconds,'candidates':rows})
    total=len(measurements)
    successful=sum(bool(m['candidates']) for m in measurements)
    report={'schema':'musicfit-benchmark/v1','kind':'real-derived' if real else 'synthetic',
        'runtime':{'python':platform.python_version(),'numpy':np.__version__,'scipy':scipy.__version__,
                   'soundfile':sf.__version__,'platform':platform.system()},
        'source_count':len(source_rows),'fit_requests':total,'rendered_requests':successful,
        'render_availability':successful/total,'strict_known_pair_top1':sum(r['strict_known_pair_top1'] for r in source_rows)/len(source_rows),
        'strict_known_pair_top3':sum(r['strict_known_pair_top3'] for r in source_rows)/len(source_rows),
        'human_naturalness_acceptance':None,'quality_is_diagnostic':True,
        'multijump_plans':sum(r['distinct_jump_count']>=2 for m in measurements for r in m['candidates']),
        'sample_exact_outputs':all(r['render']['frames']==r['plan']['target_frames'] for m in measurements for r in m['candidates']),
        'config':asdict(config),'sources':source_rows,'measurements':measurements,
        'elapsed_seconds':time.monotonic()-started,
        'not_proven':['85-90% human acceptance','ordinary unmodified-song generalization','YMM4 integration',
                      'Windows executable bundle size','vocal semantic continuity','all perceptual seam artifacts']}
    (out/'report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False,allow_nan=False)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('kind','source_count','fit_requests','rendered_requests','strict_known_pair_top1',
        'strict_known_pair_top3','multijump_plans','sample_exact_outputs','elapsed_seconds')},indent=2))
    # Execution assertions only: low quality must remain visible, not turn into missing data.
    assert total==3*len(inputs)
    assert report['sample_exact_outputs']
    assert all(len(m['candidates'])<=3 for m in measurements)
    return report

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--real',action='store_true');ap.add_argument('--output',type=Path,default=ROOT/'out')
    args=ap.parse_args();run(args.output,args.real)
