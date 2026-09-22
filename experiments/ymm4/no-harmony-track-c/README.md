# Track C: integrated display and input

Authority: PR #63 frozen Track A/B/C work plan.

Prerequisites:

- Track A #70: source `a1eebb4d6a4e0c4d6567ce49a59d4e70b3287b23`, run `35634693661`, **48/48** strict assertions on YMM4 4.55.1.1 and 4.56.1.0.
- Track B #71: source `3d0ccecc5da14fa742a71bca850607bf2290ee86`, run `35636621293`, **120/120** strict assertions on both hosts.

The frozen A/B implementations remain untouched. Track C uses a C-local display candidate derived from B plus the same FolderLayout mapping authority and an input-only adapter.

## Verified integrated native gate

Source `722f6f487c6f573fa6bb87cabb9d1941c94bb65f`, run [35662155334](https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/35662155334).

- source tree: `fbf982356af0fdd4374b851c84a0a0c5a968d0b5`
- YMM4 4.55.1.1: artifact `10666968584`, ZIP SHA256 `c7c5863d35a6d573398cc258ea7e616c4467c557a4efec78a750b0acfa610f3a`
- YMM4 4.56.1.0: artifact `10667850564`, ZIP SHA256 `d34cd94bc3eb7327d044a8791e4e3d866c2fd10437dd861a331bb44fde07a6bd`

Both artifacts were downloaded and independently hashed. Both contain:

`status=PASS_TRACK_C_INPUT`

`assertion_count=107`

with no false boolean assertions.

## Proven in this gate

- unscaled native viewport with vertical virtualization;
- normal folded-item click without accidental Layer correction;
- right-click cursor + actual standard add-position converter before and after drag;
- Shift marquee without playhead seek;
- native single-item non-uniform L6 -> L9 drag;
- native same-row multi-item L6 -> L9 drag;
- exact native horizontal Frame delta compared with an unfolded control gesture;
- live-membership/identity Ctrl+Z and Ctrl+Y for single and multi drag;
- four nested parent-collapse/reopen cycles with click and right/add semantics;
- lower logical L20 realization/click at its folded row;
- no hidden-row visual leakage;
- direct display geometry/extent stays stable and non-reentrant;
- drag-time preview is checked at WPF Render priority, every audit drains, and no selected preview disappears;
- idle stability;
- detach restores native geometry/constraint and native drag/Undo;
- no Harmony reference and no Harmony assembly loaded.

The winning drag boundary is deliberate: native TimelineItemView processes the first bubbling MouseMove before C freezes direct Top/Height refresh. While dragging, native Frame/snap/history stay host-owned, C post-corrects only logical Layer, direct display writes are frozen, and selected views receive temporary RenderTransform compensation. A temporary `TimelineViewModel.Viewport.Value` height lease keeps selected native logical rows realized without changing the user-visible Y/extent; the exact Rect is restored on MouseUp.

Synthetic fixture creation is separated from actual gestures with one public UndoRedoManager.Record() call. No artificial Record/Clear calls are injected into gestures.

## Boundaries / NOT PROVEN

`PASS_TRACK_C_INPUT` is the core integrated input gate, not Full plugin acceptance.

Still pending:

- integrated real FileDrop under the C architecture;
- other native add routes beyond the right-click/add-position converter;
- automatic navigation / ScrollToItem-style routes;
- structural layer insert/delete/reorder and folder-range tracking;
- diverse item types, grouped/multi-layer items and third-party item implementations;
- production-scale performance;
- real folder UI, persistence/schema, packaging/installer and release compatibility.

Explicit viewport Reveal calls in the harness are navigation setup, not evidence that every host navigation route is already intercepted.
