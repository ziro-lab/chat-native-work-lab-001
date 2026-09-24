# PlaybackRateMap constant positive source-time mapping

- Status: canonical
- Knowledge class: LAB-NATIVE + LAB-STATIC
- Surface: S3
- YMM4 version: 4.56.1.0
- Observed / inspected: canonical Lab experiment
- Revalidation trigger: YMM4 changes BaseItem playback-rate internals or VideoSource timing

## Claim

For the tested YMM4 4.56.1.0 `VideoSource` path at constant positive 50%, 100% and 200% rates, the host's own PlaybackRateMap produced:

`sourceTime = ContentOffset + itemTime * rate / 100`

`ContentOffset` is already a source-media offset and is not multiplied by playback rate.

The tested inverse lookup behaves as a half-open item range `[0, Length)`.

## Safe use

A feature that explicitly accepts this version-pinned S3 dependency may use a narrow adapter to the native map instead of independently reimplementing the tested constant-positive timing semantics.

## Do not infer

- `BaseItem.PlaybackRateMap` is not a public plugin-facing getter in the tested host.
- This card does not guarantee arbitrary animated, non-monotonic or reverse rates.
- Do not generalize the S3 member name/shape to other YMM4 versions without revalidation.

## Failure behavior

If the exact validated map member/type cannot be resolved, fail closed or use a separately proven lower-risk route. Do not search for a vaguely similar private member and continue.

## Evidence

- Lab experiment: [playbackratemap-source-time](../../experiments/ymm4/playbackratemap-source-time/)
- Source head: `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`
- Workflow run: `35113196298`
- Artifact: `10452663798`
- Artifact SHA256: `56e7c20f619dbba448c8272a1997862aae94b1491c9077b783e29308fc6f51de`
