from __future__ import annotations

import hashlib, io, json, math, shutil, sys, tempfile, time
from collections import defaultdict
from dataclasses import replace
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

ROOT=Path(__file__).resolve().parent
AUDIO_ROOT=ROOT.parent
sys.path.insert(0,str(AUDIO_ROOT/"fsld-commercial-safe-index-001"))
sys.path.insert(0,str(AUDIO_ROOT/"commercial-safe-corpus-001"))
sys.path.insert(0,str(AUDIO_ROOT/"music-fit-core-001"))
from remote_zip import RemoteZip
from filter_fsld_metadata import filter_metadata
from musicfit.core import Budget, Config, analyze

URL="https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
THRESHOLDS=(0.66,0.70,0.74,0.78,0.82)
TRAIN_PER_LICENSE=24
VAL_PER_LICENSE=12
TRAIN_CANDIDATES_PER_LICENSE=40
VAL_CANDIDATES_PER_LICENSE=20
MAX_MEMBER_BYTES=16*1024*1024
MAX_NETWORK_BYTES=512*1024*1024
SEED="musicfit-min-sim-tuning-v1"

def stable(*parts):
    return hashlib.sha256(":".join(map(str,parts)).encode()).hexdigest()

def first(d,*keys):
    if not isinstance(d,dict): return None
    for k in keys:
        if d.get(k) not in (None,"",0,"0"): return d[k]
    return None

def bpm_of(row):
    ann=row.get("annotations") if isinstance(row,dict) else {}
    ann=ann if isinstance(ann,dict) else {}
    value=first(ann,"bpm","tempo") or first(row,"bpm","tempo")
    try: x=float(value)
    except (TypeError,ValueError): return None
    return x if math.isfinite(x) and x>0 else None

def audio_entries(rz):
    result={}
    for name in rz.entries:
        base=name.rsplit("/",1)[-1]
        if not base.lower().endswith(".wav"): continue
        head=base.split("_",1)[0].split(".",1)[0]
        if head.isdigit(): result[head]=name
    return result

def normalize(x,peak=.82):
    y=np.asarray(x,dtype=np.float32).copy()
    m=float(np.max(np.abs(y))) if y.size else 0
    if m>1e-9: y*=np.float32(peak/m)
    return y

def variants(loop,sid):
    rng=np.random.default_rng(int(hashlib.sha256(sid.encode()).hexdigest()[:8],16))
    a=normalize(loop,.82)
    b=signal.sosfilt(signal.butter(1,.86,output="sos"),loop,axis=0).astype(np.float32)
    b=np.tanh(b*np.float32(1.08)).astype(np.float32)
    b+=rng.normal(0,1.2e-4,size=b.shape).astype(np.float32); b=normalize(b,.79)
    c=signal.lfilter(np.array([1.,-.035],np.float32),np.array([1.],np.float32),loop,axis=0).astype(np.float32)
    if c.ndim==1: c=c[:,None]
    c*=np.linspace(.96,1.04,c.shape[1],dtype=np.float32)[None,:]
    c=np.tanh(c*np.float32(1.04)).astype(np.float32); c=normalize(c,.84)
    return a,b,c

def pseudo(loop,sr,sid):
    a,b,c=variants(loop,sid); period=len(loop); flank=max(1,period//4)
    intro=b[:flank].copy(); outro=c[-flank:].copy()
    intro*=np.linspace(0,1,len(intro),dtype=np.float32)[:,None]
    outro*=np.linspace(1,0,len(outro),dtype=np.float32)[:,None]
    return normalize(np.concatenate([intro,a,b,c,outro]),.88), period

def decode(data):
    with sf.SoundFile(io.BytesIO(data)) as f:
        sr=f.samplerate; ch=f.channels; frames=f.frames
        x=f.read(dtype="float32",always_2d=True)
    if len(x)!=frames or ch not in (1,2) or not np.all(np.isfinite(x)): raise ValueError("bad_audio")
    return sr,x

def near_integer(v):
    n=round(v)
    return n if n>=4 and abs(v-n)<=max(.08,.04*n) else None

def score_edges(edges,sr,period,bpm):
    strict=[abs((e.end-e.start)/sr-period/sr)<=.070 for e in edges]
    soft=[]
    for e,st in zip(edges,strict):
        seconds=(e.end-e.start)/sr
        beats=seconds*bpm/60 if bpm else None
        soft.append(st or (beats is not None and near_integer(beats) is not None))
    return {
        "strict_top1":bool(strict and strict[0]),
        "strict_top3":any(strict[:3]),
        "soft_top1":bool(soft and soft[0]),
        "soft_top3":any(soft[:3]),
        "no_edge":not edges,
    }

def aggregate(rows):
    n=len(rows)
    return {k:sum(bool(r[k]) for r in rows)/n for k in ("strict_top1","strict_top3","soft_top1","soft_top3","no_edge")}

def main():
    out=ROOT/"out"
    if out.exists(): shutil.rmtree(out)
    out.mkdir()
    hold=json.loads((AUDIO_ROOT/"fsld-holdout-label-audit-001"/"holdout-baseline.json").read_text())
    forbidden_creators={str(x["creator"]) for x in hold["tracks"] if x.get("creator")}

    rz=RemoteZip(URL)
    mn=next(n for n in rz.entries if n.lower().endswith("/metadata.json") or n.lower()=="metadata.json")
    raw=json.loads(rz.read(mn,max_uncompressed=48*1024*1024).decode())
    if isinstance(raw,dict):
        byid={str(k):v for k,v in raw.items() if isinstance(v,dict)}; vals=raw.values()
    else:
        byid={}; vals=raw
    for row in vals:
        if isinstance(row,dict):
            sid=first(row,"id","sound_id","fs_id","freesound_id")
            if sid is not None: byid[str(sid)]=row

    safe=filter_metadata(raw)["accepted"]; amap=audio_entries(rz)
    groups={"CC0-1.0":[],"CC-BY-3.0":[]}
    for row in safe:
        lic=row["canonical_license"]; creator=str(row.get("creator") or "")
        if lic not in groups or not creator or creator in forbidden_creators: continue
        sid=str(row["id"]); member=amap.get(sid); bpm=bpm_of(byid.get(sid,{}))
        if not member or not bpm: continue
        entry=rz.entries[member]
        if 64*1024<=entry.uncompressed_size<=MAX_MEMBER_BYTES:
            groups[lic].append((stable(SEED,lic,creator,sid),creator,sid,member,bpm))
    for lic in groups: groups[lic].sort()

    # Reserve more creator-disjoint candidates than needed because FSLD metadata
    # does not expose the converted WAV duration. Acceptance into train/validation
    # is based only on decode/shape/duration policy, never benchmark performance.
    selected={"train":[],"validation":[]}; used=set(forbidden_creators)
    for lic,candidates in groups.items():
        targets=(("train",TRAIN_CANDIDATES_PER_LICENSE),("validation",VAL_CANDIDATES_PER_LICENSE))
        cursor=0
        for split,count in targets:
            while len([x for x in selected[split] if x["license"]==lic])<count:
                if cursor>=len(candidates): raise RuntimeError(f"insufficient_distinct_creators:{lic}:{split}")
                _,creator,sid,member,bpm=candidates[cursor]; cursor+=1
                if creator in used: continue
                used.add(creator)
                selected[split].append({"id":sid,"creator":creator,"license":lic,"member":member,"bpm":bpm})

    results={split:{str(t):[] for t in THRESHOLDS} for split in selected}
    source_records=[]; accepted={split:defaultdict(int) for split in selected}
    cfg=Config()
    with tempfile.TemporaryDirectory(prefix="musicfit-tune-") as td:
        td=Path(td)
        for split in ("train","validation"):
            target_count=TRAIN_PER_LICENSE if split=="train" else VAL_PER_LICENSE
            for item in selected[split]:
                lic=item["license"]
                if accepted[split][lic]>=target_count: continue
                try:
                    data=rz.read(item["member"],max_uncompressed=MAX_MEMBER_BYTES)
                    sr,loop=decode(data)
                except Exception:
                    continue
                dur=len(loop)/sr
                if not 2.0<=dur<=30:
                    continue
                song,period=pseudo(loop,sr,item["id"])
                path=td/f"{item['id']}.wav"; sf.write(path,song,sr,subtype="PCM_24")
                rec={"split":split,**item,"source_seconds":dur,"thresholds":{}}
                for threshold in THRESHOLDS:
                    a=analyze(path,replace(cfg,min_similarity=threshold),Budget(45))
                    m=score_edges(a.edges,sr,period,item["bpm"])
                    results[split][str(threshold)].append(m)
                    rec["thresholds"][str(threshold)]={**m,"edge_count":len(a.edges)}
                source_records.append(rec); accepted[split][lic]+=1
                path.unlink(missing_ok=True)
                if rz.fetched_bytes>MAX_NETWORK_BYTES: raise RuntimeError("network_budget_exceeded")
            for lic in groups:
                if accepted[split][lic] != target_count:
                    raise RuntimeError(f"not_enough_valid_sources:{split}:{lic}:{accepted[split][lic]}<{target_count}")

    metrics={split:{t:aggregate(rows) for t,rows in vals.items()} for split,vals in results.items()}
    train=metrics["train"]
    # Primary soft Top3, then strict Top3, then fewer abstentions, then more conservative threshold.
    chosen=max(THRESHOLDS,key=lambda t:(train[str(t)]["soft_top3"],train[str(t)]["strict_top3"],-train[str(t)]["no_edge"],t))
    baseline=0.78
    report={
        "schema":"fsld-min-sim-tuning/v1",
        "leakage_controls":{"holdout_used_for_selection":False,"holdout_creator_count":len(forbidden_creators),
            "one_source_per_creator":True,"train_validation_creator_overlap":False},
        "thresholds":list(THRESHOLDS),"chosen_on_train":chosen,"baseline_threshold":baseline,
        "train_count":sum(accepted["train"].values()),"validation_count":sum(accepted["validation"].values()),
        "metrics":metrics,
        "validation_delta_vs_baseline":{
            k:metrics["validation"][str(chosen)][k]-metrics["validation"][str(baseline)][k]
            for k in ("strict_top1","strict_top3","soft_top1","soft_top3","no_edge")
        },
        "network_bytes_fetched":rz.fetched_bytes,"max_network_bytes":MAX_NETWORK_BYTES,
        "sources":source_records,
        "interpretation":"Only the existing minimum-similarity scalar was tuned; no feature/model change occurred. Holdout remained untouched."
    }
    (out/"report.json").write_text(json.dumps(report,indent=2,ensure_ascii=False,allow_nan=False)+"\n")
    print(json.dumps({k:report[k] for k in ("chosen_on_train","baseline_threshold","train_count","validation_count","metrics","validation_delta_vs_baseline","network_bytes_fetched")},indent=2))

if __name__=="__main__": main()
