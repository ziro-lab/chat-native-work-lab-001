from __future__ import annotations

import hashlib
import html
import importlib.util
import io
import json
import math
import random
import shutil
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
CORE_ROOT = AUDIO_ROOT / "music-fit-core-001"
INDEX_ROOT = AUDIO_ROOT / "fsld-commercial-safe-index-001"
SAFE_ROOT = AUDIO_ROOT / "commercial-safe-corpus-001"
BENCH_PATH = AUDIO_ROOT / "fsld-musicfit-benchmark-001" / "run_benchmark.py"
HOLDOUT_PATH = AUDIO_ROOT / "fsld-holdout-label-audit-001" / "holdout-baseline.json"

sys.path.insert(0, str(CORE_ROOT))
sys.path.insert(0, str(INDEX_ROOT))
sys.path.insert(0, str(SAFE_ROOT))

from musicfit.core import Budget, Config, FitError  # noqa: E402
from musicfit.render import fit  # noqa: E402
from remote_zip import RemoteZip  # noqa: E402
from filter_fsld_metadata import filter_metadata  # noqa: E402

URL = "https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
SOURCE_COUNT = 6
TARGET_FACTORS = (0.72, 1.55)
SEED = "musicfit-human-listening-pack-001"
MAX_MEMBER_BYTES = 16 * 1024 * 1024
MAX_NETWORK_BYTES = 256 * 1024 * 1024

TUNING_IDS = set(["481597","250569","265573","260673","270690","250985","102886","372579","263871","484443","413688","344082","423745","413752","424097","404006","180657","246065","246106","204748","480590","419850","481610","75410","419008","372587","32060","45455","270804","424119","31499","345401","19798","151350","155725","19818","383948","465781","269547","56630","67302","269548","423847","38103","280229","136985","181883","420275","126058","455109","239625","182644","160371","183905","178616","270128","129277","407941","123292","130491","344622","262445","182674","70003"])
TUNING_CREATORS = set([".Andre_Onate","@realdavidfloat","CmdRobot","DirtyJewbs","DiscordantScraps","Glitchedtones","Goup_1","Greek555","Hard3eat","Hoerspielwerkstatt_HEF","Jagadamba","Kevcio","Krishmeister","LS","Natrousoxide","Slaking_97","THE_bizniss","TheFlakesMaster","Uberproduktion","Uknow Dude","VJ_XIXi","Xinematix","_jack","aji_","bOmbhead","bcnlab","bigfriendlyjiant","chipfork","comadjinn","day_tripper13","freestylednb","gis_sweden","hatchetgirl","hello_flowers","j1987","jesuswaffle","jputman","lerwickdj","levite_sound","luketande","mooncubedesign","multitonbits","nowism","orangefreesounds","robcro6010","shitefromaheight","simon wye","stair","visual","waveplay_old"])


def load_builder():
    spec = importlib.util.spec_from_file_location("fsld_fixture", BENCH_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


BUILDER = load_builder()


def stable_key(source_id: str) -> str:
    return hashlib.sha256(f"{SEED}:{source_id}".encode("utf-8")).hexdigest()


def metadata_map(payload):
    result = {}
    if isinstance(payload, dict):
        for key, row in payload.items():
            if isinstance(row, dict):
                result[str(key)] = row
        values = payload.values()
    else:
        values = payload
    for row in values:
        if not isinstance(row, dict):
            continue
        sid = (
            row.get("id")
            or row.get("sound_id")
            or row.get("fs_id")
            or row.get("freesound_id")
        )
        if sid is not None:
            result[str(sid)] = row
    return result


def candidate_letter_order(case_id: str, count: int):
    order = list(range(count))
    rng = random.Random(int(hashlib.sha256(f"{SEED}:{case_id}".encode()).hexdigest()[:16], 16))
    rng.shuffle(order)
    return order


def audio_tag(src: str):
    return f'<audio controls preload="none" src="{html.escape(src, quote=True)}"></audio>'


def build_review(cases):
    blocks = []
    for case in cases:
        source_rel = case["source_reference"]
        rows = []
        for shown in case["blind_candidates"]:
            label = shown["label"]
            full = shown["audio"]
            preview_html = "".join(
                f'<div><small>{html.escape(p["name"])}</small> {audio_tag(p["path"])}</div>'
                for p in shown["previews"]
            )
            key = f'{case["case_id"]}:{label}'
            rows.append(f"""
            <article class="candidate">
              <h3>候補 {label}</h3>
              <p>完成音声 {audio_tag(full)}</p>
              <details><summary>継ぎ目・終端の短い試聴</summary>{preview_html}</details>
              <div class="ratings" data-key="{html.escape(key)}">
                <label>全体
                  <select data-field="overall">
                    <option value="">未評価</option>
                    <option value="usable">そのまま使える</option>
                    <option value="minor">軽い違和感</option>
                    <option value="reject">使わない</option>
                  </select>
                </label>
                <label>継ぎ目
                  <select data-field="seam">
                    <option value="">未評価</option>
                    <option value="clean">自然</option>
                    <option value="noticeable">少し気づく</option>
                    <option value="bad">不自然</option>
                  </select>
                </label>
                <label>曲の流れ
                  <select data-field="flow">
                    <option value="">未評価</option>
                    <option value="natural">自然</option>
                    <option value="minor">少し反復/展開が気になる</option>
                    <option value="bad">不自然</option>
                  </select>
                </label>
                <label>終わり方
                  <select data-field="ending">
                    <option value="">未評価</option>
                    <option value="natural">自然な終止</option>
                    <option value="fade_ok">Fadeだが許容</option>
                    <option value="bad">不自然</option>
                  </select>
                </label>
              </div>
            </article>""")
        blocks.append(f"""
        <section class="case">
          <h2>{html.escape(case["display_name"])}</h2>
          <p>元音源（評価用に生成した単一トラック） {audio_tag(source_rel)}</p>
          <p>目標尺: {case["target_seconds"]:.3f} 秒</p>
          {''.join(rows)}
        </section>""")

    return """<!doctype html><meta charset="utf-8">
<title>Music Fit blind listening review</title>
<style>
body{max-width:1050px;margin:2rem auto;padding:0 1rem;font-family:system-ui,sans-serif}
.case{border-top:3px solid #888;padding:1rem 0 2rem}
.candidate{border:1px solid #aaa;border-radius:10px;padding:1rem;margin:1rem 0}
.ratings{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:.7rem}
label{display:flex;flex-direction:column;gap:.25rem} audio{max-width:100%;vertical-align:middle}
small{display:inline-block;min-width:90px}
button{font-size:1rem;padding:.7rem 1rem;margin:1rem .5rem 1rem 0}
.note{background:#eee;padding:1rem;border-radius:8px}
</style>
<h1>Music Fit ブラインド試聴</h1>
<div class="note">
<p>A/B/Cは自動順位を隠して並べ替えています。まず耳だけで評価してください。</p>
<p>主判定は「3候補のどれかに、そのまま使えるものがあるか」。継ぎ目だけでなく、曲の流れと終わり方も見てください。</p>
</div>
""" + "".join(blocks) + """
<button id="save">評価JSONを保存</button>
<script>
document.getElementById('save').onclick=()=>{
  const ratings=[...document.querySelectorAll('.ratings')].map(div=>{
    const obj={key:div.dataset.key};
    div.querySelectorAll('select').forEach(s=>obj[s.dataset.field]=s.value);
    return obj;
  });
  const payload={schema:'musicfit-human-listening/v1',ratings};
  const blob=new Blob([JSON.stringify(payload,null,2)],{type:'application/json'});
  const a=document.createElement('a'); a.href=URL.createObjectURL(blob);
  a.download='listening-ratings.json'; a.click();
  setTimeout(()=>URL.revokeObjectURL(a.href),1000);
};
</script>
"""


def main():
    out = ROOT / "out"
    if out.exists():
        shutil.rmtree(out)
    pack = out / "listening-pack"
    pack.mkdir(parents=True)

    holdout = json.loads(HOLDOUT_PATH.read_text(encoding="utf-8"))
    excluded_ids = {str(x["id"]) for x in holdout["tracks"]} | TUNING_IDS
    excluded_creators = {str(x["creator"]) for x in holdout["tracks"]} | TUNING_CREATORS

    rz = RemoteZip(URL)
    metadata_name = next(
        n for n in rz.entries
        if n.lower().endswith("/metadata.json") or n.lower() == "metadata.json"
    )
    payload = json.loads(
        rz.read(metadata_name, max_uncompressed=48 * 1024 * 1024).decode("utf-8")
    )
    raw_by_id = metadata_map(payload)
    filtered = filter_metadata(payload)
    audio_map = BUILDER.audio_entries(rz)

    eligible = []
    for row in filtered["accepted"]:
        if row["canonical_license"] != "CC0-1.0":
            continue
        sid = str(row["id"])
        creator = str(row.get("creator") or "")
        if sid in excluded_ids or creator in excluded_creators:
            continue
        member = audio_map.get(sid)
        raw = raw_by_id.get(sid)
        if member is None or raw is None:
            continue
        entry = rz.entries[member]
        if not (64 * 1024 <= entry.uncompressed_size <= MAX_MEMBER_BYTES):
            continue
        eligible.append((stable_key(sid), row, raw, member))
    eligible.sort(key=lambda x: x[0])

    selected_sources = []
    cases = []
    unblind = {"schema": "musicfit-human-listening-unblind/v1", "cases": []}
    config = Config()

    for _, meta, raw, member in eligible:
        if len(selected_sources) >= SOURCE_COUNT:
            break
        sid = str(meta["id"])
        try:
            data = rz.read(member, max_uncompressed=MAX_MEMBER_BYTES)
            sr, loop = BUILDER.decode_wav(data)
        except Exception:
            continue
        duration = len(loop) / sr
        if not (4.0 <= duration <= 12.0 and 8000 <= sr <= 96000):
            continue

        song, hidden = BUILDER.build_pseudo_song(loop, sr, sid)
        source_seconds = len(song) / sr
        source_dir = pack / f"source-{len(selected_sources)+1:02d}"
        source_dir.mkdir()
        source_path = source_dir / "reference.wav"
        sf.write(source_path, song, sr, subtype="PCM_24")

        source_cases = []
        ok = True
        for idx, factor in enumerate(TARGET_FACTORS, start=1):
            target = round(source_seconds * factor + 0.137, 3)
            fit_dir = source_dir / f"case-{idx}"
            try:
                result = fit(source_path, target, fit_dir, config, Budget(90), phase=4)
            except FitError:
                ok = False
                break
            if len(result["candidates"]) != 3:
                ok = False
                break

            case_id = f"s{len(selected_sources)+1:02d}-t{idx}"
            order = candidate_letter_order(case_id, 3)
            blind_rows = []
            mapping = []
            for shown_index, rank_index in enumerate(order):
                letter = "ABC"[shown_index]
                row = result["candidates"][rank_index]
                full_rel = str((fit_dir / row["render"]["path"]).relative_to(pack)).replace("\\","/")
                previews = [
                    {"name": p["name"], "path": p["path"].replace("\\","/")}
                    for p in row["previews"]
                ]
                # fit() preview paths are relative to the case directory's parent layout.
                for p in previews:
                    p["path"] = str(
                        (fit_dir / p["path"]).relative_to(pack)
                    ).replace("\\","/")
                blind_rows.append({
                    "label": letter,
                    "audio": full_rel,
                    "previews": previews,
                })
                mapping.append({
                    "label": letter,
                    "automatic_rank": rank_index + 1,
                    "plan_id": row["id"],
                    "ending": row["plan"]["ending"],
                    "cost": row["plan"]["cost"],
                    "transition_scores": row["plan"]["transition_scores"],
                })

            source_cases.append({
                "case_id": case_id,
                "display_name": f"ケース {len(cases)+len(source_cases)+1:02d}",
                "target_seconds": target,
                "source_reference": str(source_path.relative_to(pack)).replace("\\","/"),
                "blind_candidates": blind_rows,
                "unblind": mapping,
            })

        if not ok:
            shutil.rmtree(source_dir)
            continue

        selected_sources.append({
            "id": sid,
            "creator": meta.get("creator"),
            "license": "CC0-1.0",
            "member": member,
            "source_seconds": source_seconds,
            "publisher_loop_seconds": hidden["period_seconds"],
        })
        for case in source_cases:
            cases.append({k:v for k,v in case.items() if k != "unblind"})
            unblind["cases"].append({
                "case_id": case["case_id"],
                "source_id": sid,
                "mapping": case["unblind"],
            })

        if rz.fetched_bytes > MAX_NETWORK_BYTES:
            raise RuntimeError("network_budget_exceeded")

    if len(selected_sources) != SOURCE_COUNT or len(cases) != SOURCE_COUNT * 2:
        raise RuntimeError(
            f"insufficient_review_pack:sources={len(selected_sources)}:cases={len(cases)}"
        )

    (pack / "review.html").write_text(build_review(cases), encoding="utf-8")
    manifest = {
        "schema": "musicfit-human-listening-pack/v1",
        "source_count": len(selected_sources),
        "case_count": len(cases),
        "candidate_count": len(cases) * 3,
        "license_policy": "CC0-1.0 only",
        "source_overlap_with_holdout_or_tuning": 0,
        "sources": selected_sources,
        "network_bytes_fetched": rz.fetched_bytes,
        "quality_status": "awaiting_human_ratings",
        "instructions": "Open review.html first. Do not inspect unblind.json until ratings are saved.",
    }
    (pack / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (pack / "unblind.json").write_text(
        json.dumps(unblind, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (pack / "README.txt").write_text(
        "1) review.html を開く\n"
        "2) A/B/Cを耳で評価する（自動順位は隠されています）\n"
        "3) 「評価JSONを保存」で listening-ratings.json を作る\n"
        "4) その後に必要なら unblind.json を見る\n",
        encoding="utf-8",
    )

    print(json.dumps(manifest, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
