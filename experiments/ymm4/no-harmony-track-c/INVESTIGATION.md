# Track C native investigation

A (#70) and B (#71) passed independently before integration. The original A/B files remain frozen; GestureDisplay.cs is an explicit C candidate fork of B while the integration boundary is investigated.

## Direct integration control

Source1952997b7c45374dec41976f258db15d93d43acb / run35637614185.
4.55.1.1 artifact10656812328, SHA256 d4061cd8212358ddf923c13df10f37547b5cec2fde63b173b5afc674b5683052.

Fourteen assertions passed through normal click, right/add converter and marquee. At non-uniform drag, native item geometry and direct Top correction entered a repeatable feedback loop: Layer8/5 -> adapter Layer9 -> Top288 -> foldedTop192 -> canvas refresh -> another mouse route. The bounded guard stopped at application1001. No unbounded PASS or raised budget.

This locates a real integration conflict. It does not retroactively prove that the old #63 timeout had exactly the same cause.

## First hybrid candidate

Sourcecbc8bbcd1b342aeb6db3d23cd0651a10cd4411bb / run35638342424.
4.55.1.1 artifact10656703525, SHA2561d65685a624f55492aa676169433f36a6d6b26e57e34dd64eb05caaeb0d3e792.

102 assertions completed: single/multi non-uniform drag, exact native frames, live-identity Undo/Redo, post-drag right/add, nested layout stability and detach worked. Six normal-click assertions failed because the display transitioned on mouse-down before native hit/selection. Multi-item preview also recorded six missing-realized-view samples; these were subsequently promoted to explicit acceptance checks. Final marker remained FAIL.

4.56.1.0 artifact10657226624 instead stopped before native control because Bootstrap's cached VM was not the visible TimelineView's DataContext. The harness now resolves the actual visible VM/model before creating fixtures and checks it during navigation; that run is not evidence for or against hybrid behavior.

## Current candidate

Transition only after the native drag threshold, before bubbling move; ordinary click remains direct/native. Temporarily lease the actual item FastCanvas's Rect Viewport so selected native logical rows remain realized while their visible positions are compensated. Expand only the virtualization rectangle, not the ScrollViewer's user-visible viewport/extent. Restore the original binding on gesture end. Compensate measured cached visual Top rather than assuming VM Top equals arranged position. No per-MouseMove full canvas refresh. Exact DP presence/type is fail-closed; native result pending.

No Full product claim. FileDrop, automatic navigation variants, diverse item/group types, structural edits, persistence, UX and packaging are still separate gates.
