# Recording Archive non-zero container timestamp

## Question

Does YMM4 v4.56.1.0 accept a VideoItem backed by a valid AV container whose format start_time is non-zero, even though the current Recording Archive candidate rejects such media conservatively?

## PASS boundary

PASS records whether YMM4 loads the media and reports positive content duration. It does not prove that the current stream-copy relink math is safe for non-zero-start containers.

## Result

A synthetic MKV whose container `start_time` is about +5 s loads successfully in real YMM4. The VideoItem reports:

- `ContentLength = 3 s`
- `OriginalContentLength = 3 s`
- observed model source samples `0, 1, 2` seconds

YMM4 therefore exposes the media to VideoItem timing in a zero-based content domain despite the non-zero container timestamp. The current Recording Archive rejection is conservative rather than a YMM4 host requirement. Product FFmpeg/ffprobe coordinate translation still needs its own integration proof before support is enabled.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
