# Recording Archive zero-percent EOF boundary

## Question

What happens when a 0% VideoItem freezes exactly at, or one frame before, the media end, and can YMM4's bundled FFmpeg decode a frame at those source times?

## Method

A synthetic 60 fps MP4 is loaded by a real YMM4 v4.56.1.0 process. The probe sets PlaybackRate2 to 0% and observes PlaybackRateMap at:

- one frame before ContentLength;
- exactly ContentLength.

It also asks YMM4's bundled ffmpeg for one decoded frame at both timestamps using framehash output.

## PASS boundary

PASS means the boundary behavior was observed. It does not assume that exact EOF is decodable; an observed no-frame result is valid evidence for a product-side clamp/fallback.

## Result

For the three-second 60 fps fixture:

- 0% at `2.983333...` s (one frame before EOF) freezes correctly and ffmpeg decodes one frame.
- 0% at exact `3.000000` s also maps to exactly 3 s in PlaybackRateMap/native VideoSource calculation.
- YMM4 does not clamp that exact EOF time before the file source.
- ffmpeg decodes **zero frames** at exact EOF.

Recording Archive therefore needs a product-side last-decodable-frame clamp/fallback for a zero-width use at exact EOF.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
