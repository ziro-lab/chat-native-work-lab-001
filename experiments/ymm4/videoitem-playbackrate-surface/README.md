# VideoItem playback-rate surface

## Question

For YMM4 v4.56.1.0, which `VideoItem` / `Animation` members represent the actual constant video playback rate used for source-time mapping, and how are `PlaybackRate`, `PlaybackRate2`, `ContentLength`, and `OriginalContentLength` exposed?

## Status

- Date: 2026-09-16
- YMM4 version: v4.56.1.0 Lite
- Evidence type: native automated discovery

## Assertions

The probe must run inside the exact YMM4 host and record:

- `VideoItem.PlaybackRate`
- `VideoItem.PlaybackRate2`
- public members of the concrete `PlaybackRate2` animation object
- `ContentLength`
- `OriginalContentLength`

It must also identify any public `GetValue` / constant-value path that can be used later by a 50% / 100% / 200% behavioral fixture.

## PASS boundary

A PASS proves only that the playback-rate data surface required for the next source-time mapping experiment is discoverable in YMM4 v4.56.1.0.

## NOT PROVEN

This experiment does not yet prove the source-time formula, variable-rate behavior, reverse playback, or archive correctness.

## Downstream impact

Consumer: `ziro-lab/ymm4-plugin-garage` recording-archive candidate.
