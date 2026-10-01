# Stopped preparation: two-stage one-frame round trip

Question: after synthetic Tachie preparation completes, can a public Timeline.CurrentFrame neighbor move repaint a stopped real YMM4 preview when restoration waits until the real ITachieSource2.Update has actually observed the temporary frame?

This revision follows the completed negatives for parameter notification, same-frame assignment, and an immediate same-UI-turn one-frame round trip. It changes one thing: after readiness, set F±1, wait for the probe's real Source Update to observe that target frame, then restore F and wait for a real Source Update at F.

No private Seek, direct host Update, Play, fake parameter edit, Undo operation or product code is used. The final frame, persisted parameter/timeline JSON, selection and measured Undo state must be unchanged. A no-notification/no-frame-action control must stay red.

PASS_TWO_STAGE means the control stays red, the action phase becomes green, both target and restored frames are observed by real host Source Updates, and final invariants hold. A completed no-repaint result is a valid negative, not a PASS. The project has no audio; transient playhead/pixel flicker and audio side effects remain NOT PROVEN.

Previous protocol/history remains visible in git; this file describes only the current revision.
