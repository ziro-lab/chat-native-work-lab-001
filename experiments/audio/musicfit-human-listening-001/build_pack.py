from __future__ import annotations

import hashlib
import html
import importlib.util
import json
import shutil
import sys
import tempfile
import time
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
CORE_ROOT = AUDIO_ROOT / "music-fit-core-001"
BUILDER_PATH = AUDIO_ROOT / "audio-real-loop-self-discovery-001" / "run_self_discovery.py"
sys.path.insert(0, str(CORE_ROOT))

from musicfit.core import Budget, Config, VERSION, analyze, audition_edges, plans
from musicfit.render import render

TARGETS = [
    ("shorten", 0.68),
    ("extend-medium", 1.60),
    ("extend-long", 2.40),
]
BLIND_LABELS = ["A", "B", "C"]


def load_builder():
    spec = importlib.util.spec_from_file_location("pinned_real_fixture", BUILDER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


BUILDER = load_builder()


def convert_flac(source: Path, destination: Path):
    destination.parent.mkdir(parents=True, exist_ok=True)
    with sf.SoundFile(str(source)) as f:
        data = f.read(dtype="float32", always_2d=True)
        sr = f.samplerate
    sf.write(str(destination), data, sr, format="FLAC", subtype="PCM_24")


def clip_flac(rendered: Path, destination: Path, center: int | None, seconds=4.0, ending=False):
    destination.parent.mkdir(parents=True, exist_ok=True)
    with sf.SoundFile(str(rendered)) as f:
        sr = f.samplerate
        n = len(f)
        if ending:
            start, end = max(0, n - round(seconds * sr)), n
        else:
            assert center is not None
            half = round(seconds * sr / 2)
            start, end = max(0, center - half), min(n, center + half)
        f.seek(start)
        data = f.read(end - start, dtype="float32", always_2d=True)
    sf.write(str(destination), data, sr, format="FLAC", subtype="PCM_24")
    return {"start_frame": start, "end_frame": end, "sample_rate": sr}


def blind_order(task_id: str, candidate_count: int):
    indices = list(range(candidate_count))
    indices.sort(
        key=lambda i: hashlib.sha256(
            f"{task_id}:candidate-{i+1}:blind-v1".encode()
        ).hexdigest()
    )
    return indices


def attribution_text():
    rows = [
        "# Music Fit human listening pack — third-party attribution",
        "",
        "Runtime-generated derivative audio uses pinned Colorosse music-loop packs.",
        "Original ZIP archives are not included.",
        "",
        "Creator / publisher: Oğuzhan Girgin / Colorosse",
        "",
        "| Source | License | Canonical source | Pinned ZIP SHA256 |",
        "|---|---|---|---|",
    ]
    for source in BUILDER.SOURCES:
        rows.append(
            f"| {source['id']} | {source['license']} | {source['url']} | {source['sha256']} |"
        )
    rows += [
        "",
        "CC0 1.0: https://creativecommons.org/publicdomain/zero/1.0/",
        "",
        "CC BY 4.0: https://creativecommons.org/licenses/by/4.0/",
        "",
        "Third-party audio remains under its source license.",
    ]
    return "\n".join(rows) + "\n"


def candidate_html(task_id, candidate):
    bid = html.escape(candidate["blind_id"])
    full = html.escape(candidate["audio"], quote=True)
    previews = "".join(
        '<div class="preview"><span>' + html.escape(p["label"]) + '</span>'
        '<audio controls preload="none" src="' + html.escape(p["audio"], quote=True) + '"></audio></div>'
        for p in candidate["previews"]
    )
    issue_boxes = "".join(
        '<label><input type="checkbox" data-issue="' + name + '"> ' + label + '</label>'
        for name, label in [
            ("loop", "Loop / Jump位置"),
            ("seam", "Seam / click / crossfade"),
            ("flow", "展開 / 繰り返し"),
            ("ending", "Ending"),
            ("other", "その他"),
        ]
    )
    return f"""
    <article class="candidate" data-blind="{bid}">
      <h4>候補 {bid}</h4>
      <audio class="full" controls preload="none" src="{full}"></audio>
      <details><summary>継ぎ目・Endingの短い試聴</summary>{previews}</details>
      <div class="rating">
        <label>絶対評価（この候補を実際に使うか）
          <select class="overall">
            <option value="">未評価</option>
            <option value="good">◎ そのまま使える</option>
            <option value="acceptable">○ 十分自然</option>
            <option value="minor">△ 使えるが気になる</option>
            <option value="reject">× 使わない</option>
          </select>
        </label>
        <label>元曲との差（Music Fitで悪くなったか）
          <select class="edit-impact">
            <option value="">未評価</option>
            <option value="same_or_better">元曲と同等 / 改善（新しい違和感なし）</option>
            <option value="minor_added">加工由来の違和感は少しあるが許容</option>
            <option value="major_added">加工で明確に悪化した</option>
            <option value="unclear_source">元曲由来か加工由来か判別しにくい</option>
          </select>
        </label>
        <div class="issues"><span>Music Fitで新たに増えた / 悪化した点:</span>{issue_boxes}</div>
        <label class="best"><input type="radio" name="best-{html.escape(task_id)}" value="{bid}"> このケースで一番よい</label>
      </div>
    </article>
    """


def review_html(manifest):
    sections = []
    for task in manifest["tasks"]:
        source_audio = html.escape(task["source_audio"], quote=True)
        cards = "".join(candidate_html(task["id"], c) for c in task["candidates"])
        sections.append(f"""
        <section class="task" data-task="{html.escape(task['id'])}">
          <h2>{html.escape(task['source_id'])} — {html.escape(task['target_kind'])}</h2>
          <p>元 {task['source_seconds']:.2f}s → 目標 {task['target_seconds']:.2f}s。候補順位は伏せています。</p>
          <div class="source-ref">
            <b>元曲参照</b>
            <details><summary>元音源 全体</summary><audio controls preload="none" src="{source_audio}"></audio></details>
            <div>元曲 Ending 5秒 <audio controls preload="none" src="{html.escape(task['source_ending_audio'], quote=True)}"></audio></div>
          </div>
          <div class="candidates">{cards}</div>
          <label class="none"><input type="radio" name="best-{html.escape(task['id'])}" value=""> 3候補とも選ばない</label>
        </section>
        """)

    spec = {
        "schema": manifest["schema"],
        "core_version": manifest["core_version"],
        "task_count": len(manifest["tasks"]),
    }
    spec_json = json.dumps(spec, ensure_ascii=False).replace("</", "<\\/")

    return """<!doctype html>
<meta charset="utf-8">
<title>Music Fit 人間試聴ベンチ 001</title>
<style>
body{font-family:system-ui,sans-serif;max-width:1120px;margin:2rem auto;padding:0 1rem;line-height:1.5}
header{position:sticky;top:0;background:white;padding:.7rem 0;border-bottom:1px solid #ccc;z-index:5}
.task{margin:2rem 0;padding:1rem;border:1px solid #bbb;border-radius:10px}
.candidates{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:1rem}
.candidate{border:1px solid #ddd;border-radius:8px;padding:1rem}
audio{width:100%}.preview{margin:.5rem 0}.issues label{display:block}
.rating{margin-top:1rem}select{font-size:1rem;padding:.3rem}.best,.none{display:block;margin-top:1rem}
button,input[type=file],input[type=text]{font-size:1rem;margin:.25rem;padding:.45rem}
.done{border-color:#4b8}
</style>
<header>
  <b>Music Fit 人間試聴ベンチ</b> <span id="progress"></span><br>
  <input id="rater" type="text" placeholder="評価者名（任意）">
  <button id="save">評価JSONを保存</button>
  <label>途中結果を読込 <input id="load" type="file" accept="application/json"></label>
</header>
<p><b>評価は2種類です。</b>「絶対評価」はその候補を実際に使うか。「元曲との差」はMusic Fitの加工で新しい違和感が増えたかを見ます。元曲自体にブチ音・微妙なEndingなどがある場合、絶対評価が×でも、加工で悪化していなければ「元曲と同等 / 改善」で構いません。候補A/B/Cはアルゴリズム順位とは無関係です。</p>
""" + "".join(sections) + """
<script>
const spec=""" + spec_json + """;
function collect(){
  return {
    schema:'musicfit-human-listening-ratings/v1',
    benchmark_schema:spec.schema,
    core_version:spec.core_version,
    rater:document.getElementById('rater').value.trim(),
    tasks:Array.from(document.querySelectorAll('.task')).map(function(task){
      const checked=task.querySelector('input[type=radio]:checked');
      return {
        id:task.dataset.task,
        best:checked ? checked.value : null,
        candidates:Array.from(task.querySelectorAll('.candidate')).map(function(c){
          return {
            blind_id:c.dataset.blind,
            overall:c.querySelector('.overall').value,
            edit_impact:c.querySelector('.edit-impact').value,
            issues:Array.from(c.querySelectorAll('[data-issue]:checked')).map(function(x){return x.dataset.issue;})
          };
        })
      };
    })
  };
}
function update(){
  let done=0;
  document.querySelectorAll('.task').forEach(function(task){
    const absoluteDone=Array.from(task.querySelectorAll('.overall')).every(function(x){return x.value;});
    const relativeDone=Array.from(task.querySelectorAll('.edit-impact')).every(function(x){return x.value;});
    const complete=absoluteDone&&relativeDone;
    task.classList.toggle('done',complete);
    if(complete)done++;
  });
  document.getElementById('progress').textContent=' — '+done+'/'+spec.task_count+'ケース評価済み';
}
document.addEventListener('change',update);
document.getElementById('save').onclick=function(){
  const blob=new Blob([JSON.stringify(collect(),null,2)],{type:'application/json'});
  const url=URL.createObjectURL(blob);
  const a=document.createElement('a');
  a.href=url;a.download='listening-ratings.json';a.click();
  setTimeout(function(){URL.revokeObjectURL(url);},1000);
};
document.getElementById('load').onchange=async function(e){
  const file=e.target.files[0];if(!file)return;
  const data=JSON.parse(await file.text());
  if(data.rater)document.getElementById('rater').value=data.rater;
  (data.tasks||[]).forEach(function(row){
    const tasks=Array.from(document.querySelectorAll('.task'));
    const task=tasks.find(function(x){return x.dataset.task===row.id;});
    if(!task)return;
    (row.candidates||[]).forEach(function(rc){
      const candidates=Array.from(task.querySelectorAll('.candidate'));
      const c=candidates.find(function(x){return x.dataset.blind===rc.blind_id;});
      if(!c)return;
      c.querySelector('.overall').value=rc.overall||'';
      c.querySelector('.edit-impact').value=rc.edit_impact||'';
      c.querySelectorAll('[data-issue]').forEach(function(box){
        box.checked=(rc.issues||[]).indexOf(box.dataset.issue)>=0;
      });
    });
    if(row.best!==null&&row.best!==undefined){
      const radios=Array.from(task.querySelectorAll('input[type=radio]'));
      const hit=radios.find(function(x){return x.value===row.best;});
      if(hit)hit.checked=true;
    }
  });
  update();
};
update();
</script>
"""


def main():
    out = ROOT / "out"
    if out.exists():
        shutil.rmtree(out)
    pack_dir = out / "listening-pack"
    pack_dir.mkdir(parents=True)

    config = Config()
    tasks = []
    source_manifest = []
    started = time.monotonic()
    total_candidates = 0
    exact_outputs = True

    with tempfile.TemporaryDirectory(prefix="musicfit-listening-") as td:
        temp = Path(td)
        for source in BUILDER.SOURCES:
            source_pack = BUILDER.load_pack(source)
            song, _hidden = BUILDER.build_pseudo_song(source, source_pack)
            source_wav = temp / f"{source['id']}-source.wav"
            sf.write(str(source_wav), song, source_pack["sr"], subtype="PCM_24")
            source_flac = pack_dir / "sources" / f"{source['id']}.flac"
            convert_flac(source_wav, source_flac)
            source_ending_rel = Path("sources") / f"{source['id']}-ending.flac"
            clip_flac(
                source_wav,
                pack_dir / source_ending_rel,
                None,
                seconds=5.0,
                ending=True,
            )

            analysis = analyze(source_wav, config, Budget(180))
            hypotheses = [
                {
                    "period_seconds": (e.end - e.start) / analysis.sample_rate,
                    "score": e.score,
                    "kind": e.kind,
                }
                for e in audition_edges(analysis)
            ]
            source_seconds = analysis.frames / analysis.sample_rate
            source_manifest.append({
                "id": source["id"],
                "license": source["license"],
                "canonical_source": source["url"],
                "zip_sha256": source_pack["zip_sha256"],
                "source_seconds": source_seconds,
                "analysis_edges": len(analysis.edges),
                "audition_loop_hypotheses": hypotheses,
            })

            for target_kind, factor in TARGETS:
                target_seconds = round(source_seconds * factor + 0.137, 3)
                task_id = f"{source['id']}--{target_kind}"
                candidate_plans = plans(
                    analysis, target_seconds, config, Budget(180), phase=4
                )
                if len(candidate_plans) != 3:
                    raise RuntimeError(f"expected_three_candidates:{task_id}:{len(candidate_plans)}")

                order = blind_order(task_id, len(candidate_plans))
                blind_for_index = {
                    candidate_index: BLIND_LABELS[pos]
                    for pos, candidate_index in enumerate(order)
                }
                task_candidates = []

                for index, plan in enumerate(candidate_plans):
                    blind = blind_for_index[index]
                    work_wav = temp / f"{task_id}-{blind}.wav"
                    meta = render(
                        source_wav, analysis, plan, work_wav, config, Budget(180)
                    )
                    exact_outputs &= meta["frames"] == plan.target_frames
                    full_rel = Path("candidates") / task_id / f"{blind}.flac"
                    convert_flac(work_wav, pack_dir / full_rel)

                    seam_rows = sorted(
                        meta["seams"],
                        key=lambda s: (
                            s.get("source_context_score", 1.0),
                            s["output_frame"],
                        ),
                    )[:2]
                    preview_rows = []
                    for seam_index, seam in enumerate(seam_rows, 1):
                        rel = (
                            Path("previews")
                            / task_id
                            / f"{blind}-join-{seam_index}.flac"
                        )
                        clip = clip_flac(
                            work_wav,
                            pack_dir / rel,
                            int(seam["output_frame"]),
                            seconds=4.0,
                        )
                        preview_rows.append({
                            "label": f"継ぎ目 {seam_index}",
                            "audio": rel.as_posix(),
                            "kind": "join",
                            "source_context_score": seam.get("source_context_score"),
                            **clip,
                        })

                    ending_rel = (
                        Path("previews") / task_id / f"{blind}-ending.flac"
                    )
                    ending_clip = clip_flac(
                        work_wav,
                        pack_dir / ending_rel,
                        None,
                        seconds=5.0,
                        ending=True,
                    )
                    preview_rows.append({
                        "label": "Ending",
                        "audio": ending_rel.as_posix(),
                        "kind": "ending",
                        **ending_clip,
                    })

                    task_candidates.append({
                        "blind_id": blind,
                        "algorithm_rank": index + 1,
                        "plan_id": plan.id,
                        "audio": full_rel.as_posix(),
                        "ending": plan.ending,
                        "strategy": plan.strategy,
                        "cost": plan.cost,
                        "transition_scores": plan.transition_scores,
                        "span_count": len(plan.spans),
                        "previews": preview_rows,
                    })
                    total_candidates += 1
                    work_wav.unlink(missing_ok=True)

                task_candidates.sort(key=lambda c: c["blind_id"])
                tasks.append({
                    "id": task_id,
                    "source_id": source["id"],
                    "source_audio": f"sources/{source['id']}.flac",
                    "source_ending_audio": source_ending_rel.as_posix(),
                    "source_seconds": source_seconds,
                    "target_kind": target_kind,
                    "target_factor": factor,
                    "target_seconds": target_seconds,
                    "candidates": task_candidates,
                })
            source_wav.unlink(missing_ok=True)

    manifest = {
        "schema": "musicfit-human-listening-benchmark/v2",
        "core_version": VERSION,
        "blind_order": "deterministic SHA256; algorithm rank hidden in UI",
        "acceptable_definition": ["good", "acceptable"],
        "relative_edit_quality_definition": {
            "pass": ["same_or_better", "minor_added"],
            "clean": ["same_or_better"],
            "excluded_as_source_confounded": ["unclear_source"],
        },
        "source_count": len(source_manifest),
        "task_count": len(tasks),
        "candidate_count": total_candidates,
        "sources": source_manifest,
        "tasks": tasks,
        "limitations": [
            "six loop-derived instrumental sources from one publisher",
            "human naturalness unknown until ratings are supplied",
            "not a vocal or lyric-continuity benchmark",
        ],
    }
    (pack_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    (pack_dir / "ATTRIBUTION.md").write_text(
        attribution_text(), encoding="utf-8"
    )
    (pack_dir / "review.html").write_text(
        review_html(manifest), encoding="utf-8"
    )

    evidence = {
        "status": "PASS_MUSICFIT_HUMAN_LISTENING_PACK_V2",
        "core_version": VERSION,
        "source_count": len(source_manifest),
        "task_count": len(tasks),
        "candidate_count": total_candidates,
        "exact_duration_all_candidates": bool(exact_outputs),
        "target_kinds": [name for name, _ in TARGETS],
        "audio_format": "FLAC PCM_24",
        "ratings_present": False,
        "human_top1_acceptable": None,
        "human_top3_any_acceptable": None,
        "elapsed_seconds": time.monotonic() - started,
    }
    if len(source_manifest) != 6 or len(tasks) != 18 or total_candidates != 54:
        raise RuntimeError(
            "unexpected_pack_shape:"
            f"sources={len(source_manifest)} tasks={len(tasks)} "
            f"candidates={total_candidates}"
        )
    if not exact_outputs:
        raise RuntimeError("non_exact_output")
    (out / "evidence.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(evidence, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
