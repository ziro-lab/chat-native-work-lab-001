# FSLD minimum-similarity tuning 001

## Goal

Test the cheapest possible Music Fit accuracy improvement before changing features or adding models:

> Is the current `min_similarity=0.78` unnecessarily strict?

The experiment tunes only this existing scalar on commercially safe FSLD audio.

## Leakage controls

- the fixed 48-source holdout is never used for selection;
- every creator present in that holdout is excluded;
- train and validation use distinct creators;
- at most one source per creator is selected;
- the eventual holdout remains untouched until a setting wins validation.

## Data

Commercial-safe gate only:
- CC0-1.0
- CC-BY-3.0

Deterministic sample:
- train: 48 creators (24 + 24)
- validation: 24 different creators (12 + 12)

Each known loop is transformed into the same non-byte-identical pseudo-song form used by the fixed holdout benchmark.

## Candidate settings

`min_similarity` = 0.66, 0.70, 0.74, 0.78, 0.82.

All other Music Fit code and constants remain identical.

## Metrics

Two metrics are deliberately kept separate:

- strict Top1/Top3: publisher whole-loop period ±70 ms;
- soft audition Top1/Top3: strict match OR a >=4-integer-beat recurrence under FSLD BPM metadata.

The soft metric is **not** human naturalness and **not** bar ground truth. It only matches the planned "show up to three plausible previews" UX better than publisher-period-only scoring.

## Selection

Choose on training only:
1. highest soft Top3;
2. then highest strict Top3;
3. then lowest no-edge rate;
4. then the higher/conservative threshold.

Validation is reported after selection. No Core default is changed by this experiment.

## PASS boundary

A green run proves the split, bounded downloads, all threshold trials, train-only selection and validation reporting executed. It does not require a new threshold to beat 0.78.
