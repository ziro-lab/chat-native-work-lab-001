# P1.4a — Structural Undo synchronization feasibility

Authority:

- P1.1 standard YMM4 structural commands are one native user history operation.
- P1.2 FolderRangeTracker is frozen host-independent.
- P1.3 shared structural observation is frozen and can identify dense Add/Delete/Swap plus sparse empty-row Delete using one generic position hint.

## Question

Can plugin-owned folder state participate in the **same user-visible Undo record** as a standard YMM4 structural command without replacing the host command?

## Candidate mechanism

One generic WPF structural history bridge:

1. `PreviewExecuted` sees a recognized standard structural RoutedCommand.
2. It captures Folder state + host snapshot and calls public
   `UndoRedoManager.AddCommand(new UndoRedoActionCommand(...))`.
3. The standard YMM4 command continues unchanged and owns its native mutation / Record.
4. After native execution, the frozen P1.3 detector derives the actual structural edit.
5. Only if that evidence is exact does FolderRangeTracker update the Folder state and arm the already-added Undo command.
6. Ctrl+Z/Ctrl+Y exercise the host record normally.

The command delegates close over the transaction object, so they may be added before YMM4's Record while remaining inert until post-execution evidence arms them.

If native execution does not match the observed structural edit, the Folder transaction remains unarmed/no-op rather than guessing.

## Native gate

Initial folder state:

- A = L2..L6
- C = L20..L30

The probe exercises standard:

- Add L3 -> A 2..7, C 21..31
- Delete L3 -> A 2..5, C 19..29
- Delete completely empty L24 -> A unchanged, C 20..29

For every case:

- Preview must happen before the standard manager Recorded boundary;
- post-execution detector must be exact;
- one Ctrl+Z restores both Timeline and Folder state;
- one Ctrl+Y restores both;
- one final Ctrl+Z restores baseline;
- the Folder Undo/Redo action must run exactly once per user history traversal.

No Harmony. No standard command replacement. No custom user-visible undo stack.
