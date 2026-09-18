# FSLD audition tuning 001

## Goal

Improve the **three-candidate listening workflow** without adding a large model and without tuning on the fixed 48-source holdout.

The current holdout shows two distinct issues:

- several strict whole-file misses are still musically plausible integer-beat sub-periods;
- Top3 often contains three start positions from the **same period**, so listening to three choices adds little coverage;
- a small number of tracks produce no edge at the default similarity threshold.

This experiment tunes only two cheap policies:

1. **period-diverse Top3**: prefer different period lengths before filling remaining candidate slots;
2. **low-confidence fallback**: only when the normal analyzer returns no edge, retry at a slightly lower fixed similarity threshold and mark the result as fallback.

No feature weights, neural network, tempo model, or large runtime dependency are added.

## Data separation

- **Holdout:** the exact 48 IDs frozen in `fsld-holdout-label-audit-001/holdout-baseline.json`.
- **Tuning set:** 64 different FSLD IDs (32 CC0 + 32 CC BY), selected deterministically.
- Holdout IDs **and creators** are excluded from the tuning set.

The tuning set chooses the policy. The holdout is evaluated only after the policy is frozen.

## Product-oriented label

FSLD supplies BPM annotations but this subset has no reliable meter field. Therefore the experiment reports two metrics separately:

- **strict publisher period**: selected period equals the full published loop duration ±70 ms;
- **audition-worthy beat span**: strict publisher period **or** candidate duration is at least four annotated beats and within 4% of an integer number of annotated beats.

The second metric is deliberately a soft audition metric, not perceptual ground truth. It matches the product requirement better than forcing every useful sub-loop to equal the publisher's entire file.

## Search space

- relative period-diversity separation: 0%, 4%, 8%, 12%, 18% (plus 180 ms absolute floor);
- no-edge fallback threshold: disabled, 0.76, 0.74, 0.72, 0.70.

The chosen strategy maximizes tuning-set audition-worthy Top3, then Top1, then strict Top3, then prefers the less aggressive fallback.

## PASS boundary

PASS proves:

- 64 tuning tracks and 48 frozen holdout tracks run;
- no tuning ID or creator overlaps the holdout;
- the strategy is selected using tuning data only;
- holdout metrics are emitted after selection;
- original audio is runtime-only and never uploaded;
- network work remains bounded.

A green run does **not** mean 85–90% human naturalness. It measures known-loop recovery / audition coverage only.
