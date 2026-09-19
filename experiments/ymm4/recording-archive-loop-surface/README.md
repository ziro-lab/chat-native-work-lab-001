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
