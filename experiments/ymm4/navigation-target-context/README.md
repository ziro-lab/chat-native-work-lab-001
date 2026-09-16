# Navigation target context — YMM4 4.56.1.0

## Question

Can a real Timeline Tool receive the current Timeline, obtain public FPS metadata and selected VideoItems, retain session-local references independently of selection, and navigate the integer playhead without editing the Items?

This version-update/context experiment supports Highlight Navigator W1. It does not modify recording-archive experiments or rediscover their PlaybackRateMap formula.

## Reuse

- Recording-archive evidence at `0b69aed70a3f8a84cc2ede539d794cb4e8108677`: PlaybackRate2 and native map semantics.
- Selection/playhead experiments on 4.55.1.1 supplied hypotheses, not proof of compatibility with 4.56.1.0.

## Observed result — PASS

Real YMM4 Lite **4.56.1.0**, release SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`.

All 16 independently required assertions passed; build warnings/errors were 0/0.

- Actual Tool menu invocation delivered `ITimelineToolViewModel.SetTimelineToolInfo`.
- `TimelineToolInfo.Timeline` is public.
- Public FPS path is **`Timeline.VideoInfo.FPS`**, observed value 60.
- Public `Timeline.SelectedItems` yielded two separate `VideoItem` objects pointing at the same generated recording.
- A saved array of object references remained intact and those Items remained in `Timeline.Items` after selection was cleared. Selection notifications were observed.
- Public integer `Timeline.CurrentFrame` accepted/read back positions in both Item occurrences.
- The adopted native PlaybackRateMap returned a fractional item-local inverse time of **0.5173 seconds**. A consumer-selected ceiling policy mapped that to local frame 32 at 60fps.
- The recorded Item parameters were unchanged by the navigation operations.

The ceiling choice is a **product rounding policy**, not a claim that the host rounds all timestamps that way. Consumers must check that the chosen frame actually lies inside the candidate and the half-open Item interval.

## Evidence

- Source head / tested checkout: `3b52a3acff544ed0ec759d657f48364a415c485c`
- Source tree: `19d3b10569103addb79c63653ed0c8e49f85b1d4`
- [Workflow run 35120206452](https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/35120206452)
- Job: `104875765200`
- Artifact: `10457134077`
- Artifact ZIP SHA256: `78b2b65c8ea6e7438e0f79775388d2d5a6d8159df25a69f0038ec5650931d2ab`
- Result: `PASS_NAVIGATION_CONTEXT`
- Actual FPS path: `Timeline.VideoInfo.FPS`

`RunNative.ps1` enumerates the required IDs independently of the producer and rejects missing/duplicate/failed assertions, wrong schema/host/checkout or stale evidence. This documentation-only update does not change tested code or need another host run.

## PASS boundary

PASS establishes the named model/API assertions only on the pinned host. Session object-reference identity is not durable identity across project reloads. Consumers must invalidate/rebind targets when the Timeline/Project instance changes, and reject detached or changed Items.

## NOT PROVEN

- physical keyboard/mouse operation or decoded preview-frame correspondence;
- stable identity across restart, project reload, deletion/recreation or Undo/Redo;
- animated, zero, reverse, negative or non-monotonic rates;
- arbitrary FPS values, other YMM4 versions;
- Navigator product integration, end-user UX or installation;
- archive cutting, relinking or saving.

## Downstream use

Navigator may use the public Timeline/FPS/selection/playhead surfaces and keep a session reference locator behind its host adapter. Product tests still need explicit snapshot lifetime, stale-target rejection, same-source multi-occurrence projection and real Plugin integration checks. This PASS is not a blanket W1 product PASS.
