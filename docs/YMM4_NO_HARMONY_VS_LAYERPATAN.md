# YMM4 no-Harmony Folder vs LayerPatan — Feature Comparison

Comparison date: **2026-09-22**

Reference implementation:

- `bluemistel/YMM4-LayerPatan`
- main commit `aa58e7e772deb829853742afc65e42036cf09d47`
- version in project: `0.1.0`
- MIT license
- uses Harmony 2.4.2

no-Harmony implementation reference:

- P0-P3 frozen Lab evidence;
- P4.1 UI surface;
- P4.2 pure UX commands;
- P4.2b metadata history;
- P4.3 minimum workflow;
- P4.4 installable hands-on candidate.

This document compares **current implemented behavior**, not intended future scope.

## Summary

The current no-Harmony candidate has reached practical parity for the **minimum folder workflow**:

- create from a selected contiguous range;
- nested folders;
- collapse / expand;
- rename;
- ungroup while keeping layers/items;
- project persistence;
- normal folded Timeline interaction;
- native Undo/Redo integration;
- no mandatory Tool panel.

It is **not yet feature-parity with LayerPatan 0.1.0**.

LayerPatan currently has a substantially richer convenience/management layer:

- colors;
- folder-wide hide/show;
- add-layer conveniences;
- item selection helpers;
- Group Control creation/fitting/auto-correction;
- destructive folder+layer delete;
- expand/collapse all;
- a full management panel;
- panel drag/drop reordering;
- current-item/follow-Timeline information;
- collapsed-row visual/timing detail.

The no-Harmony candidate is stronger in some engineering boundaries:

- no runtime patching framework;
- more explicit fold-aware interaction classification;
- same-transaction structural folder Undo;
- versioned persistence schema;
- unreadable/future-state raw preservation;
- explicit project-switch/new-project lifecycle evidence;
- exact-host regression evidence across the pinned hosts for P0-P3.

## Feature matrix

Legend:

- **YES** — currently implemented / proven in that implementation;
- **PARTIAL** — mechanism exists but convenience/UI parity is incomplete;
- **NO** — not currently implemented;
- **DIFFERENT** — both support the area but intentionally use different behavior.

| Area | LayerPatan 0.1.0 (Harmony) | no-Harmony P4.4 candidate | Notes |
| --- | --- | --- | --- |
| Timeline-native folder display | YES | YES | Both operate directly in the Timeline layer-name area. |
| Collapse folders to one visible row | YES | YES | Different implementation mechanism. |
| Nested folders | YES | YES | Both derive nesting from contained ranges. |
| Create folder from selected layers | YES | YES | Both show/use a selected layer range. |
| One-layer creation | YES | NO | LayerPatan inserts an empty layer and creates a 2-layer folder. no-Harmony currently requires 2+ selected contiguous layers. |
| Auto-adjust selection beginning on existing folder head | YES | NO | LayerPatan may shift creation start one row; no-Harmony currently rejects duplicate-head/crossing creation. |
| Collapse / expand from folder tag | YES | YES | Both provide a visible folder control. |
| Rename from menu | YES | YES | |
| Double-click folder tag to rename | YES | NO | no-Harmony currently uses the menu/prompt path. |
| Ungroup / remove folder metadata while keeping layers | YES | YES | no-Harmony wording explicitly says layers remain. |
| Delete folder **and** contained layers/items | YES | NO | Deliberately not in minimum no-Harmony UX yet. |
| Expand all / collapse all | YES | NO | |
| Folder color | YES | NO | LayerPatan has an 8-color palette + default. |
| Apply folder color to YMM4 layer colors | YES | NO | |
| Folder-wide hide/show | YES | NO | LayerPatan preserves each layer's prior visibility state. |
| Select all items in folder | YES | NO | |
| Dedicated “add layer to folder end” | YES | NO | no-Harmony standard Add Layer structural tracking exists, but the dedicated convenience command is absent. |
| Dedicated “add below this row inside folder” | YES | NO | Same distinction as above. |
| Native structural Add/Delete/Move keeps folder ranges correct | YES | YES | no-Harmony has frozen P1 coverage for Add/Delete/MoveUp/MoveDown + Undo/Redo. |
| Group Control range auto-correction after structural edits | YES | NO | Major feature gap; LayerPatan explicitly adjusts GroupRange. |
| Add Group Control covering whole folder | YES | NO | |
| Fit existing Group Controls to folder | YES | NO | |
| Native right-click item-add while folded | YES | YES | no-Harmony has FoldMap mapping evidence. |
| FileDrop while folded | YES | YES | no-Harmony has real FileDrop native evidence. |
| Native item drag while folded | YES | YES | no-Harmony has single/multi drag evidence; LayerPatan README says cross-fold layer-name reorder is not sufficiently verified. |
| Shift marquee / range interaction | YES (claimed standard operation) | YES | no-Harmony has exact native proof. |
| Keyboard layer navigation across folded boundary | Not specifically documented | YES | no-Harmony P2 verifies Lower/Higher and real Down/Up. |
| Fold-aware product navigation | Harmony patches ScrollToItem | YES via explicit fold-aware navigation | no-Harmony intentionally does not guess arbitrary bare same-item ScrollToItem. |
| Collapsed row shows inner timing / thin item bands | YES | NO | LayerPatan README explicitly advertises this visual summary. |
| Folder depth/color band on left | YES | PARTIAL | no-Harmony shows nested-depth tags but lacks the mature color/band presentation. |
| Folder tooltip with range/count/actions | YES | PARTIAL | no-Harmony hands-on visual is simpler. |
| Full Tool management panel | YES | NO | no-Harmony Tool panel is currently informational/persistence support only. |
| Tool panel new-folder/new-layer/delete controls | YES | NO | |
| Tool panel expand/collapse-all | YES | NO | |
| Tool panel row drag/drop reordering | YES | NO | |
| Tool panel Follow Timeline | YES | NO | |
| Tool panel “current item at playhead” | YES | NO | |
| Tool panel group-range lanes/warnings | YES | NO | |
| Project-file folder persistence | YES | YES | Both store folder state in project ToolState data. |
| Persistence implementation needs Harmony | YES | NO | LayerPatan patches Project ctor / OpenProjectAsync; no-Harmony uses ToolArea ToolState lifecycle. |
| Fallback external settings store if project hook fails | YES | NO | LayerPatan falls back to YMM4 setting directory JSON when its persistence patch cannot install. |
| Explicit persisted schema version | NO | YES | no-Harmony v1 has explicit schema version. |
| Malformed/future state keeps exact unreadable raw data | NO | YES | no-Harmony quarantines exact raw state so unrelated save does not silently replace it while plugin is present. |
| New-project old folder-state leak explicitly handled | Not specifically documented | YES | no-Harmony P3.4 observes and corrects the host lifecycle. |
| Project switch lifecycle explicitly verified | Not specifically documented | YES | no-Harmony P3.2. |
| Plugin absent: project can still open | YES | YES | |
| Plugin absent then project re-saved: folder metadata guaranteed preserved | Not proven | NO | no-Harmony tested this explicitly and classified metadata loss as a known host limitation. |
| Folder metadata edits participate in native Undo/Redo | DIFFERENT | YES | LayerPatan's display-only collapse/name/color path intentionally does not add Undo history; no-Harmony candidate currently records metadata changes in YMM4 UndoRedoManager. |
| Structural folder correction shares native user Undo transaction | PARTIAL / DIFFERENT | YES | no-Harmony P1 places folder range state in the same YMM4 structural history unit. LayerPatan GroupRange correction is documented as a separate history operation in its README. |
| Harmony dependency | YES | NO | Primary architectural difference. |
| If folding hook becomes incompatible | Folding disables, overlay/menu remain | No Harmony hook exists | no-Harmony still has version-sensitive YMM4 access that must be compatibility-tested. |
| Tested YMM4 4.55.1.1 | YES | YES for P0-P3 | P4 UI currently primary-host 4.56.1.0 until P4 V2. |
| Tested YMM4 4.56.1.x | YES | YES | |
| One-file .ymme installer | YES | IN PROGRESS | P4 packaging branch adds and real-installer-tests this. |

## Architecture difference

### LayerPatan

Its folding implementation uses Harmony patches across several internal YMM4 surfaces, including:

- Timeline item Top / Height;
- layer label Top / Height;
- layer line Top / Height;
- max-layer/layout calculations;
- drag delta mapping;
- AddItem coordinate conversion;
- Timeline layer-height conversions;
- ScrollToItem;
- background/space geometry.

Project persistence also uses Harmony to patch Project construction and project-open flow.

If those hooks cannot be installed, LayerPatan is designed to disable folding and/or fall back to a settings-directory store rather than leave a broken patched state.

### no-Harmony

The current design instead converges on:

```text
FolderDocument
    -> FolderRangeTracker
    -> FoldMap
    -> FoldDisplay / InteractionRouter
    -> bounded YMM4 host access
```

Important consequences:

- YMM4 standard commands continue to own item creation, frame changes and structural operations;
- logical Layer <-> displayed row conversion has one FoldMap authority;
- folder range state joins YMM4 structural Undo rather than replacing commands;
- persistence is handled through ToolState surfaces rather than Project Harmony patches;
- unsupported/ambiguous host routes are classified rather than guessed.

This does **not** mean the no-Harmony implementation has no YMM4-private knowledge. It still uses bounded reflection/nonpublic host access where exact-host evidence showed it was necessary.

## Current gap assessment

### Core folder workflow

**Close to parity.**

The everyday minimum:

```text
select layers
 -> create folder
 -> collapse / expand
 -> rename
 -> edit Timeline while folded
 -> ungroup
 -> save / reopen
```

is present in both.

### Convenience features

**LayerPatan is clearly ahead.**

The largest user-visible gaps are:

1. color + mature folder-band presentation;
2. folder hide/show;
3. add-layer convenience commands;
4. expand/collapse all;
5. select-items-in-folder;
6. full management Tool panel;
7. folder/list drag-and-drop management;
8. destructive “folder + layers/items delete” command.

### Group Control integration

**LayerPatan is substantially ahead.**

It has:

- automatic GroupRange correction;
- explicit fit command;
- whole-folder Group Control creation;
- panel warnings/visual lanes.

The no-Harmony project has intentionally deferred this to compatibility/product work rather than mixing it into the frozen folder-range core.

### Safety / lifecycle evidence

**no-Harmony is more explicit.**

The no-Harmony Lab has dedicated evidence for:

- malformed/future schema recovery;
- exact raw-state preservation;
- project A/B switching;
- new project reset;
- reload -> structural edit -> Undo/Redo;
- both pinned hosts for P3;
- plugin-absent re-save limitation.

LayerPatan has robust error catching and a fallback settings store, but its current data model has no explicit schema-version field and a deserialize failure falls back to no loaded folder documents rather than preserving the unreadable raw project state as an editable quarantine.

## What “feature parity” would require

A strict feature-parity target with LayerPatan 0.1.0 would still require, at minimum:

- single-layer create convenience or an explicit decision not to copy it;
- folder-head selection convenience or an explicit rejection policy;
- double-click rename;
- expand/collapse all;
- folder color;
- YMM4 layer-color application;
- folder hide/show with per-layer visibility restoration;
- folder-end / in-folder layer insertion helpers;
- select-items-in-folder;
- destructive folder+layer delete if intentionally desired;
- Group Control add/fit/auto-correction;
- collapsed timing-summary presentation;
- full management Tool panel or an explicit decision that it is not part of the no-Harmony product.

The no-Harmony project does **not** need to clone all of these merely to prove the core replacement useful. P4 hands-on should decide which convenience features materially improve normal editing before expanding scope.
