# YMM4 no-Harmony Full — P4 Full Product / UX Freeze

作成日: 2026-09-23  
状態: **P4 COMPLETE / FROZEN**

P4は、P0-P3で凍結した no-Harmony core / structural tracking / persistence を、実際に使える Full 製品候補へ組み立てる工程だった。

P4 completion source:

`4672ffdd06f490a3ce4f9b6f21e51ad2278ae60c`

P4は以下のS0-S5 freezeを包含する:

- S0 — integration reconciliation;
- S1 — common commands / low-risk parity;
- S2 — color / visibility / schema v2;
- S3 — structural convenience / Group Control;
- S4 — management panel / explicit block move;
- S5 — timing bands / folded Group visual compatibility.

## 1. Frozen product ownership

P4完了時点の正本/所有権は次に固定する。

```text
FolderProductState
  ├─ Core FolderDocument
  ├─ FolderOptions(Color / Hidden)
  └─ VisibilityRestore
          │
          ▼
FolderCommands
  ├─ metadata edits
  ├─ structural convenience
  ├─ Group operations
  └─ panel block move
          │
          ├─────────────┐
          ▼             ▼
FolderRangeTracker   FoldMap / DirectDisplay
          │             │
          └──────┬──────┘
                 ▼
          HandsOnHostAccess
                 │
                 ▼
                YMM4
```

Read-only projections:

- Timeline folder overlay;
- management-panel `PanelProjection`;
- S5 `VisualSummaryRules` / item-area Adorner.

They do not own a second project model.

## 2. P4 feature completion

### S0 — integration reconciliation

Frozen:

- existing structural Add/Delete/Move history bridge connected to product state;
- shared P3 ToolState persistence authority;
- frozen FoldMap/input/display mechanisms retained;
- no duplicate structural owner.

Freeze:

`docs/YMM4_NO_HARMONY_S0_INTEGRATION_RECONCILIATION.md`

### S1 — common commands / low-risk LayerPatan parity

Frozen:

- reference-compatible folder creation target rules;
- gapped selection -> min..max;
- clicked outside selection -> clicked row;
- one-row creation with one inserted empty layer;
- one user-visible Undo;
- rename / collapse / expand-all / collapse-all / select-items;
- tag arrow and name hit-zone separation.

Freeze:

`docs/YMM4_NO_HARMONY_S1_COMMON_COMMANDS_FREEZE.md`

### S2 — visual metadata / visibility / v2 persistence

Frozen:

- outer schema v2 preserving P3 v1 Core semantics;
- v1 -> v2 migration;
- unsupported/malformed raw byte preservation;
- folder color;
- explicit folder-color -> YMM4 layer-color operation;
- folder Hidden separate from collapse;
- nested Hidden union;
- exact original visibility restore;
- external native eye override semantics;
- visibility-restore remap through the same structural mapping.

Freeze:

`docs/YMM4_NO_HARMONY_S2_VISUAL_METADATA_FREEZE.md`

### S3 — structural convenience / Group Control

Frozen:

- add layer at folder end / inside;
- target + ancestor-only expansion;
- folder + contained layer/item destructive delete;
- Group Control add;
- GroupRange warning / Fit;
- standard Add/Delete GroupRange auto-correction;
- plugin structural composite with one native history unit;
- S2 visibility composition through structural edits.

Freeze:

`docs/YMM4_NO_HARMONY_S3_STRUCTURAL_GROUP_FREEZE.md`

### S4 — management panel / explicit block move

Frozen:

- flat read-only-derived folder/layer list;
- F2 / Ctrl+G / Delete / arrow/Enter operations;
- Follow Timeline;
- CurrentFrame item summary;
- Group warning/lane display;
- color / visibility / item selection / Group / destructive-delete command parity;
- Before / Into / After drag/drop;
- same-parent contiguous block normalization;
- ancestor/descendant double-selection suppression;
- one layer map for folders, native items, LayerSettings, GroupRanges, visibility restore;
- one-step Undo/Redo.

Freeze:

`docs/YMM4_NO_HARMONY_S4_MANAGEMENT_PANEL_FREEZE.md`

### S5 — item-area visual parity

Frozen:

- hidden-item timing bands on collapsed owner row;
- folded Group compatibility background;
- public YMM4 `TimelineItemViewModel.Left / Width` for X geometry;
- existing FoldMap for Y geometry;
- zoom and horizontal-scroll alignment;
- plugin-owned Adorner; no summary-driven native item Top/Height mutation;
- no visual-summary work when no folded rows exist.

Freeze:

`docs/YMM4_NO_HARMONY_S5_VISUAL_PARITY_FREEZE.md`

## 3. P4 final acceptance at one source

Accepted source:

`4672ffdd06f490a3ce4f9b6f21e51ad2278ae60c`

All following workflows were GREEN from that source:

| Gate | Run | Result |
| --- | ---: | --- |
| S0 integration | `35808863846` | success |
| S1 integration | `35808863920` | success |
| S2 integration | `35808863996` | success |
| S3 integration | `35808863821` | success |
| S4 integration | `35808863894` | success |
| S5 pure visual summary | `35808863868` | 17/17 PASS |
| S5 coordinate discovery | `35808863785` | both pinned hosts PASS |
| S5 integration | `35808863883` | both pinned hosts PASS |
| hands-on candidate | `35808863922` | success |
| integrated Track C | `35808863980` | 134/134 on both hosts |
| Track C structural | `35808863872` | 353/353 on both hosts |
| real .ymme | `35808863869` | install/startup PASS |

Pinned hosts:

- YMM4 4.55.1.1 Lite;
- YMM4 4.56.1.0 Lite.

## 4. Real package acceptance

Accepted .ymme SHA256:

`d3090cad37d42ff3c3b3ba542dab79f92dfea74fbc67df2624449c3084c8af08`

- package artifact: `10728484404`;
- package digest:
  `sha256:9ac551b72a85d17bc6f40a36aad5622c749d7faedb919447282683072c2915cd`;
- evidence artifact: `10729206468`;
- evidence digest:
  `sha256:fd75f6e3f588d129df9875e97e9c0a2bcfb57fef392295afed13e00c1b6e36cc`;
- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`;
- no Harmony dependency.

## 5. Architecture constraints frozen at P4 exit

Do not reintroduce in P5+:

- Harmony;
- Tool-panel-specific FolderDocument;
- second persistence store;
- custom Undo stack;
- second structural observer for visibility or Group;
- independent display-row/layer mapper;
- per-item-type folder state;
- general event bus / resident worker;
- item-type-specific coordinate formulas when common public YMM4 item VM geometry works.

P5/P6 may add compatibility adapters only when a concrete item/plugin type requires one and the fallback is explicitly bounded.

## 6. P4 exit decision

The P4 Full feature set is **COMPLETE / FROZEN**.

Remaining work is no longer basic product feature construction:

- **P5** — Compatibility Coverage;
- **P6** — Hardening & Performance;
- **P7** — Release Gate.

P5 must validate representative native/special/third-party item classes against this frozen architecture rather than reopening S0-S5 design by default.
