# FSLD abstain fallback 001

## Goal

Rescue tracks where the normal Music Fit analyzer returns **no recurrence edge**, without weakening the normal path.

The frozen 48-source FSLD holdout has four such tracks (8.33%). Earlier tuning rejected a blanket lower similarity threshold, so this experiment keeps:

- normal analyzer unchanged;
- final local seam/context gate at the normal **0.78** threshold;
- fallback active **only after normal analysis abstains**.

The fallback may use a looser score only to propose periodicities. A proposal is not accepted as an edge unless its actual cut context still passes the 0.78 seam gate.

## Candidate generators

The experiment compares small CPU-only views:

- joint feature recurrence with a capped 8 s context;
- harmonic recurrence with a capped 8 s context;
- rhythm/energy recurrence with a capped 4 s context;
- a union of those views.

The normal analyzer compares up to a whole candidate period. Capping the proposal context can recover long, evolving repetitions without changing the final seam acceptance rule.

## Data separation

A fallback strategy is chosen using a **fixed 12-track disjoint tuning cohort** from commercial-safe FSLD. It is stratified by license (CC0 / CC BY) and archive-member size (small / large), three tracks per bucket. These tracks do not need to be normal-path abstains; they teach which fallback view best recovers known musical periods without an expensive abstain-mining scan.

The fixed 48-source holdout is not used for strategy selection. Holdout IDs and creators are excluded from the tuning cohort. Only after selection is frozen is the strategy evaluated on the four frozen holdout abstains.

## Labels

For diagnostics only:

- strict: recovered duration equals publisher whole-loop duration ±70 ms;
- audition-worthy: strict OR duration is >=4 near-integer annotated beats.

These are not human-naturalness labels.

## PASS boundary

PASS means:

- twelve disjoint stratified tuning tracks are evaluated;
- the chosen fallback is selected without holdout leakage;
- the four frozen holdout abstains remain normal-path abstains;
- fallback results are reported with final seam gate fixed at 0.78;
- no audio is committed or uploaded.

The fallback is not automatically promoted into Music Fit Core by this experiment.
