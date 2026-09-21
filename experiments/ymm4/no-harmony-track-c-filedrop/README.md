# Track C integrated FileDrop

Prerequisite: Track C core input/display PASS in #72, source `722f6f487c6f573fa6bb87cabb9d1941c94bb65f`, run `35662155334`, 107/107 on both pinned hosts.

This gate ports the already-proven no-Harmony real FileDrop route onto the accepted direct folded display / FolderLayout architecture.

## Route

The adapter observes real WPF PreviewDragEnter/Over/Drop. It maps the displayed row through the same `DirectDisplay.Layout`, updates the YMM4 Timeline cursor to the corresponding logical row, and executes YMM4's configured `CommandType.AddFileItem` command. The host remains the actual file-add implementation.

After the command, a ContextIdle finalizer only corrects newly added Item.Layer if the host route did not already honor the logical cursor. This mirrors the earlier isolated FileDrop proof and is acceptance-tested with real Ctrl+Z/Ctrl+Y.

## Acceptance

Two real OS-level drag/drop operations target logical L9:

1. layout A: collapsed [2..3] + [6..8], L9 displayed at row 6;
2. nested layout B: parent [1..5], child [2..3], plus [6..8], L9 displayed at row 3.

Both must prove enter/over/drop route observation, host AddFileItem execution, one added item, final L9 model layer, folded visual row, direct-display geometry, Undo removal, Redo restoration at L9, no hidden-row visual leakage, stable idle/detach and no Harmony.

This is still not Full product acceptance. Other add commands, navigation, structural edits, item diversity, UI/persistence/package/performance remain separate gates.
