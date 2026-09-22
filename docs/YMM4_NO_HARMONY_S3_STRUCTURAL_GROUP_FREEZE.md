# YMM4 no-Harmony Full — S3 Structural Convenience / Group Control Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

S3の目的は、S0-S2で固定した no-Harmony Core / FoldMap / persistence / visibility / Undo 所有権を維持したまま、LayerPatan参照機能のうち構造便利操作とGroup Control系を製品候補へ載せることだった。

このfreezeで、追加・削除・Group Controlを機能ごとの独自構造エンジンにはせず、

```text
pure StructuralConvenience plan
  -> YMM4 public Timeline.AddLayer/DeleteLayer
  -> GroupRange / visibility adjustment
  -> FolderProductState
  -> one native UndoRedoManager.Record()
```

へ収束した。

## 1. Accepted runtime / exact-host evidence

Accepted runtime source:

`fcc7f8f1b3cb603e507de14575e2ec880fc1f3b2`

### Pure structural rules

Run:

`35796862805`

Result:

- `PASS_S3_STRUCTURAL_RULES`
- **33/33 PASS**

Covered pure behavior:

- folder tail insert;
- insert inside folder;
- folder-head insert;
- target folder + ancestors only expand;
- later/disjoint folders shift through the frozen FolderRangeTracker mapping;
- destructive folder-content delete plan;
- GroupRange insert/delete adjustment;
- Group Control add plan;
- GroupRange issue detection;
- Fit suggestions;
- pre-host standard Add/Delete GroupRange correction;
- unsupported/outside-folder cases remain unchanged;
- resulting FolderDocument remains serializable.

### Dual-host product integration

Run:

`35796862740`

| Host | Job | Result | Candidate DLL SHA256 |
| --- | ---: | --- | --- |
| YMM4 4.56.1.0 Lite | `106978067977` | `PASS_S3_INTEGRATION` | `5a17e9c0ab36cdb93f2987991142f76b8b9c13ff1eb6abfd5a906fba5476316d` |
| YMM4 4.55.1.1 Lite | `106978068177` | `PASS_S3_INTEGRATION` | `c012a9ee3b93471139bdbb5f91bd2575147054320ad4ce2aa0e9449c447d640c` |

Both hosts proved:

- explicit insert into a hidden folder;
- newly inserted hidden row joins the same visibility-restore ownership;
- plugin-owned structural Add + FolderProductState + GroupRange + visibility is one Undo/Redo unit;
- standard YMM4 Add performs GroupRange auto-correction in the same YMM4-owned history unit;
- GroupRange Fit has native Undo/Redo;
- Group Control add has structural + item + folder-state Undo/Redo;
- destructive folder-content delete removes folder rows/items and restores/reapplies them with one Undo/Redo.

Evidence artifacts:

- 4.56.1.0: `10724496649`, digest `sha256:a0d477f00fd4d2bc7e1adc3316a16b7d874d8d6ad77f7c132f3fb6b4199a31f6`;
- 4.55.1.1: `10724156080`, digest `sha256:615fa60d35e56c9165e765bbbe353529dea18307e4ebd6e0d986fc2603727714`.

## 2. Product implementation frozen by S3

### 2.1 StructuralConvenienceRules

Host-independent rules own the explicit planning for:

- insert into a selected folder;
- insert at folder head / inside / tail;
- delete folder contents;
- Group Control head insertion;
- GroupRange issue detection;
- Fit suggestions;
- GroupRange correction for standard structural Add/Delete.

The rules return final FolderDocument, the structural layer mapping, and final GroupRanges. They do not know YMM4/WPF/Undo.

### 2.2 Host mutation remains YMM4-owned

Plugin-owned structural composites use public:

- `Timeline.AddLayer(int)`;
- `Timeline.DeleteLayer(int)`;
- `Timeline.TryAddItems(...)`;
- existing item/property Undo surfaces.

The product does **not** rebuild LayerSettings or remap every IItem by its own independent host algorithm.

Multiple public mutations belonging to one plugin command are left pending and committed by one final `UndoRedoManager.Record()`.

This exact-host gate proves that one Undo/Redo restores/reapplies the whole composite.

### 2.3 Common command surface

The existing S1 `FolderCommands` now also owns:

- add layer at folder end;
- add layer below a layer while remaining inside its folder;
- destructive folder + contained rows/items delete;
- add Group Control for a folder;
- GroupRange issue query;
- Fit GroupRanges.

Timeline context-menu actions call these same typed commands. The later S4 management panel must reuse them rather than create a second structural implementation.

### 2.4 Standard structural GroupRange auto-correction

The existing P1/S0 standard-command boundary remains authoritative.

For standard YMM4 Add/Delete:

1. PreviewExecuted resolves the already-supported StructuralEdit.
2. S3 pure rules calculate only necessary GroupRange changes.
3. GroupRange changes are applied before the host command completes.
4. FolderProductState is added to the same pending history unit.
5. the plugin still does not call a second Record().
6. YMM4's normal command owns the final Record boundary.

MoveUp/MoveDown do not invent GroupRange corrections.

### 2.5 Visibility composition

S3 does not create a second visibility observer.

Explicit structural commands reuse the S2 visibility ownership:

- visibility-restore keys are remapped through the same layer map;
- a newly inserted row inside a Hidden folder captures its native visibility once;
- that row is hidden before the composite Record;
- structural Undo removes its restore ownership;
- structural Redo restores it.

The integration gate explicitly proves this composition.

## 3. UI parity added in S3

Timeline menu now exposes:

- add layer at the innermost folder tail;
- add layer below the clicked row while staying inside the folder;
- folder-tail add in the folder submenu;
- add Group Control;
- Fit GroupRange with issue count;
- destructive folder+layer delete with layer/item-count confirmation.

Ungroup remains metadata-only and is distinct from destructive delete.

## 4. Important boundaries retained

S3 does not add:

- Harmony;
- a second FolderDocument / FolderProductState;
- a second persistence store;
- a plugin-owned Undo stack;
- a second FoldMap;
- a second structural observer;
- per-feature reflection;
- a general transaction engine.

`StructuralDeltaDetector` remains the conservative classifier for future host routes that cannot enter the already-proven standard-command boundary. S3 does not guess support for such routes.

## 5. Exact-host finding about empty rows

During S3 acceptance, `Timeline.MaxLayer` was found not to be a reliable measure of pure empty-layer structure because it is affected by item placement.

The acceptance gate therefore distinguishes:

- `Timeline.MaxLayer`: item-derived timeline maximum;
- `Timeline.LayerSettings.MaxLayer`: explicit layer-setting / empty-row structure.

S3 validates explicit structural row Add/Delete with `LayerSettings.MaxLayer`, while separately checking GroupItem placement. This is a test-model correction, not a product workaround.

## 6. Regression / package state

Accepted source `fcc7f8f1...` also passed:

- S0 integration run `35796862771` — success;
- S1 integration run `35796862851` — success;
- S2 integration run `35796862734` — success;
- hands-on candidate run `35796862806` — success;
- real `.ymme` run `35796862738` — success.

Real `.ymme` evidence:

- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`;
- candidate / installed DLL SHA256:
  `5a17e9c0ab36cdb93f2987991142f76b8b9c13ff1eb6abfd5a906fba5476316d`;
- `.ymme` SHA256:
  `7520ee183e48341c795b0f1f7eadbbb50880e04f8075c07ce7cbd9ccd0ffbdb5`;
- installed Harmony files: **0**.

## 7. S3 exit decision

S3 is **COMPLETE / FROZEN**.

The active P4 path advances to:

**S4 — management panel parity**

S4 should remain a View/projection over the same FolderSession / FolderCommands / FoldMap boundaries and must not reopen S3 structural ownership merely to implement panel drag/drop or keyboard UX.
