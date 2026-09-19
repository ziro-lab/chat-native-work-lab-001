# YMM4 Timeline Playback Start Synchronization

## Question

On exact YMM4 4.55.1.1 Lite, after a Timeline Tool changes public `Timeline.CurrentFrame` while its own UI owns focus, does YMM4 playback actually start from that displayed frame?

This experiment was added after a manual observation: Template Placer moves the visible playhead to a Voice start, but pressing Play appears to resume from the previous playback position.

## Method

The real host is launched with a Timeline Tool probe.

The probe:

1. leaves the fresh playback state near project start;
2. gives keyboard focus to its Tool UI;
3. writes `Timeline.CurrentFrame = 180` and selects a synthetic Voice;
4. invokes the real PreviewViewModel public Play-like command;
5. records the first Timeline CurrentFrame changes after playback starts;
6. stops playback;
7. performs a real ruler click as a native-control case, records the frame produced by that click, starts playback again, and records the first playback frames.

It also inventories public PreviewViewModel members containing Play / Pause / Stop / Seek / Frame / Current / Position / Time.

No private playback field is used as product evidence.

## PASS boundary

`PASS_PLAYBACK_START_SYNC_OBSERVATION` means both playback-start sequences executed and their Timeline frame traces were recorded.

The result separately reports whether playback began near the programmatically displayed frame and whether it began near the real-ruler-click frame.

## NOT PROVEN

- other YMM4 versions;
- audio clock precision;
- exact decoded video frame;
- product fix route unless a public host surface is separately identified.
