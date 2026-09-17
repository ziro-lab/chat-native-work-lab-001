# Real loop source inventory 001

## Goal

Establish a small, reproducible set of **real rendered seamless music loops** for the next loop-analysis benchmark without committing third-party audio into Git.

The initial source is Colorosse's public music-loop catalog. The selected six packs are all described by the publisher as sample-exact seamless loops with WAV masters and OGG versions. Two are CC0-1.0 and four are CC-BY-4.0.

This experiment does not evaluate the loop detector yet. It answers one narrower question:

> Can CI resolve, download, inventory and cryptographically identify the exact real-audio packs that the benchmark will use?

## Selected source pages

| id | page | declared license | published tempo |
| --- | --- | --- | ---: |
| arcade | `https://www.colorosse.com/assets/audio/music/arcade-music-loop` | CC0-1.0 | 151.999 BPM |
| vellum | `https://www.colorosse.com/assets/audio/music/vellum-music-loop` | CC0-1.0 | 127.999 BPM |
| anvil | `https://www.colorosse.com/assets/audio/music/anvil-music-loop` | CC-BY-4.0 | 62.002 BPM |
| prism | `https://www.colorosse.com/assets/audio/music/prism-music-loop` | CC-BY-4.0 | 52.001 BPM |
| rust | `https://www.colorosse.com/assets/audio/music/rust-music-loop` | CC-BY-4.0 | 108 BPM |
| timber | `https://www.colorosse.com/assets/audio/music/timber-music-loop` | CC-BY-4.0 | 84 BPM |

CC-BY entries remain attributed to Oğuzhan Girgin / Colorosse and are not relicensed by this repository.

## Method

For every source page the workflow:

1. downloads the current public HTML;
2. resolves the page's ZIP download link without hard-coding a guessed asset URL;
3. downloads the ZIP;
4. records final resolved URL, byte size and SHA256;
5. inventories every archive member;
6. records WAV/OGG names and basic WAV metadata when readable;
7. checks that audio assets exist;
8. writes a machine-readable `inventory.json` artifact.

Audio ZIPs themselves remain runtime-only and are not uploaded as GitHub Actions artifacts.

## PASS boundary

PASS proves that all six declared source pages currently resolve to downloadable ZIP archives, each ZIP has a stable digest for that run, and each archive contains audio suitable for a follow-up benchmark.

## NOT PROVEN

PASS does not yet prove:

- that the current loop detector recovers their loop phase;
- that Beat This detects every pack well (notably pulse-free/ambient material may be hard);
- that the resolved ZIP URLs will never change;
- that future upstream versions have the same digest;
- arbitrary-song performance.

After the first successful inventory run, the discovered URLs and SHA256 values can be pinned in the next experiment so source drift becomes an explicit failure rather than a silent change.
