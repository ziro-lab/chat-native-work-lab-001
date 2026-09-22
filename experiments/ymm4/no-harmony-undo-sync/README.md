# P1.4 — native Undo synchronization

Authority:

- P1.2 FolderRangeTracker: frozen pure positional folder semantics.
- P1.3 structural observer: frozen shared structural-event boundary on both pinned hosts.

## Question

Can plugin-owned folder state participate in the **same user-visible Undo transaction** as a standard YMM4 Add/Delete layer command without replacing YMM4's command implementation and without calling a second `Record()`?

## Candidate

One handled-events-too `PreviewExecuted` observer sees the already-validated standard structural command before its YMM4 handler finishes.

For a structural edit that changes folder state:

1. compute the next folder state through the frozen `FolderRangeTracker`;
2. update plugin-owned folder state;
3. call public `UndoRedoManager.AddCommand(new UndoRedoActionCommand(...))`;
4. **do not call `UndoRedoManager.Record()`**;
5. allow YMM4's standard command to continue and own its normal transaction boundary.

Hypothesis: YMM4's own standard command `Record()` will commit both the native layer mutation and the already-pending plugin folder command into one user Undo unit.

## Strict native gate

Nested folder fixture:

- A = L2..L8
- B = L4..L6

Standard command cases:

- Add L5 -> A 2..9, B 4..7;
- Delete L5 -> A 2..7, B 4..5;
- MoveDown L5 -> positional folder ranges unchanged.

For every case:

- standard RoutedCommand executes;
- exactly one Recorded boundary is observed;
- one Ctrl+Z restores both Timeline and Folder state;
- one Ctrl+Y restores both;
- final one Ctrl+Z restores the common baseline;
- plugin does not call Record during the structural operation.

For MoveDown, FolderRangeTracker produces no folder-state change, so no plugin Undo command is added; native Undo/Redo must still remain correct.

This probe is mechanism evidence only. Persistence and Track C display integration remain P1.5+ work.
