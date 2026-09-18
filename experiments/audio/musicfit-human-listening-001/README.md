# Music Fit human listening benchmark 001

## Goal

Measure the thing the product actually needs:

> Given up to three Phase-4 Music Fit candidates for a target duration, is at least one candidate natural enough to use?

Machine loop-recovery scores are useful diagnostics, but they are not human listening acceptance. This experiment creates a **blind local listening pack** for the current `music-fit-core-001`.

## First-round corpus

Six exact-hash-pinned Colorosse music-loop packs already used elsewhere in the lab:

- Arcade — CC0 1.0
- Vellum — CC0 1.0
- Anvil — CC BY 4.0
- Prism — CC BY 4.0
- Rust — CC BY 4.0
- Timber — CC BY 4.0

The runtime fixture builds one pseudo-song from each pack's stems with a sparse intro, three differently mixed body passes and a sparse outro. No third-party audio is committed to Git.

Each source is fitted at three targets:

- **shorten**: ~0.68× source length
- **extend-medium**: ~1.60×
- **extend-long**: ~2.40×

That yields 18 listening tasks and up to 54 candidates.

## Blind review

Candidate rank is hidden. A/B/C order is deterministically shuffled per task.

Each candidate provides:

- the complete fitted track;
- up to two short previews around the lowest-scoring joins;
- an ending preview.

Rating:

- **◎ そのまま使える**
- **○ 十分自然**
- **△ 使えるが気になる**
- **× 使わない**

Optional issue flags:

- Loop / Jump point
- Seam / click / crossfade
- Flow / repetition / musical development
- Ending
- Other

One candidate can also be marked **best** for each task.

The page exports `listening-ratings.json`. It contains only ratings and blind candidate IDs; the algorithm rank mapping remains in `manifest.json`.

## Score

From this directory:

```bash
python score_ratings.py /path/to/pack/manifest.json /path/to/listening-ratings.json --output report.json
```

The main KPIs are:

- **Top1 acceptable** — algorithm rank 1 is ◎ or ○;
- **Top3 oracle acceptable** — any offered candidate is ◎ or ○;
- no-acceptable rate;
- best-candidate rank distribution;
- failure reasons by shorten / medium extension / long extension.

The target is not 100%. For this product concept, roughly **Top1 >=80–85% and Top3 >=90%** would already support a practical “listen to three and choose” workflow.

## Important limitation

The first round uses six loop-derived instrumental sources from one publisher. It is intended to find obvious Phase-4 / seam / ending problems and validate the review workflow. It does **not** establish general acceptance on arbitrary songs or vocals.

A later round should add more commercial-safe full-length music from independent creators.

## PASS boundary

A green build proves:

- all six pinned source packs still match their expected hashes;
- 18 fit requests run;
- every generated candidate is exact-duration;
- blind A/B/C mapping is deterministic and one-to-one;
- complete tracks and previews are packaged as lossless FLAC;
- attribution is included;
- no original ZIP is uploaded.

PASS does not assert perceptual naturalness. That only exists after ratings are returned.
