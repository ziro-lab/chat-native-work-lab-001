# FSLD holdout label audit 001

This experiment keeps the strict publisher-whole-loop metric frozen, while checking whether strict misses are annotated beat/bar-compatible integer sub-periods.

Inputs: the fixed 48-source holdout from run 35310322059 plus FSLD metadata read by bounded HTTP Range. No audio member is fetched.

Candidate labels:
- strict whole-file period (±70 ms)
- near-integer sub-period factor 2–8
- integer annotated-beat span
- whole annotated-bar span when meter/signature is available
- practical positive = strict whole period OR bar-compatible integer sub-period

No 4/4 assumption is made when meter is unavailable. This is a label audit, not a claim of perceptual naturalness.

PASS means all 48 IDs are found, the frozen strict metrics reproduce exactly, no audio is read, and annotation-aware diagnostics are emitted. Music Fit thresholds/weights are untouched.
