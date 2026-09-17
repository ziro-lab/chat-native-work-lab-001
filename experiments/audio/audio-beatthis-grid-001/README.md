# Audio Beat This grid 001

## Goal

Measure whether the public **Beat This!** `small0` model can recover beat and downbeat timing accurately enough on this lab's transformed synthetic loop fixture to replace the oracle rhythm grid in the next loop-ranking experiment.

This experiment is intentionally limited to timing detection. It does not rank loop candidates yet.

## Upstream

- project: https://github.com/CPJKU/beat_this
- package: `beat-this==1.1.0`
- model: `small0` (about 8.1 MB)
- code / published weights license: MIT
- inference: CPU, no DBN postprocessing

The model checkpoint is downloaded at runtime by upstream code and is not committed to this repository. The experiment records its SHA256 after download so a follow-up can pin the exact checkpoint digest.

## Fixture

The experiment reuses the three synthetic takes from `../audio-loop-recovery-001/`.

Ground Truth:

- sample rate: 16 kHz
- tempo: 120 BPM
- meter: 4/4
- beat interval: 0.5 s
- downbeat interval: 2.0 s
- duration: 16 s

Each take has different gain/timbre/phase/noise, so the detector is not being evaluated on one byte-identical file repeated three times.

## Metrics

Beat and downbeat predictions are matched independently to Ground Truth using a **±70 ms** tolerance.

For each class we record:

- Precision
- Recall
- F1
- matched-event mean absolute timing error
- matched-event maximum absolute timing error

A greedy nearest-match policy is used with one prediction matched to at most one Ground-Truth event.

## PASS boundary

PASS requires aggregate:

- Beat F1 >= 0.90
- Downbeat F1 >= 0.80
- at least two of the three takes individually achieve Beat F1 >= 0.90
- downloaded model checkpoint exists and its SHA256 is recorded

PASS proves only that Beat This `small0` provides a sufficiently accurate rhythm grid on this synthetic fixture under the stated tolerance.

## NOT PROVEN

This does not prove:

- arbitrary-song Beat/Downbeat accuracy;
- robustness to tempo drift or unusual meter;
- loop-point naturalness;
- free-form loop candidate generation;
- crossfade/seam rendering quality;
- YMM4 integration.
