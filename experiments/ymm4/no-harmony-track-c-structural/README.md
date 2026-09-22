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


## P1.5b result

Source `0cfc6026dd3011f35a1858ad40d1ca13d422adea`, run `35694253060`.

Both pinned hosts passed `PASS_TRACK_C_STRUCTURAL` with **156/156** assertions.

After a real standard Add L3 structural mutation:

- folded native drag maps the C owner from logical L7 to L10;
- native Frame delta remains +35 on both hosts;
- drag Undo/Redo/reset is exact and Folder state remains unchanged;
- real PNG FileDrop maps to logical L10;
- native AddFileItem executes and the added item is post-corrected to L10;
- FileDrop Undo/Redo/reset is exact;
- Folder state stays unchanged;
- geometry, extent, no-reentry and no-hidden-view invariants remain green.

Artifacts:

- 4.55.1.1: `10680220486`, SHA256 `f9436ddfd2f135b033d86cc57fc1bc2117725b6829b699bc9055265fbdfbc826`;
- 4.56.1.0: `10680200520`, SHA256 `574e2b7e497bd4b6b6d15808156953d28b49af7db41aa8efd2922042ea36a810`.

## P1.5c — Full structural acceptance

The final P1.5 gate adds the remaining roadmap exit coverage:

- standard MoveUp;
- Add exactly at an outer folder owner boundary;
- Delete an outer owner with nested-head normalization;
- a repeated four-operation sequence:
  - Add owner;
  - Delete inserted layer;
  - MoveUp;
  - inverse MoveDown;
- four exact Ctrl+Z traversals;
- four exact Ctrl+Y traversals;
- folded geometry/extent/no-hidden-view checks at every history state;
- native click/right interaction after the entire history traversal;
- no Harmony.
