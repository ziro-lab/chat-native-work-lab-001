# VideoItem media-length / PlaybackRate2 observation

## Question

For YMM4 v4.56.1.0 and a real deterministic media file, can `VideoItem.ContentLength` be used to derive source-time usage after applying `PlaybackRate2` and `ContentOffset`?

## Fixture

GitHub Actions generates a redistribution-safe 30-second, 60fps H.264 MP4. Inside the exact YMM4 host, the probe creates `VideoItem` fixtures for 50%, 100%, and 200% constant `PlaybackRate2`, each at source offsets 0s and 5s.

## Observed behavior

The initial falsification run showed that both `OriginalContentLength` and `ContentLength` remained 30 seconds in all six cases:

- 50 / 100 / 200% at offset 0s;
- 50 / 100 / 200% at offset 5s.

Therefore `ContentLength` does **not** encode the remaining source range or constant playback-rate scaling needed by the recording archive planner.

## PASS boundary

A PASS proves, for the exact tested YMM4 v4.56.1.0 host and H.264 fixture, that:

- `OriginalContentLength` exposes the media duration;
- `ContentLength` is invariant across the tested `PlaybackRate2` and `ContentOffset` values;
- archive source-time planning must not infer consumed source duration from `ContentLength`.

## NOT PROVEN

This experiment does not establish the actual source-time mapping formula. That mapping must be obtained from the playback/source path or a rendered clock-frame test.
