# Track C native investigation

Track A (#70) and Track B (#71) passed independently before integration. Their source remains frozen; all integration experiments are contained in Track C.

## 1. Direct integration control

Source `1952997b7c45374dec41976f258db15d93d43acb`, run `35637614185`.

Direct folded Top/Height correction was allowed to run while native drag was active. After native movement, the display adapter repeatedly rewrote Top and called FastCanvas UpdateAll. That fed back into the mouse route and hit the bounded application guard. This established a real integration conflict; it does not prove the older #63 timeout had exactly the same cause.

## 2. Early hybrid

Source `cbc8bbcd1b342aeb6db3d23cd0651a10cd4411bb`, run `35638342424`.

Single/multi non-uniform drag, exact native Frames, live-identity Undo/Redo, post-drag right/add, nested stability and detach advanced substantially, but entering gesture display from mouse-down broke ordinary click. A 4.56.1.0 harness issue also showed Bootstrap's cached VM can differ from the actually visible TimelineView DataContext; subsequent candidates bind the exact visible VM/model before creating fixtures.

## 3. Freeze after native bubbling MouseMove

Source `f9b958a3cb613fdc700f1ef9b5ed4515160b79fa`, run `35660463405`.

The display transition was moved out of PreviewMouseMove. The first native TimelineItemView bubbling MouseMove runs first; only then is direct display frozen for the rest of the gesture. This eliminated the route break and produced **104/105** passing assertions on both hosts. All functional drag/Undo/nested/detach behavior was green. Only drag-preview continuity remained red.

## 4. Virtualization and preview diagnosis

A temporary `TimelineViewModel.Viewport.Value` height lease was introduced using the already-proven public viewport surface. Run `35661325619` reduced missing same-stack visual observations from 14 to 3 while preserving all other behavior.

Those three misses were exactly the first L6->L9 transition observations: one single item plus two selected multi items. Same-stack `TranslatePoint` was therefore not accepted as rendered evidence.

A WPF Render-priority audit was added, with explicit requirements that audits are sampled and fully drained. Run `35661769479` proved the remaining three failures were instead caused at gesture start: anticipatory compensation was incorrectly applied to L6->L6 even when Layer did not change, producing one -32px frame per selected item.

## 5. Accepted C input candidate

Source `722f6f487c6f573fa6bb87cabb9d1941c94bb65f`, run `35662155334`.

Anticipatory native-Top compensation is now applied **only when the logical Layer will actually change**. Result:

- YMM4 4.55.1.1: `PASS_TRACK_C_INPUT`, 107/107;
- YMM4 4.56.1.0: `PASS_TRACK_C_INPUT`, 107/107;
- Render audits sampled and drained;
- `gesture_previews_remained_visible=True`;
- single/multi L6->L9, exact Frame, live Undo/Redo, right/add, marquee, nested collapse/reopen, L20 virtualization, idle and detach all green;
- no Harmony loaded/referenced.

Artifacts and SHA256 are recorded in README.md and PR #72.

## Remaining architecture gates

The core input/display integration is no longer the current blocker. Full plugin work can move to route coverage and product behavior:

1. integrated FileDrop and additional native add routes;
2. automatic host navigation/scroll correction;
3. structural layer edits and folder-range tracking;
4. diverse item types and grouping semantics;
5. folder model/persistence/UI/package and production-scale acceptance.

Do not infer these from the core Track C PASS.
