# Navigation target context — YMM4 4.56.1.0

## Question

Can a real Timeline Tool on YMM4 Lite 4.56.1.0 receive the current Timeline, obtain a public FPS context and selected VideoItems, retain their session-local references independently of selection, and navigate the public integer playhead without editing the Items?

This is a narrow version-update/context probe for Highlight Navigator W1. It does not modify the recording-archive experiments or rediscover their already-proven PlaybackRateMap formula.

## Reuse

- Recording-archive evidence at `0b69aed70a3f8a84cc2ede539d794cb4e8108677`: PlaybackRate2, map method signatures and constant-positive source-time meaning.
- Selection and playhead experiments on 4.55.1.1: hypotheses to revalidate on 4.56.1.0, not evidence of compatibility by themselves.

## Procedure / required claims

The runner generates a disposable MP4 and starts the pinned real host. A real Tool menu command opens the registered Timeline Tool; its SetTimelineToolInfo callback must be received. The probe reads public FPS metadata, inserts two separate VideoItems using the same source, snapshots their references, changes selection, and checks that the snapshot and Item parameters remain unchanged. It then exercises integer Timeline.CurrentFrame and a non-frame-aligned source timestamp through the host map. The rounding-to-frame policy is a product decision: this probe records a fractional item-local result rather than pretending that it establishes a universal host rounding rule.

Required IDs are independently enumerated in RunNative.ps1. The producer cannot reduce the requirement set and still pass. Output contains checkout/source identity, host version, requirement results and the exact FPS access path.

## PASS boundary

After a successful run, PASS establishes only the named model/API assertions on 4.56.1.0. Session object-reference identity is not a durable identity across project reloads. Consumers must invalidate/rebind targets when the Timeline/Project instance changes and reject detached/changed Items.

## NOT PROVEN

- physical keyboard/mouse interaction or decoded preview-frame correspondence;
- stable item identity across restart, project reload, deletion/recreation or Undo/Redo;
- animated, zero, negative, reverse or non-monotonic playback;
- other YMM4 versions;
- Navigator product integration, end-user UX or installation;
- archive cutting, relinking or saving.

## Status

PREPARED. Native acceptance is recorded after the workflow finishes. Never infer PASS from build-only or artifact-upload success.
