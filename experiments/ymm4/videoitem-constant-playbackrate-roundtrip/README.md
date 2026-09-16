# VideoItem constant PlaybackRate2 roundtrip

## Question

For YMM4 v4.56.1.0, do constant 50% / 100% / 200% `VideoItem.PlaybackRate2` values and `ContentOffset` survive a native project save / reload roundtrip?

## Status

- Date: 2026-09-16
- YMM4 version: v4.56.1.0 Lite
- Evidence type: native automated behavior

## Fixture

The native host creates three `VideoItem` instances with deterministic remarks, identical timeline length, identical non-zero `ContentOffset`, and constant `PlaybackRate2` values of 50 / 100 / 200 using `Animation.SetFirstValue()`.

The project is saved and reloaded through YMM4. For each reloaded item, the probe evaluates `PlaybackRate2.GetValue()` at the start, middle, and final timeline frame and verifies the expected constant rate and the original `ContentOffset`.

## PASS boundary

A PASS proves that constant `PlaybackRate2` and `ContentOffset` values survive YMM4's native project serialization roundtrip for the tested host version.

## NOT PROVEN

This experiment does not by itself prove that source media time advances by exactly `rate / 100` in the decoder. A later clock-video experiment must verify actual source-frame correspondence. It also does not prove animated/variable rate or reverse playback.

## Downstream impact

The recording archive plugin should use `PlaybackRate2`, not obsolete `BaseItem.PlaybackRate`, as the rate input for any source-time planner.
