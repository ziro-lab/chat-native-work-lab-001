# YMM4 no-Harmony Full — S0 Integration Reconciliation Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

S0の目的は、新しい便利機能を追加する前に、P0-P3で実証済みの機構とP4.5のインストール可能候補を突き合わせ、**LabでGREENだっただけの部品と、実際に製品候補へ接続済みの部品を一致させること**だった。

このfreezeはCoreの再設計ではない。P0-P3の意味・所有権を維持し、現在サポートする標準経路に必要な配線だけを製品候補へ戻した。

## 1. Freeze source / exact-host evidence

Accepted source:

`a15f89fce2f41604c7c09e15b76be04dc4c7a93b`

Dual-host S0 run:

`35751457014`

| Host | Job | Result | Candidate DLL SHA256 |
| --- | ---: | --- | --- |
| YMM4 4.56.1.0 Lite | `106826372470` | `PASS_S0_INTEGRATION` | `7c6c0e216f8c46841e337648420a4433f6741c98b0266c01cbcffc90c175239b` |
| YMM4 4.55.1.1 Lite | `106826372761` | `PASS_S0_INTEGRATION` | `53ddcca39c87b48367b9c88f35f4d3d4155fe0256b1151e3b7624317e6b25438` |

Both hosts produced the same structural facts:

- resource-free baseline `LayerSettings=0`;
- standard Add Layer at L2 -> `LayerSettings=1`;
- persisted folder L1..L2 -> L1..L3;
- exactly one pending plugin folder history command;
- one host Undo -> Timeline structure and folder range both return to baseline;
- exactly one folder Undo callback;
- one host Redo -> Timeline structure and folder range both reapply;
- exactly one folder Redo callback;
- folded input adapter attached;
- FileDrop adapter attached;
- no Harmony dependency is introduced.

Evidence artifacts:

- 4.56.1.0: artifact `10706285226`, digest `sha256:8d7c6affe0c5454e75f74218cc4fdcc004aa3f4ad98d546fbe7d2bb286a50887`;
- 4.55.1.1: artifact `10706280208`, digest `sha256:0c572216b08ee1150cd9f6be5e0ad611d692a89a0c20be498585507376ad635d`.

## 2. Product candidate wiring after S0

### Structural folder synchronization

The installable candidate now owns a product-side `StructuralFolderBridge` for the already-supported YMM4 standard structural commands:

- Add Layer;
- Delete Layer;
- Move Up Layer;
- Move Down Layer.

The bridge:

1. observes the validated standard command at `PreviewExecuted`;
2. converts it to the frozen `StructuralEdit`;
3. runs the frozen `FolderRangeTracker`;
4. updates the active Timeline's persisted `FolderDocument`;
5. adds one `UndoRedoActionCommand`;
6. deliberately does **not** call `UndoRedoManager.Record()`;
7. lets the YMM4 standard command own the user-visible history boundary.

This preserves the P1.4/P1.5 same-transaction rule. A standard layer edit remains one user action, not a native action plus a second plugin Undo unit.

### Folded interaction routes

The installable candidate now attaches the already-proven Track C mechanisms for:

- right-click display-coordinate -> logical-layer mapping;
- native item drag correction with the existing gesture/display safeguards;
- real FileDrop mapping and post-correction.

All of these continue to use the same `FolderLayout/FoldMap` authority as the folded display. S0 does not introduce an independent hidden-row formula.

### Persistence / runtime state

P3's runtime state and persistence path was already present in the installable candidate through the shared `FolderStateStore` / ToolState persistence path. S0 keeps that state as the single folder-document authority and connects structural history back into it rather than creating a second runtime model.

## 3. Deliberate non-connections

### P2 automatic selection navigation

The P2 `SelectionNavigationBridge` is **not** blindly wired into the current product candidate.

P2 proved the host/navigation mechanism, but its freeze record explicitly left **collapse auto-expansion UX and user-visible history policy** to P3/P4. The P2 probe treated the reveal as view-state behavior, while the current P4 candidate persists `IsCollapsed` in `FolderDocument`.

Directly connecting the probe would therefore silently answer an unresolved product question:

> when host navigation reveals a hidden destination, is that temporary view state, a persisted folder edit, or an Undo-visible operation?

S0 records this as an intentional policy boundary, not an integration omission. Product-owned Follow Timeline / navigation policy is resolved with the P4 UX work, primarily S4, before this mechanism is connected.

### StructuralDeltaDetector

`StructuralDeltaDetector` remains the frozen host-delta classifier for routes that require post-hoc structural classification. It is not installed as a second structural owner beside the current standard-command bridge.

For the currently supported standard Add/Delete/Move commands, the P1.4/P1.5 same-transaction proof requires the plugin folder command to enter the host's pending history **before** YMM4 performs its own `Record()`. The proven product route therefore uses the standard command identity plus `FolderRangeTracker` at `PreviewExecuted`.

This does not permit guessing new structural routes. A future structural mutation that cannot enter this proven standard-command boundary must be classified through the frozen detector / Ambiguous policy before it is treated as supported.

## 4. Architecture boundary retained

S0 does **not** add:

- a second FolderDocument;
- a second persistence path;
- a plugin-owned Undo stack;
- a new structural policy engine;
- a second FoldMap;
- Harmony;
- a feature-specific reflection island.

The candidate still reuses some frozen Lab adapter source files directly. Their final class/file names are not a product contract. Later convergence may move them behind `YmmHostAccess`, `PlacementRouter` and `GestureLease`, but must preserve the exact behavior proven here.

## 5. S0 exit decision

S0 is complete.

The integration gap identified before convenience work is now classified as:

- structural standard-command synchronization — **connected and dual-host GREEN**;
- folded input/native drag mapping — **connected**;
- real FileDrop mapping — **connected**;
- P3 runtime/persistence state — **already connected and retained**;
- P2 automatic reveal — **intentionally deferred until its UX/history semantics are chosen**;
- StructuralDeltaDetector — **retained as the classifier boundary for routes that actually require delta classification, not duplicated beside the standard command owner**.

The next active implementation step is **S1 — common command surface + low-risk LayerPatan parity**.
