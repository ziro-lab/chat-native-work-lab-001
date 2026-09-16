# PlaybackRateMap source-time behavior

## Question

Can a YMM4 plugin use the host's own non-public `BaseItem.PlaybackRateMap` as the source-time authority for recording archive planning, and does it reproduce the native constant-rate semantics for 50 / 100 / 200%?

## Fixture

For each constant rate, a `VideoItem` is configured with:

- `Length = 300` frames at 60fps (5 item seconds)
- `ContentOffset = 4` source-time seconds
- `PlaybackRate2 = 50 / 100 / 200`

The probe retrieves `PlaybackRateMap` through one narrow reflection boundary and invokes its runtime methods. It verifies `IsConstant`, `FirstRate`, forward `GetSourceTime`, and inverse `FindFirstTimeForSourceTime` behavior.

## Observed behavior

For the actual `VideoSource` path, constant-rate mapping is:

`sourceTime = ContentOffset + itemTime * rate / 100`

Therefore `ContentOffset` is already a source-media offset and is **not** multiplied by playback rate.

Verified for 50 / 100 / 200% at multiple interior item times. Forward and inverse mapping agree within the item body. `GetSourceTime` accepts the exact item end, while `FindFirstTimeForSourceTime` returns null there; inverse lookup therefore behaves as a half-open item range `[0, Length)`.

The initial contrary inference came from a different `TachieSource` lip-sync branch and was falsified by this direct behavior test.

## API boundary

- `BaseItem.PlaybackRateMap` getter is non-public, so product access needs one narrow version-pinned adapter.
- Once the map instance is obtained, `GetSourceTime`, `FindFirstTimeForSourceTime`, `IsConstant`, and `FirstRate` are public on the runtime map type.

## PASS boundary

A PASS proves the constant positive 50 / 100 / 200% source-time mapping and inverse behavior of YMM4 v4.56.1.0's own `PlaybackRateMap`.

The recording-archive MVP may use this native map instead of independently reimplementing YMM4's time semantics. Arbitrary animated/non-monotonic or reverse rates remain outside this experiment's product guarantee.

## Evidence

- Source head: `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`
- Workflow run: `35113196298`
- Artifact: `10452663798`
- Artifact SHA256: `56e7c20f619dbba448c8272a1997862aae94b1491c9077b783e29308fc6f51de`
