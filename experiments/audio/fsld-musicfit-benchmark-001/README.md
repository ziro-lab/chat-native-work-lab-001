# FSLD Music Fit benchmark 001

## Goal

Measure the current CPU-only `music-fit-core-001` on a broader commercially safe audio sample before changing scoring weights.

The sample is drawn only from FSLD items that pass the strict commercial-safe gate:

- 24 CC0 loops
- 24 CC BY loops

The selection is deterministic and audio is fetched from the 8.8 GB ZIP by HTTP Range requests only. No full archive download and no third-party audio artifact upload.

## Fixture construction

Each source is a known loop, but the analyzer is not given its period.

For every accepted loop:

1. decode the original WAV;
2. make three mild, deterministic render variants with different filtering/compression/noise;
3. build a single pseudo-song:
   `fade-in fragment -> variant A -> variant B -> variant C -> fade-out fragment`;
4. call the normal `musicfit.analyze()` on that single waveform;
5. reveal the original loop period/body only after analysis.

This avoids a byte-identical copy benchmark while preserving an exact hidden loop period.

## Metrics

- strict known-period Top1
- strict known-period Top3
- pair-valid rate (period match + both endpoints inside repeated body)
- no-edge rate
- fallback-feature usage
- per-license results
- analysis runtime
- ranged network bytes

The first run is diagnostic: quality is **not** part of PASS. A low score is useful evidence for the next tuning experiment.

## PASS boundary

- exactly 48 commercially safe, valid loops are analyzed (24 CC0 + 24 CC BY);
- source ZIP / license gate remain bounded and conservative;
- every selected source has a valid WAV member and 2–30 s duration;
- no original or transformed audio is uploaded;
- aggregate/per-source diagnostic metrics are emitted;
- total ranged network traffic remains below 384 MiB.

This is a broader loop-recovery benchmark, not a human naturalness test for arbitrary songs.
