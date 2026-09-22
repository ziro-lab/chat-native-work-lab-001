# YMM4 no-Harmony Full — S1 Common Commands / Low-Risk Parity Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

S1の目的は、P4.5/S0で成立したインストール可能候補に、LayerPatan固定基準
`aa58e7e772deb829853742afc65e42036cf09d47` の低リスクな便利機能を追加しつつ、
UIごとに別実装を増やさず、今後の管理パネルも再利用できる共通コマンド境界を作ることだった。

## 1. Accepted runtime source

Runtime source:

`6ffaefd24271c9596054faf77a8b72a6a43ec011`

S1 pure run:

- run `35753664272`
- job `106833915443`
- result: **PASS_S1_PURE**
- assertions: **27/27 PASS**

Dual-host S1 integration run:

`35753664211`

| Host | Job | Result | Candidate DLL SHA256 |
| --- | ---: | --- | --- |
| YMM4 4.56.1.0 Lite | `106834085298` | `PASS_S1_INTEGRATION` | `522c9d3b084773b36521605b2854ca8ee16afc7e4d7c1004a7259255a6963bab` |
| YMM4 4.55.1.1 Lite | `106834085660` | `PASS_S1_INTEGRATION` | `6fce552e0dde4e6be67b8fa1547588b678eee2f25687c2be30ef930884d7dce1` |

Evidence artifacts:

- 4.56.1.0: artifact `10707502296`, digest `sha256:e30830d1e00fa4e76fb98543e72b38f845ba5d3dfe038f27b2add99fa2f6afab`;
- 4.55.1.1: artifact `10707137587`, digest `sha256:02bb487ae65ff8f4dcaa66aac054e2ef3ab8a42b6f95a537ef6eb0c77599e9e2`.

Same-source regression checks:

- P4 hands-on candidate run `35753664220` — success;
- S0 integration run `35753664122` — success on both pinned hosts;
- real `.ymme` run `35753664160` — success;
- P4 minimum workflow run `35753664242` — success.

## 2. Creation semantics now frozen

The product creation context now matches the pinned LayerPatan behavior:

- if the right-clicked layer is part of the current layer selection, use selected `min..max`;
- non-contiguous selection intentionally includes the gaps between min and max;
- if the right-clicked layer is outside the current selection, use only that layer;
- no selection also uses only the clicked layer;
- when the requested start is an existing folder head, shift the new folder start downward until a legal nested/disjoint range is found;
- crossing ranges remain rejected by the existing FolderDocument/FolderRange invariants;
- a final one-row range is accepted as a creation request and asks for one empty layer immediately below it.

The original P4.2 `EvaluateCreate` API remains intact for frozen callers. S1 adds the contextual creation API instead of silently changing the meaning of an earlier frozen entry point.

## 3. One-row create convenience

A one-row create is a composite native/product edit:

1. compute the final FolderDocument purely;
2. preflight the real YMM4 Add Layer command;
3. install a scoped one-shot override in the existing structural bridge;
4. when that exact Add Layer reaches `PreviewExecuted`, append the final FolderDocument Undo/Redo callback;
5. let YMM4 perform the layer insertion and own `Record()`;
6. do not create a second plugin history unit.

The pure plan ensures:

- the new folder owns the inserted row;
- containing ancestor folders also own the inserted row;
- ancestors whose old end already covered the insertion are not expanded twice;
- disjoint folders below the insertion shift exactly once.

Exact-host S1 acceptance proved on both pinned hosts:

- baseline LayerSettings count `0`;
- one-row create at L1 causes one native layer insertion;
- final folder is L1..L2;
- exactly one pending plugin folder history command;
- one Undo removes the inserted layer and the folder together;
- exactly one folder Undo callback;
- one Redo restores both;
- exactly one folder Redo callback.

## 4. Shared FolderCommands boundary

The product candidate now has one typed `FolderCommands` entry surface for:

- contextual Create;
- Rename;
- ToggleCollapsed;
- ExpandAll / CollapseAll;
- Ungroup;
- SelectItemsInFolder.

Timeline context-menu and folder-tag interactions call this same boundary.

The later Tool panel and its keyboard actions must call the same `FolderCommands` methods rather than reimplementing folder mutations. S1 freezes the command ownership now; S4 supplies the management-panel projection and panel-specific keyboard UX later.

No CommandBus, string command DSL, second Undo stack, or second FolderDocument was introduced.

## 5. Timeline UX parity added in S1

Timeline menu:

- contextual create wording shows the resolved range;
- one-row create explicitly says that one empty layer will be added below;
- folder-head adjustment is displayed to the user;
- existing folder submenu supports open/collapse, rename, select items, and ungroup;
- root menu supports expand all / collapse all.

Folder tag:

- arrow area: single-click open/collapse;
- name area: double-click rename;
- the two hit regions are separate so double-click rename does not intentionally reuse the collapse hit target.

The final feel/discoverability of double-click and hit sizes remains part of the overall P4 hands-on gate; S1 freezes the command wiring and non-conflicting hit model.

## 6. Host-access convergence

Standard command lookup/execution used by the S0/S1 product candidate is centralized in `HandsOnHostAccess`.

The S1 one-row composite does not introduce another YMM4 mutation route. It uses the same native Add Layer command family already proven by P1/S0.

## 7. Scope boundaries retained

S1 does **not** add:

- Color / Hidden persistence;
- visibility restoration;
- apply-folder-color-to-YMM4-layer-color;
- folder-end / inside insertion commands beyond the one-row create convenience;
- destructive folder+layers delete;
- Group Control helpers;
- management panel;
- Follow Timeline;
- block drag/drop;
- timing-band visual redesign.

Those remain S2-S5.

## 8. Exit decision

S1 is **COMPLETE / FROZEN**.

Automated evidence covers:

- host-independent creation policy;
- one-row insertion planning;
- serialization;
- dual-host composite native history;
- bulk collapse/expand;
- S0 regression;
- installable candidate startup;
- real `.ymme` packaging/install route;
- existing P4 minimum workflow.

Next phase:

**S2 — extended project state + visual metadata.**
