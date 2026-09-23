# YMM4 no-Harmony Full — S4 Management Panel / Explicit Block Move Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

S4の目的は、S0-S3で固定した FolderProductState / FolderCommands / FoldMap / native Undo 所有権を維持したまま、管理パネルと明示的な複数レイヤー移動を製品候補へ載せることだった。

このfreezeで、管理パネルは第二のタイムラインエンジンではなく、

```text
FolderProductState + live Timeline
  -> pure PanelProjection
  -> flat ListBox rows
  -> existing FolderCommands
```

へ収束した。

Before / Into / After のドラッグ移動も、

```text
panel selection
  -> pure PanelDragBlock / PanelDropTarget
  -> pure PanelMovePlan
  -> one host block permutation
  -> visibility restore remap
  -> one native UndoRedoManager.Record()
```

として固定する。

## 1. Accepted runtime / exact-host evidence

Accepted source:

`45aa3e13c7979e7b663835fd7c83065cdc4ee93a`

PR:

- #113 `s4: add management panel parity and block move`

### Pure panel / move rules

Run:

`35806164327`

Result:

- `PASS_S4_PANEL_PURE`
- **40/40 PASS**

Covered pure behavior:

- flat folder/layer projection from FolderProductState;
- parent/depth derivation from range containment only;
- collapsed folder descendant suppression;
- one trailing empty layer row;
- item-count and GroupRange-warning projection;
- selected parent folder suppresses descendant rows from the drag set;
- only contiguous same-parent top-level rows become one drag block;
- Before / Into / After normalization;
- same-boundary no-op rejection;
- self / descendant Into rejection;
- explicit folder move out of its parent;
- parent + descendant move into another folder;
- moving folder IDs and descendant relationships preserved;
- one MapLayer reused for visibility-restore remap;
- GroupRange move calculation;
- resulting FolderDocument serialization.

### Dual-host product integration

Run:

`35806164338`

| Host | Job | Result | Candidate DLL SHA256 |
| --- | ---: | --- | --- |
| YMM4 4.55.1.1 Lite | `107007391159` | `PASS_S4_INTEGRATION` | `8800a55441eded5f0ff23d0becfd0a81c439af8d38583afb835f5288b0d62097` |
| YMM4 4.56.1.0 Lite | `107007391301` | `PASS_S4_INTEGRATION` | `51be367ebc9077e3bc6a937e698a47a38fdc123b1d528f2648d25e1e61482b8d` |

Both hosts proved:

- management-panel surface can be constructed without extra XAML/resource dependency;
- representative two-layer / nested-folder block move;
- folder parentage changes exactly as the pure plan predicts;
- native item Layer values follow the same move map;
- LayerSettings follow the same move map;
- visibility-restore keys follow the same move map;
- moved Hidden rows remain hidden;
- explicit layer count remains unchanged by the permutation;
- one Undo restores host state + FolderProductState together;
- one Redo reapplies the same state;
- fold-aware panel follow scroll can use the existing DirectDisplay/FoldMap.

Evidence artifacts:

- 4.55.1.1: artifact `10727607684`, digest `sha256:91c9191377c2ed93edbd5773bbde87abaf72d17a7fd1c573d9be16b21dfc88bd`;
- 4.56.1.0: artifact `10727333074`, digest `sha256:b5ea14d50b7f29edf04107bf23bccf6707213241eae04b384a2a4547d558d7a3`.

## 2. Management-panel product surface frozen by S4

The panel is a flat ListBox projection. It does not persist another tree or another folder document.

Implemented panel functionality:

- folder/layer flat list with depth indentation;
- collapsed descendants hidden from the panel projection;
- inline folder rename;
- F2 rename;
- Ctrl+G folder creation;
- Delete for ungroup / layer deletion;
- Left / Right / Enter folder open/close;
- expand all / collapse all;
- Follow Timeline;
- current item at CurrentFrame;
- Group warning text;
- Group lane glyphs;
- folder color selection;
- explicit folder-color -> YMM4 layer-color apply;
- folder / layer visibility actions;
- select items in a folder/layer;
- add layer;
- add Group Control;
- Fit GroupRange;
- destructive folder-content delete;
- extended row selection;
- Before / Into / After drag/drop.

Editing text remains panel-local view state. Row selection/editing state is keyed by stable row identity and is not another project model.

## 3. Follow Timeline boundary

S4 does not wire the old P2 SelectionNavigationBridge as-is because that candidate assumed collapse reveal was view-only, while the current product stores IsCollapsed in project state.

Panel-owned Follow Timeline therefore uses the existing DirectDisplay/FoldMap to scroll to visible rows without changing native selection or CurrentFrame.

The product still does not claim to intercept unsupported bare host `ScrollToItem` calls that bypass a supported selection/product-owned navigation route.

## 4. Explicit block move boundary

The move planner is host-independent and owns only:

- selected logical block;
- destination boundary;
- Into folder identity;
- folder range/result calculation;
- old -> new layer map;
- final GroupRanges.

The host boundary applies that same layer permutation to native layer-bearing state and performs one final native history record.

Frozen constraints:

- same-parent contiguous top-level rows only;
- selected ancestor removes selected descendants from the moving set;
- self / descendant Into is rejected;
- no-op same-boundary drop is rejected;
- moving folder IDs are preserved;
- descendants move with their selected ancestor;
- LayerSettings / items / visibility-restore use the same map;
- no repeated MoveUp/MoveDown loop;
- no automatic structural observer double-application;
- no second Undo stack.

## 5. Architecture result

S4 retains the existing ownership:

- FolderProductState remains the only plugin project state;
- PanelProjection is read-only derived state;
- FolderCommands remains the shared edit surface;
- DirectDisplay/FoldMap remains the logical/display mapping authority;
- S2 visibility restore reuses the block move map;
- native UndoRedoManager remains the user-visible history owner;
- no Harmony;
- no second persistence store;
- no Tool-panel-specific FolderDocument;
- no second structural observer.

## 6. Regression / real package

Accepted source also passed:

- S0 integration run `35806164344` — success;
- S1 integration run `35806164222` — success;
- S2 integration run `35806164236` — success;
- S3 integration run `35806164278` — success;
- hands-on candidate run `35806164205` — success;
- real .ymme run `35806164253` — success.

Real .ymme:

- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`;
- .ymme SHA256:
  `9a724819f0edd532204ab07e7c6384261404608247adaba163850b16f27010bb`;
- package artifact `10727739101`, digest
  `sha256:83cacdddf1e7f1949f552ae86a26e59ec74f9a072d4029f0fbc2a8a1943feea3`;
- evidence artifact `10727559268`, digest
  `sha256:3876bc6c161378ac387b2d11b1a169cb4e38c54501a375cc7ff98b7f72ab6b82`.

## 7. Explicitly deferred to S5

S4 does not claim visual parity for the item-area time axis.

Still deferred:

- collapsed-owner-row inner item timing bands;
- folded Group/background compatibility in the item area;
- final Timeline visual polish / visual acceptance;
- any correction of native Group background geometry that requires new host-sensitive observation.

These are S5 visual concerns and must not reopen S4 state/command/move ownership merely to draw them.

## 8. S4 exit decision

S4 is **COMPLETE / FROZEN**.

The active P4 path advances to:

**S5 — timing bands / Group visual compatibility / final P4 acceptance.**
