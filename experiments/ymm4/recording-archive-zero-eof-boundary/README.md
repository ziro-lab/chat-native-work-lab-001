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
