# P1.5a — Track C structural integration

This gate composes the already-green pieces without rewriting them:

- Track C `DirectDisplay`;
- Track C `InputMapAdapter`;
- P1.2 `FolderRangeTracker`;
- P1.3 `StructuralDeltaDetector`;
- P1.4 same-transaction Folder Undo bridge.

The Track C display/input source files are linked from the frozen experiment rather than copied.

## Folder fixture

All three ranges are collapsed:

- A = L1..L5
- B = L2..L4 (nested inside A)
- C = L6..L8

The visible interaction target is the C owner row.

## Structural cases

- Add L3
  - predicted: Insert(3,1)
  - Folder: A 1..6, B 2..5, C 7..9
- Delete L3
  - predicted: Delete(3,1)
  - Folder: A 1..4, B 2..3, C 5..7
- MoveDown L3
  - predicted: Swap(3)
  - positional Folder ranges unchanged

For every case the gate checks:

- actual post-host state is classified by the frozen StructuralDeltaDetector;
- predicted edit equals detected edit;
- one native Record boundary;
- one Ctrl+Z / Ctrl+Y keeps Timeline + Folder state synchronized;
- folded geometry and extent remain correct after forward / Undo / Redo / reset;
- hidden logical rows do not leak realized item views;
- click and right-click mapping remain native-correct after the structural mutation and after reset.

This is P1.5a only. Native item drag and real FileDrop after structural mutation are intentionally deferred to P1.5b.


## P1.5a result

Source `d2ca75c53c92feb8edf2e6b10e1897b9e6186d5f`, run `35693945595`.

Both pinned hosts passed `PASS_TRACK_C_STRUCTURAL` with **108/108** assertions.

The strict structural history sequence deliberately runs before native click/right mapping because the host records that later interaction as an additional history boundary. This keeps the claim precise: one Ctrl+Z immediately after a structural mutation restores Timeline + Folder state together.

## P1.5b — drag + real FileDrop after structural mutation

P1.5b keeps the proven Add L3 state active:

- folders = A1..6 / B2..5 / C7..9;
- original L6 marker becomes the visible C owner at L7;
- original L9 marker becomes visible L10.

Then it proves:

- native drag from folded visible L7 one display row down maps to logical L10;
- drag Frame remains native-owned and changes horizontally;
- drag Undo/Redo/reset are exact while folder state stays unchanged;
- real PNG FileDrop onto folded visible L10 executes native AddFileItem;
- post-correction places the new item on logical L10;
- FileDrop Undo/Redo/reset works while folder state stays unchanged;
- folded geometry/extent/no-hidden-view invariants remain green throughout.
