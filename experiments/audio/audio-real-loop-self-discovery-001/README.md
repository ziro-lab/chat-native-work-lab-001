# Real-audio single-track loop self-discovery 001

## Goal

Move from **phase registration between two known corresponding clips** to the problem that an eventual YMM4 tool actually needs:

> Given one ordinary-looking audio file, discover a repeated musical period and a usable Loop Start / Loop End pair without being given the original loop, BPM, beat grid or loop length.

The previous dense-phase experiment reached 12/12 on pinned real loop variants, but it still compared a source take against a target take. This experiment removes that reference take from the analyzer.

## Pseudo-song construction

For each of the six pinned Colorosse real loop packs, build one runtime-only pseudo-song:

```text
1-bar sparse/fade-in intro
-> full loop mix A
-> full loop mix B
-> full loop mix C
-> 1-bar sparse/fade-out outro
```

The three middle cycles use different deterministic stem balances and mild rendering differences. The result therefore contains a genuinely repeated musical structure without being a byte-identical PCM copy.

The hidden benchmark knows:

- original loop period in samples;
- repeated-body start/end;
- all valid adjacent-cycle regions.

The analyzer does not receive those values.

## Analyzer under test

No Beat This model is used in the discovery stage.

1. Convert the single pseudo-song to a ~40 ms uniform time grid.
2. Compute linear (non-circular) STFT chroma features plus temporal-difference / energy information.
3. Scan plausible time lags directly from the feature sequence.
4. For each lag, find the best contiguous recurrence window whose duration is one candidate period.
5. Select the highest recurrence peak as the discovered period and Loop Start / Loop End pair.
6. Reveal hidden Ground Truth only after the pair has been selected.

This separates **period discovery** from the rhythm-grid model. Beat/Downbeat information can later be used as an optional musical snap/prior rather than as a prerequisite.

## Evaluation

Diagnostic metrics include:

- discovered period error vs the source pack's exact loop period;
- period Hit@±70 ms;
- whether the selected Loop Start/End pair lies entirely inside the repeated body;
- pair-valid rate = period hit + body containment;
- Top1/Top2 recurrence margin;
- per-source results, including the pulse-free Prism loop.

A phase inside a truly cyclic body is not required to equal the publisher's canonical file start. The important first-stage property is recovering the correct **period** and choosing both endpoints inside the repeatable body.

## PASS boundary

This first self-discovery run is diagnostic. PASS proves:

1. all six pinned source ZIP digests match;
2. six single-file pseudo-songs are generated at runtime;
3. discovery receives only each pseudo-song waveform;
4. every track produces a ranked lag and Loop Start/End pair;
5. hidden period/body metadata is consulted only after selection;
6. period/pair quality metrics are emitted;
7. third-party audio is neither committed nor uploaded as an artifact.

Quality is deliberately not part of the first-run PASS threshold. If this exposes a failure, the next experiment will target the actual failure mode rather than moving a goalpost.
