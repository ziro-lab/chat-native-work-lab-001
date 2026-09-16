# VideoItem media-length / PlaybackRate2 mapping

## Question

For YMM4 v4.56.1.0 and a real deterministic media file, does `VideoItem.ContentLength` encode the same constant-rate source-time relationship expected by the recording archive planner?

## Fixture

GitHub Actions generates a redistribution-safe 30-second, 60fps H.264 MP4. Inside the exact YMM4 host, the probe creates `VideoItem` fixtures for 50%, 100%, and 200% constant `PlaybackRate2`, each at source offsets 0s and 5s.

For each case it records `OriginalContentLength` and `ContentLength` and checks:

- the original media length is positive and consistent;
- slower rate yields longer timeline content length and faster rate yields shorter length;
- a larger `ContentOffset` shortens remaining content length;
- `ContentLengthSeconds * PlaybackRate2 / 100` approximately equals `OriginalContentLengthSeconds - ContentOffsetSeconds` within a small media/frame tolerance.

## PASS boundary

A PASS supports the constant-rate source-duration relation used by the archive planner for this host version and fixture codec.

## NOT PROVEN

This does not prove animated/variable rate, reverse playback, every codec, or exact rendered-frame identity. A clock-frame render test remains the strongest optional follow-up.
