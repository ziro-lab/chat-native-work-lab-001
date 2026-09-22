# YMM4 no-Harmony Full — P1 Architecture Convergence Review v0.1

Status: **COMPLETE / FROZEN**

Reviewed source line: P0 Track C through P1.5c, final integrated source `05237928242f6a182a74b7e63090512dd4693212`.

## Decision

**No broad P1 Lab refactor is required before freeze.**

The current Lab is intentionally split by evidence purpose. The product should not copy that file/class topology, but the verified mechanisms already have clean enough ownership boundaries to extract into a simpler product structure without changing the algorithms that were required for correctness.

The correct simplification strategy is therefore:

> preserve proven mechanisms, centralize ownership, and merge duplicated entry paths during product extraction.

Do not simplify by deleting the drag/display/Undo safeguards that exact-host testing proved necessary.

## 1. Current evidence boundaries

| Responsibility | Frozen evidence implementation | Product ownership |
| --- | --- | --- |
| logical folder transform | `FolderRangeTracker` | `FolderRangeTracker` under `FolderDocument` |
| host structural delta classification | `StructuralDeltaDetector` | structural observer feeding tracker |
| logical <-> display mapping | `FolderLayout` | `FoldMap` |
| idle folded geometry / extent | `DirectDisplay` | `FoldDisplay` |
| native drag mapping | `InputMapAdapter` + `DirectDisplay` gesture lifecycle | `InteractionRouter.GestureLease` |
| right-click placement mapping | `InputMapAdapter` -> `display.Layout.MapDisplayPointToLogical` | shared `PlacementRouter` |
| real FileDrop placement mapping | `FileDropMapAdapter` -> same `display.Layout.MapDisplayPointToLogical` | same `PlacementRouter` + native action |
| same-transaction folder history | `StructuralFolderBridge` using `UndoRedoManager.AddCommand` | structural synchronization service |
| version-sensitive host access | Lab `Host`, bounded reflection in display/adapter probes | `YmmHostAccess` |
| persistence | not part of P1 | P3 `Persistence` <-> `FolderDocument` |

## 2. FoldMap authority — GREEN

The current input paths do **not** maintain independent coordinate formulas.

Both the right-click path and the FileDrop path call:

`display.Layout.MapDisplayPointToLogical(...)`

Drag correction and display geometry also use the same `FolderLayout` logical/visible-row functions.

Product rule:

- `FoldMap` is the only owner of logical Layer <-> visible Row conversion;
- no placement/navigation/gesture component may recreate hidden-row arithmetic.

This satisfies the “one mapping authority” convergence rule.

## 3. Placement convergence — GREEN, product extraction required

The Lab currently has separate right-click and FileDrop adapters because they were isolated proofs. Their common mechanism is already visible:

```text
display point
  -> FoldMap.MapDisplayPointToLogical
  -> set native cursor/context position
  -> execute/allow YMM4 native action
  -> optional post-correction when the host action materializes an item
```

Product target:

```text
PlacementRouter
  MapDisplayPoint()
  SetNativeCursor()
  ExecuteNativeAction()
  PostCorrectIfNeeded()
```

Right-click, FileDrop and future add routes should enter this shared boundary.

**Decision:** do not refactor the frozen Lab adapters merely to rename them. Their common mapping authority is already proven; perform the entry-path convergence when extracting the product runtime.

## 4. YmmHostAccess boundary — DEFINED / GREEN

Version-sensitive YMM4 knowledge currently appears in the Lab `Host` helper and a small amount of bounded reflection inside display/fixture adapters.

The product `YmmHostAccess` boundary must own:

- visible Timeline / TimelineViewModel binding;
- reactive Timeline cursor get/set;
- Viewport get/set and lease/restore;
- item/label/line geometry Top/Height access;
- FastCanvas refresh route where required;
- native standard command lookup/execution;
- UndoRedoManager access;
- bounded version-sensitive member discovery.

Outside `YmmHostAccess`, product code should not know private member names or reflection details.

FileDrop test-only FilePath inspection is fixture/evidence code and is **not** a required runtime host-access API.

## 5. Gesture complexity — ENCAPSULATE, DO NOT REMOVE

Track C already exposes a usable lifecycle:

- `BeginGesture(items)`;
- `UpdateGestureVisuals()`;
- `EndGesture()`.

Internally, exact-host evidence proved the need for:

- freezing direct geometry writes during native drag;
- temporary Viewport expansion/lease;
- native ownership of Frame/snap/history;
- Layer post-correction;
- RenderTransform visual compensation;
- deterministic transform/Viewport restore.

Product target:

```csharp
using var gesture = display.BeginNativeGesture(items);
gesture.Update();
```

or an equivalent `GestureLease`.

The public surface can be simpler; the internal protections must remain.

## 6. Lab instrumentation — SEPARABLE / GREEN

The following are evidence mechanisms, not product requirements:

- mutation counters;
- application/reentry counters;
- render-audit counters;
- missing-view audits;
- write budgets;
- verbose geometry logs;
- fixture identities;
- artifact manifest/evidence output.

The product may retain lightweight diagnostics if independently useful, but these counters do not belong to the core runtime architecture.

Removing them during product extraction does not remove the behavior they proved.

## 7. Structural ownership — GREEN

P1 now separates three concerns:

```text
host observation
  -> StructuralDeltaDetector
  -> StructuralEdit
  -> FolderRangeTracker
  -> FolderDocument state
  -> same native Undo transaction
```

Important ownership rules:

- `StructuralDeltaDetector` classifies host deltas; it does not contain folder policy.
- `FolderRangeTracker` transforms logical folder spans; it does not know YMM4/WPF.
- the RoutedCommand layer position is only a generic disambiguation hint when state evidence is insufficient.
- `UndoRedoManager.AddCommand` synchronizes plugin-owned state with the host transaction; the plugin does not replace the host command or create a second `Record()`.

No Add/Delete/Move-specific folder adapter is required.

## 8. What must remain complex

Do not simplify away:

- FoldMap ownership of all row conversion;
- native command ownership where a stable host route exists;
- state-delta structural observation + conservative Ambiguous fallback;
- generic command-position hint for otherwise-underdetermined sparse deletes;
- same-transaction folder Undo command;
- drag-time geometry freeze;
- Viewport lease;
- RenderTransform gesture compensation;
- Layer post-correction;
- deterministic detach/restore.

Each exists because a simpler alternative either failed exact-host testing or could not uniquely preserve native semantics.

## 9. What should become simpler in the product

Converge these Lab separations:

- right-click/FileDrop/future add mapping -> `PlacementRouter`;
- scattered reflection/private-member knowledge -> `YmmHostAccess`;
- Begin/Update/End drag protocol -> `GestureLease`;
- proof-only state holders -> `FolderDocument`;
- evidence counters/logging -> tests/diagnostics only.

Keep `FolderRangeTracker`, `StructuralDeltaDetector` and `FoldMap` small and independently testable.

## 10. P1.6 checklist

- [x] FolderRangeTracker is host/WPF-independent.
- [x] StructuralDeltaDetector is host/WPF-independent.
- [x] FoldMap/FolderLayout is the single coordinate authority.
- [x] right-click/FileDrop can converge behind one PlacementRouter.
- [x] YMM4 version-sensitive access has a defined YmmHostAccess boundary.
- [x] native drag has a clear GestureLease extraction boundary.
- [x] Lab-only instrumentation is separable.
- [x] P1 components do not duplicate folder policy or Undo ownership.
- [x] no broad convergence refactor is needed inside frozen Lab evidence.

## 11. Why no refactor is performed before P1 freeze

The P1 integrated suite is now green at **353/353 on both pinned hosts**.

A broad Lab refactor at this point would:

- change evidence code that already proves the required behavior;
- consume more native Actions without improving the behavioral claim;
- risk conflating “cleaner product topology” with “different host mechanism”.

The product topology can be simpler without rewriting the proof topology.

Therefore P1 freezes the mechanisms and ownership rules here. P2 may add additional host interaction routes through the same target boundaries.

## 12. P2 handoff

P2 should evaluate each new host interaction by asking, in order:

1. Is it native-safe without adaptation?
2. If it uses a display coordinate, can it enter the shared Placement/Navigation boundary and FoldMap?
3. Does it need an existing GestureLease behavior?
4. Does it require new YmmHostAccess surface?
5. Is it truly a new mechanism, or only a new route into an existing mechanism?

Do not create a new product adapter solely because a new Lab probe exists.
