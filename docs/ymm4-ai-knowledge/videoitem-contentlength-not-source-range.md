# VideoItem ContentLength is not consumed source range

- Status: evidence-qualified
- Repository state: main
- Knowledge class: NEGATIVE-FINDING + LAB-NATIVE
- Surface: S1
- YMM4 version: 4.56.1.0
- Observed / inspected: canonical Lab experiment
- Revalidation trigger: YMM4 changes VideoItem media-length or PlaybackRate2 semantics

## Claim

For the tested deterministic 30-second H.264 media on YMM4 4.56.1.0, `VideoItem.ContentLength` remained the media duration across constant `PlaybackRate2` values 50%, 100% and 200% and source offsets 0s and 5s.

It therefore did not encode the consumed/remaining source range required for archive planning.

## Safe use

Use `ContentLength` as media-length information only within the proven boundary.

Do not infer consumed source duration from it when playback rate or content offset matters.

## Do not infer

- This experiment does not establish the general source-time mapping formula.
- It does not cover every media type or every future YMM4 version.
- It does not by itself prove animated, reverse or non-monotonic playback behavior.

## Failure behavior

When a feature requires exact source-time usage, use a source-time authority that is separately proven for that path rather than deriving it from `ContentLength`.

## Evidence

- Lab experiment: [videoitem-media-length-rate-mapping](../../experiments/ymm4/videoitem-media-length-rate-mapping/)
