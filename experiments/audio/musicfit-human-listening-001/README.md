# Music Fit human listening benchmark 002 — source-normalized review

## Goal

Measure two different things without mixing them:

1. **Absolute usability** — would the listener actually use this fitted result?
2. **Relative edit quality** — did Music Fit introduce a new problem or make the source audibly worse?

This distinction matters because the source itself may already contain clicks, abrupt transitions, unusual endings or other material the listener dislikes. Those source-native issues should affect product usability, but they should **not automatically count as Music Fit failures**.

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

That yields 18 listening tasks and 54 candidates.

## Blind review

Candidate rank is hidden. A/B/C order is deterministic but unrelated to algorithm rank.

Every task exposes:

- the complete source track;
- a **5-second source-ending reference**;
- three fitted candidates.

Every candidate exposes:

- the complete fitted track;
- up to two short previews around low-context-score joins;
- a 5-second ending preview.

### Absolute usability

- **◎ そのまま使える**
- **○ 十分自然**
- **△ 使えるが気になる**
- **× 使わない**

This answers the product question, but it includes the quality of the original material.

### Relative edit quality

- **元曲と同等 / 改善** — no new audible issue attributable to Music Fit
- **加工由来の違和感は少しあるが許容**
- **加工で明確に悪化した**
- **元曲由来か加工由来か判別しにくい**

The first two count as **no-major-regression**. The last option is excluded from the algorithm-quality denominator rather than treated as a failure.

Issue flags now mean **problems newly introduced or worsened by Music Fit**:

- Loop / Jump point
- Seam / click / crossfade
- Flow / repetition / musical development
- Ending
- Other

This allows, for example:

> absolute usability = ×  
> relative edit quality = source-equivalent

when a source already contains a click or awkward ending that Music Fit merely preserves.

## Score

```bash
python score_ratings.py /path/to/pack/manifest.json /path/to/listening-ratings.json --output report.json
```

The report keeps both families of KPI.

### Product KPI

- absolute Top1 acceptable
- absolute Top3-any-acceptable
- no-acceptable rate

### Algorithm KPI

- relative Top1 no-major-regression
- relative Top3-any-no-major-regression
- Top1/Top3 with **no new issue at all**
- source-confounded candidate rate
- newly introduced issue reasons

For the plugin concept, roughly **relative Top1 >=80–85% and relative Top3 >=90%** is a useful practical target, while absolute usability is reported separately.

## Compatibility

Older v1 rating JSON can still be loaded. Its absolute ratings remain usable. Relative fields are simply missing and therefore excluded from the source-normalized denominator until the reviewer fills them in.

## Important limitation

This first-round pack still uses six loop-derived instrumental sources from one publisher. It is primarily for finding Phase-4, seam and ending problems and validating the review method.

It does **not** establish general acceptance on arbitrary full songs or vocals. Later rounds should add independent commercial-safe full-length music.

## PASS boundary

A green build proves:

- all six pinned source packs still match expected hashes;
- 18 fit requests and 54 candidates run;
- every generated candidate is exact-duration;
- blind A/B/C mapping stays hidden;
- full source and source-ending references are present;
- complete candidates and previews are packaged as lossless FLAC;
- the v2 scorer reports absolute and relative metrics;
- old v1 rating JSON still preserves absolute metrics;
- attribution is included.

PASS does not assert perceptual naturalness. That exists only after human ratings are returned.
