#!/usr/bin/env python3
"""Resolve Colorosse loop-pack downloads and record exact runtime identities."""
from __future__ import annotations

import hashlib
import io
import json
import re
import urllib.parse
import urllib.request
import wave
import zipfile
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
UA = "ziro-lab-audio-loop-benchmark/0.1 (+https://github.com/ziro-lab/chat-native-work-lab-001)"

SOURCES = [
    {
        "id": "arcade",
        "title": "Arcade — Arcade Loop",
        "page": "https://www.colorosse.com/assets/audio/music/arcade-music-loop",
        "license": "CC0-1.0",
        "author": "Oğuzhan Girgin",
        "tempo_bpm": 151.999,
    },
    {
        "id": "vellum",
        "title": "Vellum — Market Loop",
        "page": "https://www.colorosse.com/assets/audio/music/vellum-music-loop",
        "license": "CC0-1.0",
        "author": "Oğuzhan Girgin",
        "tempo_bpm": 127.999,
    },
    {
        "id": "anvil",
        "title": "Anvil — Crypt Loop",
        "page": "https://www.colorosse.com/assets/audio/music/anvil-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "tempo_bpm": 62.002,
    },
    {
        "id": "prism",
        "title": "Prism — Void Loop",
        "page": "https://www.colorosse.com/assets/audio/music/prism-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "tempo_bpm": 52.001,
    },
    {
        "id": "rust",
        "title": "Rust — Neon Loop",
        "page": "https://www.colorosse.com/assets/audio/music/rust-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "tempo_bpm": 108.0,
    },
    {
        "id": "timber",
        "title": "Timber — Hearth Loop",
        "page": "https://www.colorosse.com/assets/audio/music/timber-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "tempo_bpm": 84.0,
    },
]


class LinkParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.links: list[dict] = []
        self._current: dict | None = None

    def handle_starttag(self, tag: str, attrs) -> None:
        if tag.lower() != "a":
            return
        data = dict(attrs)
        href = data.get("href")
        if href:
            self._current = {"href": href, "text": "", "download": data.get("download")}

    def handle_data(self, data: str) -> None:
        if self._current is not None:
            self._current["text"] += data

    def handle_endtag(self, tag: str) -> None:
        if tag.lower() == "a" and self._current is not None:
            self.links.append(self._current)
            self._current = None


def request_bytes(url: str) -> tuple[bytes, str, dict]:
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"})
    with urllib.request.urlopen(req, timeout=60) as response:
        data = response.read()
        return data, response.geturl(), dict(response.headers.items())


def candidate_downloads(page_url: str, html: str) -> list[str]:
    parser = LinkParser()
    parser.feed(html)
    ranked = []
    for link in parser.links:
        href = str(link["href"])
        text = re.sub(r"\s+", " ", str(link.get("text", ""))).strip().lower()
        absolute = urllib.parse.urljoin(page_url, href)
        lower = absolute.lower()
        score = 0
        if ".zip" in lower:
            score += 100
        if "download" in text:
            score += 50
        if "download" in lower:
            score += 25
        if link.get("download") is not None:
            score += 20
        if score:
            ranked.append((score, absolute))
    # Also catch literal ZIP URLs embedded in JSON/script data.
    for raw in re.findall(r'https?[^"\'<>\\\s]+?\.zip(?:\?[^"\'<>\\\s]*)?', html, flags=re.I):
        ranked.append((110, raw.replace("&amp;", "&")))
    unique = []
    seen = set()
    for _, url in sorted(ranked, key=lambda x: (-x[0], x[1])):
        if url not in seen:
            unique.append(url)
            seen.add(url)
    return unique


def resolve_zip(page_url: str) -> tuple[bytes, str, list[str]]:
    page_bytes, final_page, _ = request_bytes(page_url)
    html = page_bytes.decode("utf-8", errors="replace")
    candidates = candidate_downloads(final_page, html)
    errors = []
    for url in candidates[:12]:
        try:
            data, final_url, _ = request_bytes(url)
            if data[:4] == b"PK\x03\x04" and zipfile.is_zipfile(io.BytesIO(data)):
                return data, final_url, candidates
            errors.append(f"not zip: {url} ({len(data)} bytes, magic={data[:8]!r})")
        except Exception as exc:
            errors.append(f"{url}: {type(exc).__name__}: {exc}")
    raise RuntimeError(
        f"could not resolve ZIP from {page_url}; candidates={candidates[:12]}; errors={errors}"
    )


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def inspect_wav(data: bytes) -> dict:
    with wave.open(io.BytesIO(data), "rb") as wf:
        frames = wf.getnframes()
        rate = wf.getframerate()
        return {
            "channels": wf.getnchannels(),
            "sample_width_bytes": wf.getsampwidth(),
            "sample_rate": rate,
            "frames": frames,
            "duration_seconds": frames / rate if rate else None,
        }


def inventory_one(source: dict) -> dict:
    archive, final_url, candidates = resolve_zip(source["page"])
    archive_path = OUT / f"{source['id']}.zip"
    archive_path.write_bytes(archive)
    members = []
    with zipfile.ZipFile(io.BytesIO(archive), "r") as zf:
        for info in zf.infolist():
            if info.is_dir():
                continue
            row = {"name": info.filename, "size": info.file_size}
            suffix = Path(info.filename).suffix.lower()
            if suffix == ".wav":
                try:
                    row["wav"] = inspect_wav(zf.read(info))
                except Exception as exc:
                    row["wav_error"] = f"{type(exc).__name__}: {exc}"
            members.append(row)
    audio_members = [m for m in members if Path(m["name"]).suffix.lower() in {".wav", ".ogg", ".flac", ".mp3"}]
    wav_members = [m for m in members if Path(m["name"]).suffix.lower() == ".wav"]
    if not audio_members or not wav_members:
        raise RuntimeError(f"{source['id']}: archive lacks expected audio/WAV files")
    return {
        **source,
        "resolved_download_url": final_url,
        "zip_size": len(archive),
        "zip_sha256": sha256(archive),
        "resolver_candidate_count": len(candidates),
        "archive_members": members,
        "audio_member_count": len(audio_members),
        "wav_member_count": len(wav_members),
    }


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    for source in SOURCES:
        row = inventory_one(source)
        rows.append(row)
        print(
            f"{source['id']}: zip={row['zip_size']} sha256={row['zip_sha256']} "
            f"wav={row['wav_member_count']} url={row['resolved_download_url']}"
        )
    inventory = {
        "schema": "audio-real-loop-source-inventory/v1",
        "source_count": len(rows),
        "sources": rows,
        "license_policy": {
            "allowed": ["CC0-1.0", "CC-BY-4.0"],
            "redistribution": "runtime-only in this experiment; ZIP/audio bytes are not uploaded as artifacts",
        },
    }
    (OUT / "inventory.json").write_text(json.dumps(inventory, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
