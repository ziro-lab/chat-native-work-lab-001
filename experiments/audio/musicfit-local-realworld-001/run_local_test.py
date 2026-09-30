from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import shutil
import sys
import time
import webbrowser
from pathlib import Path

import soundfile as sf

HERE = Path(__file__).resolve().parent

# Standalone package: musicfit/ is beside this script.
# Repository checkout: reuse the sibling core experiment.
for candidate in (
    HERE,
    HERE.parent / "music-fit-core-001",
):
    if (candidate / "musicfit" / "__init__.py").is_file():
        sys.path.insert(0, str(candidate))
        break

from musicfit.core import Budget, Config, FitError, VERSION  # noqa: E402
from musicfit.render import fit  # noqa: E402

SUPPORTED_SUFFIXES = {
    ".wav", ".flac", ".ogg", ".oga", ".mp3", ".aiff", ".aif", ".opus"
}
BLIND_LABELS = ("A", "B", "C")


def load_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path.name}: JSON root must be an object")
    return value


def positive_seconds_list(value, label: str) -> list[float]:
    if not isinstance(value, list) or not value:
        raise ValueError(f"{label}: expected a non-empty list")
    result = []
    for item in value:
        if isinstance(item, bool) or not isinstance(item, (int, float)):
            raise ValueError(f"{label}: all targets must be numbers")
        seconds = float(item)
        if not 0.5 <= seconds <= 3600:
            raise ValueError(f"{label}: target {seconds} is outside 0.5..3600 seconds")
        result.append(seconds)
    return result


def validate_settings(settings: dict) -> dict:
    allowed = {
        "targets_seconds",
        "per_file_targets",
        "phase",
        "budget_seconds_per_fit",
        "max_files",
    }
    unknown = set(settings) - allowed
    if unknown:
        raise ValueError(f"settings.json: unknown fields: {sorted(unknown)}")

    default_targets = positive_seconds_list(
        settings.get("targets_seconds", [60, 120]),
        "targets_seconds",
    )
    overrides = settings.get("per_file_targets", {})
    if not isinstance(overrides, dict):
        raise ValueError("per_file_targets must be an object")
    normalized_overrides = {
        str(name): positive_seconds_list(targets, f"per_file_targets[{name!r}]")
        for name, targets in overrides.items()
    }

    phase = settings.get("phase", 4)
    if type(phase) is not int or phase not in (3, 4, 5):
        raise ValueError("phase must be 3, 4 or 5")

    budget = settings.get("budget_seconds_per_fit", 180)
    if isinstance(budget, bool) or not isinstance(budget, (int, float)):
        raise ValueError("budget_seconds_per_fit must be numeric")
    budget = float(budget)
    if not 5 <= budget <= 600:
        raise ValueError("budget_seconds_per_fit must be between 5 and 600")

    max_files = settings.get("max_files", 20)
    if isinstance(max_files, bool) or not isinstance(max_files, int) or not 1 <= max_files <= 100:
        raise ValueError("max_files must be an integer between 1 and 100")

    return {
        "targets_seconds": default_targets,
        "per_file_targets": normalized_overrides,
        "phase": phase,
        "budget_seconds_per_fit": budget,
        "max_files": max_files,
    }


def safe_task_id(index: int, source: Path, target: float) -> str:
    digest = hashlib.sha256(
        f"{source.name}\0{target:.6f}".encode("utf-8")
    ).hexdigest()[:10]
    return f"{index:03d}-{digest}"


def blind_order(task_key: str, count: int) -> list[int]:
    indices = list(range(count))
    indices.sort(
        key=lambda index: hashlib.sha256(
            f"{task_key}\0rank-{index + 1}\0blind-v1".encode("utf-8")
        ).hexdigest()
    )
    return indices


def copy_as_wav(source: Path, destination: Path) -> dict:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with sf.SoundFile(str(source)) as inp:
        if inp.frames <= 0 or inp.samplerate <= 0:
            raise RuntimeError("empty_or_invalid_audio")
        with sf.SoundFile(
            str(destination),
            mode="w",
            samplerate=inp.samplerate,
            channels=inp.channels,
            format="WAV",
            subtype="PCM_24",
        ) as out:
            while True:
                block = inp.read(65536, dtype="float32", always_2d=True)
                if len(block) == 0:
                    break
                out.write(block)
        return {
            "frames": int(inp.frames),
            "sample_rate": int(inp.samplerate),
            "channels": int(inp.channels),
            "seconds": float(inp.frames / inp.samplerate),
        }


def write_ending(source: Path, destination: Path, seconds: float = 8.0) -> dict:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with sf.SoundFile(str(source)) as inp:
        count = min(inp.frames, round(seconds * inp.samplerate))
        start = max(0, inp.frames - count)
        inp.seek(start)
        data = inp.read(count, dtype="float32", always_2d=True)
        sf.write(
            str(destination),
            data,
            inp.samplerate,
            format="WAV",
            subtype="PCM_24",
        )
        return {
            "start_frame": int(start),
            "frames": int(len(data)),
            "sample_rate": int(inp.samplerate),
        }


def source_targets(source: Path, settings: dict) -> list[float]:
    override = settings["per_file_targets"].get(source.name)
    return override if override is not None else settings["targets_seconds"]


def scan_inputs(input_dir: Path, max_files: int) -> list[Path]:
    files = [
        path for path in input_dir.iterdir()
        if path.is_file() and path.suffix.lower() in SUPPORTED_SUFFIXES
    ]
    files.sort(key=lambda path: path.name.casefold())
    return files[:max_files]


def candidate_card(task_id: str, candidate: dict) -> str:
    label = html.escape(candidate["blind_id"])
    audio = html.escape(candidate["audio"], quote=True)
    ending = html.escape(candidate["ending_audio"], quote=True)
    radio_name = html.escape(f"best-{task_id}", quote=True)
    return f"""
    <article class="candidate" data-blind="{label}">
      <h4>候補 {label}</h4>
      <audio controls preload="metadata" src="{audio}"></audio>
      <details>
        <summary>Endingだけ確認</summary>
        <audio controls preload="metadata" src="{ending}"></audio>
      </details>
      <div class="ratings">
        <label>全体
          <select class="overall">
            <option value="">未評価</option>
            <option value="none">違和感なし</option>
            <option value="minor">少し違和感</option>
            <option value="major">はっきり違和感</option>
          </select>
        </label>
        <label>Ending
          <select class="ending">
            <option value="">未評価</option>
            <option value="none">違和感なし</option>
            <option value="minor">少し違和感</option>
            <option value="major">はっきり違和感</option>
          </select>
        </label>
        <label class="source-same">
          <input type="checkbox" class="source-same-box">
          気になった要素は元曲にもある
        </label>
        <label class="best">
          <input type="radio" name="{radio_name}" value="{label}">
          この中で一番よい
        </label>
      </div>
    </article>
    """


def review_html(manifest: dict) -> str:
    sections = []
    for task in manifest["tasks"]:
        if task["status"] != "ok":
            sections.append(
                f"""<section class="task error">
                <h2>{html.escape(task["source_name"])} → {task["target_seconds"]:.3f}s</h2>
                <p>生成失敗: <code>{html.escape(task["error"])}</code></p>
                </section>"""
            )
            continue

        cards = "".join(
            candidate_card(task["id"], candidate)
            for candidate in task["candidates"]
        )
        diagnostic = ''
        if task.get("core_result"):
            diagnostic = ('<details><summary>解析・構成の手がかり（診断用）</summary>'
                          '<a href="' + html.escape(task["core_result"], quote=True)
                          + '">詳細JSON</a><pre style="white-space:pre-wrap">'
                          + html.escape(json.dumps(task.get("planner"),ensure_ascii=False,indent=2))
                          + '</pre></details>')
        sections.append(
            f"""<section class="task" data-task="{html.escape(task["id"])}">
              <h2>{html.escape(task["source_name"])} → {task["target_seconds"]:.3f}s</h2>
              <p>元曲 {task["source_seconds"]:.2f}s / Music Fit Core {html.escape(manifest["core_version"])}</p>
              <div class="source">
                <b>元曲</b>
                <audio controls preload="metadata" src="{html.escape(task["source_audio"], quote=True)}"></audio>
                <details>
                  <summary>元曲 Ending {task.get("ending_preview_seconds", 8):g}秒</summary>
                  <audio controls preload="metadata" src="{html.escape(task["source_ending_audio"], quote=True)}"></audio>
                </details>
              </div>
              <div class="candidates">{cards}</div>{diagnostic}
            </section>"""
        )

    spec = {
        "schema": manifest["schema"],
        "core_version": manifest["core_version"],
        "task_count": sum(task["status"] == "ok" for task in manifest["tasks"]),
    }
    spec_json = json.dumps(spec, ensure_ascii=False).replace("</", "<\\/")

    return """<!doctype html>
<meta charset="utf-8">
<title>Music Fit ローカル実機テスト</title>
<style>
body{font-family:system-ui,sans-serif;max-width:1180px;margin:1.5rem auto;padding:0 1rem;line-height:1.5}
header{position:sticky;top:0;background:#fff;padding:.6rem 0;border-bottom:1px solid #bbb;z-index:5}
.task{border:1px solid #bbb;border-radius:10px;padding:1rem;margin:1.5rem 0}
.task.done{border-color:#278a45}.task.error{border-color:#b33}
.source{background:#f6f6f6;border-radius:8px;padding:.8rem;margin:.8rem 0}
.candidates{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:1rem}
.candidate{border:1px solid #ddd;border-radius:8px;padding:1rem}
audio{width:100%;margin:.4rem 0}.ratings label{display:block;margin:.7rem 0}
select,button,input{font-size:1rem}select,button{padding:.35rem}
.note{background:#fff8dc;border-radius:8px;padding:.7rem}.source-same{font-weight:600}
</style>
<header>
  <b>Music Fit ローカル実機テスト</b>
  <span id="progress"></span>
  <button id="save">評価JSONを保存</button>
</header>
<p class="note">
細かい分析は不要です。<b>曲全体に違和感があるか</b>と<b>終わり方に違和感があるか</b>だけで十分です。
元曲にも同じ気になる要素がある場合は「元曲にもある」にチェックしてください。
</p>
""" + "".join(sections) + """
<script>
const spec=""" + spec_json + """;
function collect(){
  return {
    schema:'musicfit-local-ratings/v1',
    benchmark_schema:spec.schema,
    core_version:spec.core_version,
    created_at:new Date().toISOString(),
    tasks:Array.from(document.querySelectorAll('.task[data-task]')).map(function(task){
      const checked=task.querySelector('input[type=radio]:checked');
      return {
        id:task.dataset.task,
        best:checked ? checked.value : null,
        candidates:Array.from(task.querySelectorAll('.candidate')).map(function(c){
          return {
            blind_id:c.dataset.blind,
            overall:c.querySelector('.overall').value,
            ending:c.querySelector('.ending').value,
            source_same:c.querySelector('.source-same-box').checked
          };
        })
      };
    })
  };
}
function update(){
  let done=0;
  document.querySelectorAll('.task[data-task]').forEach(function(task){
    const complete=Array.from(task.querySelectorAll('.candidate')).every(function(c){
      return c.querySelector('.overall').value && c.querySelector('.ending').value;
    });
    task.classList.toggle('done',complete);
    if(complete)done++;
  });
  document.getElementById('progress').textContent=' — '+done+'/'+spec.task_count+'ケース評価済み ';
}
document.addEventListener('change',update);
document.getElementById('save').onclick=function(){
  const blob=new Blob([JSON.stringify(collect(),null,2)],{type:'application/json'});
  const url=URL.createObjectURL(blob);
  const a=document.createElement('a');
  a.href=url;a.download='local-ratings.json';a.click();
  setTimeout(function(){URL.revokeObjectURL(url);},1000);
};
update();
</script>
"""


def build_task(
    source: Path,
    target_seconds: float,
    task_index: int,
    run_dir: Path,
    work_dir: Path,
    settings: dict,
) -> dict:
    task_id = safe_task_id(task_index, source, target_seconds)
    task_dir = run_dir / "tasks" / task_id
    task_dir.mkdir(parents=True, exist_ok=False)

    source_audio = task_dir / "source.wav"
    source_ending = task_dir / "source-ending.wav"
    raw_dir = work_dir / f"{task_id}-fit"
    source_info = None

    try:
        # Decode/copy is part of the per-file task boundary. A single unsupported
        # or damaged input must not abort the entire batch.
        source_info = copy_as_wav(source, source_audio)
        ending_preview_seconds = 24.0 if settings["phase"] == 5 else 8.0
        write_ending(source_audio, source_ending, ending_preview_seconds)

        report = fit(
            source,
            target_seconds,
            raw_dir,
            Config(),
            Budget(settings["budget_seconds_per_fit"]),
            settings["phase"],
        )

        # Keep the portable edit map and hints before raw_dir is removed.
        core_result = task_dir / "core-result.json"
        candidates = report["candidates"]
        if not candidates:
            raise FitError("fit_returned_no_candidates")

        permutation = blind_order(
            f"{source.name}\0{target_seconds:.6f}",
            len(candidates),
        )
        blind_for_rank_index = {
            rank_index: BLIND_LABELS[blind_index]
            for blind_index, rank_index in enumerate(permutation)
        }

        output_candidates = []
        for rank_index, candidate in enumerate(candidates):
            blind = blind_for_rank_index[rank_index]
            rendered = raw_dir / candidate["render"]["path"]
            destination = task_dir / f"{blind}.wav"
            shutil.copy2(rendered, destination)
            candidate["render"]["path"] = destination.name
            ending_path = task_dir / f"{blind}-ending.wav"
            write_ending(destination, ending_path, ending_preview_seconds)

            output_candidates.append({
                "blind_id": blind,
                "algorithm_rank": rank_index + 1,
                "plan_id": candidate["id"],
                "audio": f"tasks/{task_id}/{blind}.wav",
                "ending_audio": f"tasks/{task_id}/{blind}-ending.wav",
                "ending_mode": candidate["render"].get("ending"),
                "seconds": candidate["render"].get("seconds"),
            })

        output_candidates.sort(key=lambda row: row["blind_id"])
        if (raw_dir / "previews").is_dir():
            shutil.copytree(raw_dir / "previews", task_dir / "previews")
        core_result.write_text(json.dumps(report,ensure_ascii=False,indent=2,allow_nan=False)
                               + "\n",encoding="utf-8")
        return {
            "id": task_id,
            "status": "ok",
            "source_name": source.name,
            "source_seconds": source_info["seconds"],
            "target_seconds": target_seconds,
            "source_audio": f"tasks/{task_id}/source.wav",
            "source_ending_audio": f"tasks/{task_id}/source-ending.wav",
            "candidates": output_candidates,
            "core_result": f"tasks/{task_id}/core-result.json",
            "planner": report.get("planner"),
            "ending_preview_seconds": ending_preview_seconds,
        }
    except Exception as exc:
        return {
            "id": task_id,
            "status": "error",
            "source_name": source.name,
            "source_seconds": (
                source_info["seconds"] if source_info is not None else None
            ),
            "target_seconds": target_seconds,
            "source_audio": (
                f"tasks/{task_id}/source.wav" if source_audio.is_file() else None
            ),
            "source_ending_audio": (
                f"tasks/{task_id}/source-ending.wav"
                if source_ending.is_file() else None
            ),
            "candidates": [],
            "error_stage": (
                "input_decode" if source_info is None else "musicfit"
            ),
            "error": f"{type(exc).__name__}: {exc}",
        }
    finally:
        if raw_dir.exists():
            shutil.rmtree(raw_dir, ignore_errors=True)


def parse_args():
    parser = argparse.ArgumentParser(
        description="Run Music Fit against private local BGM files."
    )
    parser.add_argument("--input", type=Path, default=HERE / "input")
    parser.add_argument("--output", type=Path, default=HERE / "output")
    parser.add_argument("--settings", type=Path, default=HERE / "settings.json")
    parser.add_argument("--no-open", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    input_dir = args.input.resolve()
    output_root = args.output.resolve()
    settings = validate_settings(load_json(args.settings.resolve()))

    input_dir.mkdir(parents=True, exist_ok=True)
    output_root.mkdir(parents=True, exist_ok=True)
    sources = scan_inputs(input_dir, settings["max_files"])
    if not sources:
        print(f"[ERROR] No supported audio files were found in: {input_dir}")
        print("Put BGM files there and run again.")
        return 2

    stamp = time.strftime("%Y%m%d-%H%M%S")
    run_dir = output_root / f"run-{stamp}"
    suffix = 1
    while run_dir.exists():
        suffix += 1
        run_dir = output_root / f"run-{stamp}-{suffix}"
    run_dir.mkdir(parents=True)
    work_dir = run_dir / ".work"
    work_dir.mkdir()

    tasks = []
    index = 0
    started = time.monotonic()
    try:
        for source in sources:
            targets = source_targets(source, settings)
            for target_seconds in targets:
                index += 1
                print(
                    f"[{index}] {source.name} -> {target_seconds:.3f}s",
                    flush=True,
                )
                task = build_task(
                    source,
                    target_seconds,
                    index,
                    run_dir,
                    work_dir,
                    settings,
                )
                tasks.append(task)
                if task["status"] == "error":
                    print(f"    ERROR: {task['error']}", flush=True)
                else:
                    print(
                        f"    OK: {len(task['candidates'])} candidates",
                        flush=True,
                    )
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)

    manifest = {
        "schema": "musicfit-local-realworld/v1",
        "core_version": VERSION,
        "settings": settings,
        "input_directory": str(input_dir),
        "task_count": len(tasks),
        "success_count": sum(task["status"] == "ok" for task in tasks),
        "error_count": sum(task["status"] == "error" for task in tasks),
        "elapsed_seconds": time.monotonic() - started,
        "privacy": {
            "network_used_by_runner": False,
            "audio_uploaded": False,
        },
        "tasks": tasks,
    }
    (run_dir / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    review = run_dir / "review.html"
    review.write_text(review_html(manifest), encoding="utf-8")

    print()
    print(f"Output: {run_dir}")
    print(f"Review: {review}")
    print(
        f"Success: {manifest['success_count']} / {manifest['task_count']} tasks"
    )

    if manifest["success_count"] and not args.no_open:
        try:
            if os.name == "nt":
                os.startfile(str(review))  # type: ignore[attr-defined]
            else:
                webbrowser.open(review.as_uri())
        except Exception:
            pass

    return 0 if manifest["success_count"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
