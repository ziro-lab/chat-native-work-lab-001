# Stopped preparation: routed two-stage SeekWithoutSnap round trip

Question: after synthetic Tachie preparation completes, can YMM4's public routed `SeekWithoutSnap` command repaint a stopped real preview if the probe waits for a real `ITachieSource2.Update` at the temporary neighbor frame before routing back to the original frame?

This follows valid negatives for one owner-parameter notification, same-value CurrentFrame assignment, immediate CurrentFrame round trip, and same-frame SeekWithoutSnap.

## Protocol

- Control: preparation becomes ready, no signal; preview must stay red.
- Action: at readiness capture F; issue `SeekWithoutSnap(F±1)`; wait up to four seconds for both Timeline.CurrentFrame and real paused source callback at that target; only then issue `SeekWithoutSnap(F)`; wait for the real source callback at F.
- A subsequent host Update is the only code allowed to apply the green GPU input. The completion worker never calls host Update or touches GPU state.
- Final Timeline serialization, persisted parameter JSON, selection and measured Undo state must remain unchanged. If playback, owner/timeline identity, or an unexpected frame changes, restoration is not forced across that state.
- The project has no audio. Transient playhead/pixel flicker and live user races remain outside this automated evidence.

## Verdict

`OBSERVED_ROUTED_TWO_STAGE_REPAINT` requires the no-action control to stay red, two routed commands, real target and restored source callbacks, final original stopped frame and green preview.

`VALID_NEGATIVE_ROUTED_TWO_STAGE` requires the same invariants but final preview remains red.

BLOCKED/FAIL does not support adoption.
