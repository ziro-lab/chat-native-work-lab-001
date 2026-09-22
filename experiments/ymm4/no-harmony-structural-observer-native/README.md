# P1.3b — exact-host structural observer

This is the exact-host bridge from YMM4 state/events into the pure P1.3a StructuralDeltaDetector.

## Product-boundary candidate

The host side takes only snapshots:

- stable item identity -> logical Layer;
- LayerSettings immutable-list snapshot;
- layer selection;
- UndoRedoManager Recorded / Undoed / Redoed timing events.

It derives generic pairs/hints, then delegates structural classification to the pure detector.

There is no AddLayerAdapter / DeleteLayerAdapter / MoveLayerAdapter folder logic.

The command-specific code in this probe exists only to *generate known host actions for the test harness*.

## False-positive guard

Insert/Delete-like item movement is accepted as structural only when LayerSettings was also replaced.

This prevents a coordinated multi-item vertical move from being interpreted as an inserted/deleted layer.

Exact adjacent swap remains classifiable because the frozen P1.2 positional folder policy does not mutate folder ranges for swap.

## Gate

On both YMM4 4.55.1.1 and 4.56.1.0 the probe must prove:

- standard Add L3 -> exact Insert(3,1);
- Ctrl+Z -> exact Delete(3,1) and baseline restore;
- standard Delete L3 -> exact Delete(3,1);
- Ctrl+Z -> exact Insert(3,1) and baseline restore;
- MoveDown L3 -> exact Swap(3);
- Ctrl+Z -> exact Swap(3);
- MoveUp L4 -> exact Swap(3);
- Ctrl+Z -> exact Swap(3);
- UndoRedoManager Recorded/Undoed events observed;
- insertion-like coordinated item move with unchanged LayerSettings is rejected as Ambiguous;
- no Harmony.

P1.3b does not yet mutate FolderDocument or add folder state to the Undo stack. That is P1.4.
