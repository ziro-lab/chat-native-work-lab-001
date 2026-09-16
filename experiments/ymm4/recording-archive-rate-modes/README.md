# Recording Archive playback-rate modes

## Question

Can Recording Archive preserve YMM4 VideoItems at 0%, negative/reverse playback, and animated/variable playback without rebuilding the user's PlaybackRate2 curve?

## Result

Verified on exact YMM4 Lite 4.56.1.0.

The implementation can be generalized around YMM4's own `PlaybackRateMap` rather than a single positive constant rate:

- `PlaybackRate2=0%` is a valid constant map. Every sampled item time resolves to the same source time and `GetConsumedContentRange()` is zero-width.
- Negative values are accepted by `PlaybackRate2` and represent reverse playback. A `-100%` five-second item reports consumed range `[-5s, 0s]`.
- Reverse playback starts from the content-end side. `GetSourceTime(..., out startsFromContentEnd)` reports this explicitly.
- `GetConsumedContentRange(itemLength,fps)` exposes the actual source-time envelope for constant and animated maps, including backward and forward consumption.
- Positive animated playback was exercised across the available `AnimationType` values. Changing only ContentOffset translated the complete source-time mapping by the same amount; the PlaybackRate2 curve itself did not need rebuilding.
- A crop/relink coordinate transform was proven for constant reverse, positive-to-negative variable playback, and negative-to-positive variable playback. After translating into the shorter archive clip, sampled source times matched `oldSourceTime - clipStart` while preserving the same origin direction.

Therefore Recording Archive should preserve the serialized `PlaybackRate2` unchanged and use PlaybackRateMap only to determine the consumed source envelope and the source-origin convention. The archive rewrite should change only the media path and the ContentOffset needed for the new clip coordinate system.

## Archive coordinate rule

Let the old item source time at item-time zero be `oldAnchor`, let the stream-copy clip begin at `clipStart`, and let its local content length be `clipLength`.

```text
desiredLocalAnchor = oldAnchor - clipStart

if startsFromContentEnd == false:
    newContentOffset = desiredLocalAnchor
else:
    newContentOffset = clipLength - desiredLocalAnchor
```

The same PlaybackRate2/PlaybackRateMap then produces the same source mapping in clip-local coordinates. Product code must validate the resulting archive after save/reload rather than assume a guessed formula is correct.

## 0% point ranges

A 0% item consumes a point rather than a positive-duration interval. The archive planner must keep the semantic used range as a point while requesting a small positive media span for stream-copy/decoding when user handles are both zero. This is an implementation detail; the archived YMM4 item keeps PlaybackRate2=0% unchanged.

## Evidence

- Workflow run: `35133598162`
- Job: `104920324448`, completed / success
- Result: `status=PASS_ARCHIVE_RATE_MODES_SURFACE`
- Assertions: **402 PASS**
- Artifact: `10462371524`, `ymm4-recording-archive-rate-modes`
- Artifact SHA256: `0c349a50f83ced538f33eb53b03510e49c8501551defdace0b8e6e7c6ba7a64d`

The probe is evidence for YMM4 mapping behavior. Product acceptance must still exercise actual stream-copy, detached project rewrite, save/reload, and original-file immutability before the new modes are considered delivered.
