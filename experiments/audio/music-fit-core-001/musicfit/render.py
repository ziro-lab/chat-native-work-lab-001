"""Sample-exact streaming renderer; constant-sum crossfades do not shorten the edit map."""
from __future__ import annotations
import hashlib
import html
import json
import os
import shutil
import tempfile
from dataclasses import asdict
from pathlib import Path
import numpy as np
import soundfile as sf
from .core import Analysis, Budget, Config, FitError, Plan, SCHEMA, VERSION, analyze, digest, plans, validate_plan

BLOCK = 65536


def _read(audio, start, frames):
    audio.seek(start)
    x = audio.read(frames, dtype='float32', always_2d=True)
    if len(x) != frames or not np.all(np.isfinite(x)):
        raise FitError('source_read_failed')
    return x


def render(source: Path, analysis: Analysis, plan: Plan, output: Path,
           config: Config | None = None, budget: Budget | None = None):
    c, b = config or Config(), budget or Budget()
    c.validate(); validate_plan(analysis, plan, c); b.check()
    source, output = Path(source), Path(output)
    if source.resolve() == output.resolve() or output.exists():
        raise FitError('refuse_audio_overwrite')
    if digest(source,b) != analysis.source_sha256:
        raise FitError('source_changed_since_analysis')
    sr, written = analysis.sample_rate, 0
    gain = min(1.0, 0.98 / max(analysis.sample_peak, 1e-10))
    ending_fade = round((c.fade_out_seconds if plan.ending == 'fade' else 0.012) * sr)
    ending_fade = min(ending_fade, plan.target_frames // 3)
    fade_start = plan.target_frames - ending_fade
    output.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix='.render-', suffix='.wav', dir=output.parent)
    os.close(fd)
    seams = []; peak = 0.0
    try:
        with sf.SoundFile(str(source)) as audio, sf.SoundFile(tmp, 'w', samplerate=sr,
                       channels=analysis.channels, subtype='PCM_24', format='WAV') as sink:
            if len(audio) != analysis.frames or audio.channels != analysis.channels or audio.samplerate != sr:
                raise FitError('source_metadata_changed')
            for index, span in enumerate(plan.spans):
                b.check()
                count = span.end - span.start
                fade = 0; blend = None
                if index and plan.spans[index-1].end != span.start:
                    previous_end = plan.spans[index-1].end
                    fade = min(round(c.crossfade_seconds * sr), count,
                               analysis.frames-previous_end, max(0,count-1))
                    if fade < 2: raise FitError('insufficient_crossfade_handle')
                    outgoing = _read(audio, previous_end, fade)
                    incoming = _read(audio, span.start, fade)
                    t = np.linspace(0,1,fade,dtype=np.float32)
                    w = t*t*(3-2*t)  # smoothstep; weights sum to one, no +3 dB bump.
                    blend = outgoing * (1-w[:,None]) + incoming*w[:,None]
                    seam = {'output_frame':written,'source_exit_frame':previous_end,
                            'source_entry_frame':span.start,'crossfade_frames':fade,
                            'crossfade_mode':'post_cut_carryover_constant_sum',
                            'source_context_score':plan.transition_scores[index-1]}
                    # Signal jump at the exact cut before/after applying carryover, not human naturalness.
                    prior = _read(audio, previous_end-1,1)[0]
                    seam['hard_cut_step'] = float(np.max(np.abs(incoming[0]-prior)))
                    seam['rendered_cut_step'] = float(np.max(np.abs(blend[0]-prior)))
                    seam['crossfade_peak'] = float(np.max(np.abs(blend)))
                    seams.append(seam)
                for offset in range(0,count,BLOCK):
                    b.check()
                    size = min(BLOCK,count-offset)
                    block = _read(audio,span.start+offset,size)
                    if blend is not None and offset < fade:
                        k = min(size,fade-offset)
                        block[:k] = blend[offset:offset+k]
                    block *= np.float32(gain)
                    end = written + size
                    if ending_fade and end > fade_start:
                        pos = np.arange(written,end)
                        env = np.clip((plan.target_frames-1-pos)/max(1,ending_fade-1),0,1)
                        # Half-cosine ending, all channels use the same envelope.
                        block *= (0.5 - 0.5*np.cos(np.pi*env)).astype(np.float32)[:,None]
                    peak = max(peak,float(np.max(np.abs(block))))
                    if not np.all(np.isfinite(block)): raise FitError('nonfinite_render')
                    sink.write(block)
                    written = end
            if written != plan.target_frames: raise FitError('render_duration_mismatch')
        b.check()
        if digest(source,b) != analysis.source_sha256:
            raise FitError('source_changed_during_render')
        check = sf.info(tmp)
        if check.frames != plan.target_frames or check.channels != analysis.channels:
            raise FitError('output_reopen_mismatch')
        output_digest = digest(Path(tmp),b)
        if peak > 0.98001: raise FitError('output_peak_budget_exceeded')
        if output.exists(): raise FitError('refuse_audio_overwrite')
        os.replace(tmp, output)
    finally:
        if os.path.exists(tmp): os.unlink(tmp)
    return {'path':output.name,'frames':written,'seconds':written/sr,'sample_rate':sr,
            'channels':analysis.channels,'sample_peak':peak,'global_gain':gain,
            'sha256':output_digest,'seams':seams,'ending':plan.ending,
            'ending_fade_frames':ending_fade}


def previews(rendered: Path, metadata: dict, destination: Path, budget: Budget):
    """One short clip per edit and one ending clip; bounded 2 seconds either side."""
    destination.mkdir(parents=True, exist_ok=True)
    result = []
    with sf.SoundFile(str(rendered)) as f:
        sr = f.samplerate
        locations = [('join-%02d'%(i+1),s['output_frame']) for i,s in enumerate(metadata['seams'])]
        locations.append(('ending',len(f)))
        for name, position in locations:
            budget.check()
            start, end = max(0,position-2*sr), min(len(f),position+2*sr)
            if end <= start: continue
            path = destination/(name+'.wav')
            sf.write(str(path),_read(f,start,end-start),sr,subtype='PCM_24')
            result.append({'name':name,'path':str(path.relative_to(destination.parent.parent)).replace(os.sep,'/'),
                           'output_start_frame':start,'output_end_frame':end})
    return result


def review_page(rows):
    blocks=[]
    for row in rows:
        cid = html.escape(row['id'])
        clips=''.join(f'<p>{html.escape(p["name"])} <audio controls preload="none" src="{html.escape(p["path"],quote=True)}"></audio></p>' for p in row['previews'])
        blocks.append(f'<section><h2>{cid}</h2><p>Ending: {html.escape(row["render"]["ending"])}</p>'
                      f'<audio controls preload="none" src="{html.escape(row["render"]["path"],quote=True)}"></audio>'
                      f'{clips}<label>聴感評価 <select data-id="{cid}"><option value="">未評価</option>'
                      '<option value="acceptable">自然で実用可</option><option value="minor">軽い違和感</option>'
                      '<option value="reject">使わない</option></select></label></section>')
    return '''<!doctype html><meta charset="utf-8"><title>Music Fit 試聴</title>
<style>body{max-width:850px;margin:2rem auto;padding:1rem;font-family:system-ui}section{border-top:1px solid;padding:1rem 0}audio{vertical-align:middle;max-width:100%}label{display:block;margin:1rem 0}</style>
<h1>Music Fit 候補の聴き比べ</h1><p>自動判定は自然さの確率ではありません。継ぎ目・曲の流れ・終わり方を確認してください。</p>
''' + ''.join(blocks) + '''<button id="save">評価をJSONで保存</button><script>
document.getElementById('save').onclick=()=>{let ratings=[...document.querySelectorAll('select')].map(s=>({id:s.dataset.id,rating:s.value}));let a=document.createElement('a');let u=URL.createObjectURL(new Blob([JSON.stringify({schema:'musicfit-listening/v1',ratings},null,2)],{type:'application/json'}));a.href=u;a.download='listening-ratings.json';a.click();setTimeout(()=>URL.revokeObjectURL(u),1000);};</script>'''


def fit(source: Path, seconds: float, output_dir: Path, config: Config | None = None,
        budget: Budget | None = None, phase=4):
    c, b = config or Config(), budget or Budget()
    source, output_dir = Path(source), Path(output_dir)
    if output_dir.exists(): raise FitError('output_directory_already_exists')
    a = analyze(source,c,b)
    candidates = plans(a,seconds,c,b,phase)
    if not candidates: raise FitError('no_suitable_plan;try_shorter_duration_or_manual_loop')
    output_dir.parent.mkdir(parents=True,exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix='.musicfit-',dir=output_dir.parent))
    try:
        rows=[]
        for p in candidates:
            rendered=stage/(p.id+'.wav')
            meta=render(source,a,p,rendered,c,b)
            clips=previews(rendered,meta,stage/'previews'/p.id,b)
            rows.append({'id':p.id,'plan':p.to_dict(),'render':meta,'previews':clips})
        report={'schema':SCHEMA,'version':VERSION,'status':'needs_listening_review',
                'source_name':source.name,'analysis':a.summary(),'config':asdict(c),
                'target_seconds':seconds,'phase':phase,'candidates':rows,
                'quality':{'human_acceptance_rate':None,'calibrated_confidence':False},
                'elapsed_seconds':time_elapsed(b)}
        (stage/'result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2,allow_nan=False)+'\n',encoding='utf-8')
        (stage/'review.html').write_text(review_page(rows),encoding='utf-8')
        b.check()
        if output_dir.exists(): raise FitError('output_directory_already_exists')
        os.rename(stage,output_dir)
        return report
    finally:
        if stage.exists(): shutil.rmtree(stage)


def time_elapsed(b):
    import time
    return time.monotonic()-b.started
