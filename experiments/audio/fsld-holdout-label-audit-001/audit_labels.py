from __future__ import annotations
import json, math, re, sys
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parent
AUDIO_ROOT=ROOT.parent
sys.path.insert(0,str(AUDIO_ROOT/"fsld-commercial-safe-index-001"))
from remote_zip import RemoteZip

URL="https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
TOL=0.070

def first(d,*keys):
    if not isinstance(d,dict): return None
    for k in keys:
        if d.get(k) not in (None,"",0,"0"): return d[k]
    return None

def num(v):
    try: x=float(v)
    except (TypeError,ValueError): return None
    return x if math.isfinite(x) and x>0 else None

def meter(v):
    if v is None: return None
    m=re.match(r"^(\d+)\s*/\s*(\d+)$",str(v).strip())
    if m: return int(m.group(1))
    m=re.match(r"^(\d+)$",str(v).strip())
    return int(m.group(1)) if m else None

def near_int(x, rel=.04):
    n=round(x)
    return n if n>0 and abs(x-n)<=max(.08,rel*n) else None

def ann_fields(row):
    ann=row.get("annotations") if isinstance(row,dict) else {}
    ann=ann if isinstance(ann,dict) else {}
    bpm=num(first(ann,"bpm","tempo") or first(row,"bpm","tempo"))
    sig=first(ann,"signature","time_signature","meter")
    if sig is None: sig=first(row,"signature","time_signature","meter")
    wc=first(ann,"well_cut")
    if wc is None: wc=first(row,"well_cut")
    return {"bpm":bpm,"signature_raw":sig,"meter_numerator":meter(sig),"well_cut":wc,"annotation_keys":sorted(ann.keys())}

def label(p,full,a):
    strict=abs(p-full)<=TOL
    ratio=full/p if p>0 else 0
    factor=near_int(ratio,.018)
    sub=factor is not None and 2<=factor<=8
    bpm=a["bpm"]; be=None; bi=None; beat=False; bars=None; bar=False
    if bpm:
        be=p*bpm/60
        bi=near_int(be)
        beat=bi is not None
        m=a["meter_numerator"]
        if beat and m:
            bars=near_int(bi/m,.001)
            bar=bars is not None and bars>=1
    # "audition_worthy_beat_span" is intentionally a soft product metric, not
    # ground truth: a Top3 recurrence spanning >=4 annotated integer beats is
    # worth previewing even when the publisher supplied a longer loop file.
    audition_worthy = strict or (beat and bi is not None and bi >= 4)
    return {"candidate_seconds":p,"strict_full_period":strict,"publisher_period_ratio":ratio,
            "integer_subperiod_factor":factor if sub else None,"integer_subperiod":sub,
            "annotated_beats_exact":be,"annotated_beats":bi,"beat_compatible":beat,
            "annotated_bars":bars,"bar_compatible":bar,
            "practical_loop_positive":strict or (sub and bar),
            "audition_worthy_beat_span":audition_worthy}

def main():
    base=json.loads((ROOT/"holdout-baseline.json").read_text())
    ids={str(x["id"]) for x in base["tracks"]}
    rz=RemoteZip(URL)
    mn=next(n for n in rz.entries if n.lower().endswith("/metadata.json") or n.lower()=="metadata.json")
    payload=json.loads(rz.read(mn,max_uncompressed=48*1024*1024).decode())
    by={}
    if isinstance(payload,dict):
        by.update({str(k):v for k,v in payload.items() if isinstance(v,dict)})
        vals=payload.values()
    else: vals=payload
    for row in vals:
        if isinstance(row,dict):
            sid=first(row,"id","sound_id","fs_id","freesound_id")
            if sid is not None: by[str(sid)]=row
    miss=sorted(ids-set(by))
    if miss: raise RuntimeError(f"missing_holdout_metadata:{miss}")

    rows=[]; s1=s3=p1=p3=a1=a3=0; bpmN=meterN=0; promoted=[]; audition_promoted=[]; keys=Counter()
    for b in base["tracks"]:
        a=ann_fields(by[str(b["id"])])
        bpmN+=a["bpm"] is not None; meterN+=a["meter_numerator"] is not None
        keys.update(a["annotation_keys"])
        labs=[label(float(p),float(b["loop"]),a) for p in b["top"]]
        s1+=bool(labs and labs[0]["strict_full_period"]); s3+=any(x["strict_full_period"] for x in labs)
        p1+=bool(labs and labs[0]["practical_loop_positive"]); p3+=any(x["practical_loop_positive"] for x in labs)
        a1+=bool(labs and labs[0]["audition_worthy_beat_span"]); a3+=any(x["audition_worthy_beat_span"] for x in labs)
        if labs and labs[0]["practical_loop_positive"] and not labs[0]["strict_full_period"]: promoted.append(str(b["id"]))
        if labs and labs[0]["audition_worthy_beat_span"] and not labs[0]["strict_full_period"]: audition_promoted.append(str(b["id"]))
        rows.append({"id":str(b["id"]),"creator":b["creator"],"license":b["license"],"publisher_loop_seconds":b["loop"],
                     "metadata_annotations":a,"top3_labels":labs})
    n=len(rows)
    frozen1=sum(x["s1"] for x in base["tracks"])/n; frozen3=sum(x["s3"] for x in base["tracks"])/n
    if abs(frozen1-s1/n)>1e-9 or abs(frozen3-s3/n)>1e-9: raise RuntimeError("strict_baseline_changed")
    report={"schema":"fsld-holdout-label-audit/v1","holdout_count":n,"audio_members_read":0,
            "range_requests":rz.range_requests,"network_bytes_fetched":rz.fetched_bytes,
            "bpm_available_count":bpmN,"meter_available_count":meterN,
            "annotation_key_counts":dict(keys.most_common()),
            "strict_full_period_top1":s1/n,"strict_full_period_top3":s3/n,
            "practical_bar_compatible_top1":p1/n,"practical_bar_compatible_top3":p3/n,
            "audition_worthy_beat_span_top1":a1/n,"audition_worthy_beat_span_top3":a3/n,
            "strict_failures_promoted_by_bar_compatible_subperiod":promoted,
            "strict_failures_soft_promoted_for_audition":audition_promoted,"tracks":rows}
    out=ROOT/"out"; out.mkdir(exist_ok=True)
    (out/"report.json").write_text(json.dumps(report,indent=2,ensure_ascii=False,allow_nan=False)+"\n")
    print(json.dumps({k:report[k] for k in ("holdout_count","bpm_available_count","meter_available_count",
        "strict_full_period_top1","strict_full_period_top3","practical_bar_compatible_top1",
        "practical_bar_compatible_top3","audition_worthy_beat_span_top1","audition_worthy_beat_span_top3",
        "strict_failures_promoted_by_bar_compatible_subperiod","strict_failures_soft_promoted_for_audition")},indent=2))
if __name__=="__main__": main()
