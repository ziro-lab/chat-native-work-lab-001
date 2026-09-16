# VideoItem source-time IL surface

## Question

In YMM4 v4.56.1.0, which native methods consume `PlaybackRate2`, `ContentOffset`, and `PlaybackRateMap` on the actual video playback/source path?

## Method

A native-host probe walks method bodies in the loaded YukkuriMovieMaker assemblies, resolves IL metadata tokens, and records methods that reference the source-time inputs. For hit methods it emits a compact resolved IL listing without relying on an external decompiler.

## Observed structure

For the native video path:

- `YukkuriMovieMaker.Player.Video.Items.VideoSource.Update()` reads `BaseItem.PlaybackRateMap` and `BaseItem.ContentOffset`;
- `VideoSource.Update()` calls `VideoSource.CalculateSourceTime()`;
- `VideoSource.CalculateSourceTime()` delegates to `PlaybackRateMap.GetSourceTime(...)`.

A separate `TachieSource.GetMouthShape()` lip-sync branch contains its own constant-rate arithmetic. That branch is **not** the authority for normal VideoItem source-time mapping and must not be generalized to recording archive planning.

The behavioral `playbackratemap-source-time` experiment is the authority for the actual constant VideoItem mapping.

## PASS boundary

A PASS proves that the native VideoItem playback path routes source-time calculation through `PlaybackRateMap` for the exact YMM4 v4.56.1.0 build.

Static IL alone does not define every future/version/codec behavior; the corresponding `PlaybackRateMap` behavior experiment supplies the runtime proof used downstream.

## Evidence

- Source head: `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`
- Workflow run: `35113196449`
- Artifact: `10453785359`
- Artifact SHA256: `87c2b609b27d45930ddcc982123707d90cf6029cbf2817f4890e159fc326ebf7`
