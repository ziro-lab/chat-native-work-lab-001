# PlaybackRateMap source-time behavior

## Question

Can a YMM4 plugin use the host's own non-public `BaseItem.PlaybackRateMap` as the source-time authority for recording archive planning, and does it reproduce the native constant-rate semantics for 50 / 100 / 200%?

## Fixture

For each constant rate, a `VideoItem` is configured with:

- `Length = 300` frames at 60fps (5 item seconds)
- `ContentOffset = 4` source-time seconds
- `PlaybackRate2 = 50 / 100 / 200`

The probe retrieves `PlaybackRateMap` through one narrow reflection boundary and invokes its native public/runtime methods. It verifies `IsConstant`, `FirstRate`, forward `GetSourceTime`, and inverse `FindFirstTimeForSourceTime` behavior.

The first behavioral run falsified an earlier inference taken from a different `TachieSource` lip-sync branch: `PlaybackRateMap.GetSourceTime(0, ..., ContentOffset=4s, rate=50%)` returned **4s**, not 2s.

For the actual `VideoSource` path, the constant mapping under test is therefore:

`sourceTime = ContentOffset + itemTime * rate / 100`

This distinction matters for archive relinking: `ContentOffset` is already a source-media offset and is not itself multiplied by playback rate by `PlaybackRateMap`.

## PASS boundary

A PASS proves that the exact YMM4 v4.56.1.0 `PlaybackRateMap` reproduces the constant-rate source-time mapping and inverse lookup used by native video playback. This is stronger than reimplementing the arithmetic independently.

## NOT PROVEN

This experiment does not yet claim arbitrary animated/non-monotonic speed curves are safe for archive range union. Product MVP may still limit support to constant positive rates while preserving the map adapter for later extension.
