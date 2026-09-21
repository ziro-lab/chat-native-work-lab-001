# Isolated no-Harmony Track A

Authority: PR #63, `../no-harmony-full-round2/WORK_PLAN.md` at `e47546dd7b97dbe9ee3737a534ca39466a92229c`.

This experiment answers only whether generalized FolderLayout input can preserve native editing on a RenderTransform display. It does not extend the direct Top/Height probe. No Track B/C or structural-edit work is included.

## Verified native gate

Source `a1eebb4d6a4e0c4d6567ce49a59d4e70b3287b23`, run [35634693661](https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/35634693661).

Checkout `6a3f6cf4fd344ae80728e716bf56b02bcc424353`; tree `aaa5870bcc76804db343f03eab19f727be12c278`.

Both pinned hosts produced **PASS_TRACK_A, 48/48 strict assertions**. Artifacts were downloaded and their bytes/hashes checked.

| Host | Artifact | ZIP SHA256 |
| --- | --- | --- |
| 4.55.1.1 | 10656440075 | `f36a4858dd2559b28dfe29a5d0f2f575e03780885f3e9a205ce2e1df9449afef` |
| 4.56.1.0 | 10655880786 | `364fb216c3e3e3dba3be813b31b4411dd8b8dc64a67d48cfe246daa858512aed` |

## Acceptance coverage

A `[2..3],[6..8]`; B `[1..5],[2..3],[6..8]`; normal transformed clicks; right-click and actual standard add-position converter; Shift rectangle without seek; L6 -> L9 native diagonal drag; exact +64 frame delta against an unfolded control gesture; single/multi-item Undo/Redo; dynamic parent collapse; detach and restored native click.

Undo/Redo additionally checks all six live Timeline item identities/count, not just fields on retained references. The multi-item fixture has two independent selected items on L6. Different-row selected-group semantics and hidden-destination policy remain NOT PROVEN by this fixture.

## Harness correction

Without an explicit fixture boundary, native Ctrl+Z could undo programmatic fixture creation together with the first gesture. Retained item references then misleadingly had their original frame/layer even though Timeline.Items was empty. Saving the synthetic project did not separate history either (run 35634356265).

The exact public `MainModel.UndoRedoManager.Record()` was discovered in that run. The harness now calls it **once after fixture creation**, before native control input. No Record/Clear is injected into the input adapter or gesture helpers. Native drag and actual Ctrl+Z/Ctrl+Y own all subsequent editing/history.

The harness reaches MainModel through the known exact MainViewModel.model field. Product code should prefer the public TimelineToolInfo.UndoRedoManager route. This fixture correction does not establish that the old mixed probe's separate right-click timeout had the same cause.

## Boundaries

- RenderTransform / opacity / hit testing only; VM Top/Height are read, never written.
- No FastCanvas.UpdateAll or CanvasHeight rewrite.
- Actual Win32 mouse/keyboard input, not direct invocation of private drag handlers.
- A whole-window scale of 0.5 is a restored **CI fixture accommodation** so all 11 logical rows are realized on small desktops. It is not a product UI change or virtualization proof.
- Periodic transform refresh is scoped/disposed; this is not a production-performance claim.
- Normal click must not create a layer correction.
- Final strict marker, zero false assertions, phase/mutation progress flushed before host calls.

References: existing #61 native drag/multi-drag probes; #63 FolderLayout/work plan; docs/YMM4_REFERENCE_SOURCES.md and YMM4_PLUGIN_SURFACE_GUIDE.md; existing project-save-copy-roundtrip public save route; Microsoft WPF Transforms Overview. No third-party implementation code copied.

## NOT PROVEN

This is not an integrated Full plugin. Direct layout stability, unscaled production lifecycle, persistence, structural edit tracking, packaging and release compatibility remain separate gates. Track B can now proceed independently; Track C still requires B's own stability acceptance.
