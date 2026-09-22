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
