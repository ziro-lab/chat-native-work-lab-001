# Recording Archive loop-playback surface

## Question

Is VideoItem loop playback fully represented by PlaybackRateMap, or does native VideoSource apply additional IsLooped/content-boundary logic that Recording Archive must preserve separately?

## Method

The native probe:

- compares PlaybackRateMap source-time outputs with the same VideoItem toggled IsLooped=false/true;
- uses a deliberately short content length so source time crosses the media boundary;
- scans loaded YMM4 IL for native methods that reference VideoItem.IsLooped together with video source-time/content members;
- records VideoSource.CalculateSourceTime signatures and relevant resolved IL.

## PASS boundary

PASS establishes where loop semantics live in exact YMM4 v4.56.1.0. It does not by itself authorize product support for looped archive items.

## Result

`PlaybackRateMap` itself is identical with `IsLooped=false/true`. Loop semantics are applied later by native `VideoSource.CalculateSourceTime`, which consumes `IsLooped` and the actual file-source duration.

Observed mappings change when the underlying archived clip is shortened. A three-second source loop and a one-second shortened clip produce different repeated source times for the same item times. The mapping also changes when file-source duration differs from content length.

Therefore trimming a looped VideoItem and keeping `IsLooped=true` is **not equivalent by default**. The current product fail-closed behavior is justified.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
