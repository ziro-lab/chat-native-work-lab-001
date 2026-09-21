# Isolated no-Harmony Track A

Authority: PR #63, `../no-harmony-full-round2/WORK_PLAN.md` at `e47546dd7b97dbe9ee3737a534ca39466a92229c`.

This experiment answers only whether generalized FolderLayout input can preserve native editing on a RenderTransform display. It does not extend the direct Top/Height probe. No Track B/C or structural-edit work is included.

## Acceptance

Both pinned YMM4 4.55.1.1 and 4.56.1.0 must pass every boolean assertion. Required scenarios: A `[2..3],[6..8]`; B `[1..5],[2..3],[6..8]`; normal transformed clicks; right-click and standard add-position converter; Shift rectangle; L6 -> L9 native diagonal drag; exact frame delta against an unfolded native control gesture; single/multi-item Undo/Redo; dynamic parent collapse; detach and restored native click.

The multi-item fixture has two independent selected items on L6. Different-row selected-group semantics and hidden-destination policy remain NOT PROVEN by this fixture.

## Boundaries

- RenderTransform / opacity / hit testing only; host VM Top and Height are read, never written.
- No FastCanvas.UpdateAll or CanvasHeight rewrite.
- Actual Win32 mouse/keyboard input, not direct invocation of private drag handlers.
- Native drag owns frame movement and Undo; only logical Layer is post-corrected.
- A whole-window scale of 0.5 is a **CI fixture accommodation** so all 11 logical rows are realized on small desktops. It is restored at teardown. It is not a product UI change or virtualization proof.
- Periodic transform refresh is scoped and disposed; this is not a production-performance claim.
- Normal click must not create a layer correction.
- Workflow success requires the final strict marker and zero false assertions; progress is flushed before/after host calls so a timeout cannot be called PASS.

References checked: existing native drag/multi-drag probes from #61; #63 FolderLayout and failed mixed probe; `docs/YMM4_REFERENCE_SOURCES.md`; `docs/YMM4_PLUGIN_SURFACE_GUIDE.md`; Microsoft WPF Transforms Overview (RenderTransform is applied after layout). No third-party implementation code is copied.

## NOT PROVEN

Native results are pending the first run. No integrated Full plugin, direct layout stability, production lifecycle, persistence, structural edit tracking, package, or release compatibility is claimed.
