# VideoItem split lifecycle — YMM4 4.56.1.0

## Question

When the real YMM4 host splits a selected `VideoItem` at the current playhead, what happens to:

- the original object reference;
- left/right `Frame`, `Length`, `ContentOffset`, `PlaybackRate2`, `FilePath`;
- the source-time ranges represented by the resulting pieces;
- current selection after the split?

This experiment is for Highlight Navigator's edit-while-review lifecycle. It asks whether an already analyzed source-time candidate can be rebound to the current split pieces instead of forcing a re-decode.

## Environment

- GitHub Actions `windows-latest`
- YMM4 Lite 4.56.1.0
- Host ZIP SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Generated FFV1 fixture, no private project/media

## Initial probe

The native probe inserts a constant-rate VideoItem, selects it, moves the playhead inside it, discovers the real host split command/surface, invokes it, and records the resulting item objects and ranges.

A PASS must be based on actual host mutation, not a synthetic clone performed by the probe.

## NOT PROVEN until the native run passes

- mouse/keyboard split gestures;
- Undo/Redo;
- project restart identity;
- arbitrary animated/reverse playback rates;
- Highlight Navigator product integration;
- moving a split piece by physical drag.
