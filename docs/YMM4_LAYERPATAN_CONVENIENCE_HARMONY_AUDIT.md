# LayerPatan Convenience Features — Harmony Dependency Audit

Audit date: **2026-09-22**

Reference:

- repository: `bluemistel/YMM4-LayerPatan`
- main commit: `aa58e7e772deb829853742afc65e42036cf09d47`
- version: `0.1.0`
- license: MIT

Purpose:

> Determine which LayerPatan convenience/product features actually require Harmony, and which can inform the no-Harmony implementation directly.

## Executive result

**Most convenience features are not Harmony-dependent.**

LayerPatan's Harmony usage is concentrated in two areas:

1. **folded Timeline geometry / coordinate integration**;
2. **project-file ToolState injection / project-open interception**.

The convenience layer itself is mostly implemented with:

- plain WPF;
- FolderTree / LayerOps pure logic;
- TimelineTracker;
- YMM4 Timeline / LayerSettings / item APIs;
- UndoRedoManager;
- bounded reflection for Timeline scrolling / dirty-state helpers.

This is significant for the no-Harmony project because P0-P3 have already replaced the two major Harmony-dependent areas with:

- FoldMap / DirectDisplay / InteractionRouter;
- ToolState lifecycle persistence.

Therefore a large part of LayerPatan's missing convenience feature set can be **ported conceptually without reintroducing Harmony**.

## Harmony locations in LayerPatan

### A. PatchInstaller — Harmony REQUIRED in LayerPatan

Harmony patches are used for folded geometry and native coordinate integration:

- Timeline item Top / Height;
- layer label Top / Height;
- layer line Top / Height;
- max row count / layer-height coordinate sites;
- drag delta mapping;
- AddItem position conversion;
- blank-space / Group background geometry;
- ScrollToItem correction.

This is the part that makes a collapsed folder truly occupy one YMM4 display row.

It also produces the special **thin timing bands for hidden items** by forcing hidden item Height to a small nonzero value and remapping Top.

### B. ProjectPersistence — Harmony REQUIRED in LayerPatan

LayerPatan patches:

- Project construction, to inject its ToolState;
- project-open flow, to load its ToolState before attaching trackers.

If that patch cannot be installed, LayerPatan falls back to a settings-directory JSON store.

The no-Harmony project already has a different, proven project-owned ToolState lifecycle and therefore does not need to copy this Harmony path.

## Convenience-feature dependency matrix

Legend:

- **NO HARMONY** — implementation does not require Harmony;
- **FOLD CORE ONLY** — command/UI is Harmony-free, but its visible collapsed-row effect relies on LayerPatan's patched fold core;
- **CURRENT IMPLEMENTATION USES HARMONY** — the feature itself is realized through a PatchInstaller hook.

| Feature missing from no-Harmony candidate | LayerPatan implementation | Harmony dependency | Portability to no-Harmony |
| --- | --- | --- | --- |
| Single-layer create -> auto-add empty layer | `TimelineTracker.CreateFolder` + `LayerOps.PlanInsert` | **NO HARMONY** | High. Port policy + use no-Harmony structural/native transaction path instead of copying direct remap. |
| Auto-adjust creation start off existing folder head | `FolderTree.AdjustForFolderHeads` | **NO HARMONY** | Very high; pure range logic. |
| Double-click folder tag rename | WPF `MouseLeftButtonDown` click-count + prompt | **NO HARMONY** | Very high. |
| Expand all / collapse all | `ExecuteUiOnly` sets `IsExpanded` on all folders | **FOLD CORE ONLY** | Very high; our FoldMap already provides fold core. |
| Folder color palette | metadata + WPF brushes | **NO HARMONY** | Very high. |
| Apply folder color to YMM4 layer colors | `Timeline.LayerSettings.Colors[layer]` | **NO HARMONY** | High. |
| Folder-wide hide/show | `SetFolderHidden` + `ReconcileVisibility` + `Suppressed` map | **NO HARMONY** | High, but needs persisted visibility-restoration state and native history policy. |
| Toggle individual layer visibility while folder-hidden | `ToggleLayerVisibility` | **NO HARMONY** | High after folder hide/show model exists. |
| Select all items in folder | `Timeline.SelectItems(...)` | **NO HARMONY** | Very high. |
| Add layer at folder end | `InsertLayers` | **NO HARMONY** | Medium-high. UI/plan reusable; host mutation must use our P1/native ownership path. |
| Add layer below current layer inside folder | `InsertLayers` | **NO HARMONY** | Medium-high; same caveat. |
| Delete folder + contained layers/items | `DeleteLayers` | **NO HARMONY** | Medium. Must route through our proven structural/native ownership path; destructive UX needs confirmation. |
| Reorder layer/folder blocks | panel DragDrop -> `MoveBlock` | **NO HARMONY** | Medium. Pure drop policy is reusable; arbitrary block movement needs no-Harmony compatibility work beyond current standard MoveUp/Down coverage. |
| Tool panel row drag/drop | normal WPF DragDrop | **NO HARMONY** | High for UI; underlying move operation still needs adapted host route. |
| Add Group Control for whole folder | `GroupItem` + `TryAddItems` + range plan | **NO HARMONY** | Medium-high. Can use our host/history architecture. |
| Fit Group Control range to folder | write `GroupItem.GroupRange` using `LayerOps.FindGroupIssues` | **NO HARMONY** | High. |
| Auto-correct GroupRange after external structural edit | TimelineTracker structural inference + `LayerOps.FixGroupsAfterExternalChange` | **NO HARMONY** | High conceptually; better to attach to our already-frozen P1 StructuralDelta / same-transaction path rather than copy its inference stack. |
| Group-range warnings / lanes in Tool panel | ViewModel calculations + WPF | **NO HARMONY** | High after Group Control model is ported. |
| Tool panel New Folder/New Layer/Delete buttons | ViewModel commands | **NO HARMONY** | High; underlying structural action caveats apply. |
| Tool panel inline rename / F2 / Ctrl+G | WPF commands | **NO HARMONY** | Very high. |
| Tool panel Follow Timeline | `TimelineScroller.TryScrollToLayer` reflection | **NO HARMONY** | High; bounded reflection, no patching. |
| Tool panel current item at playhead | tracker/current-frame + item lookup | **NO HARMONY** | Very high. |
| Folder list / tree presentation | ViewModel rows + FolderTree | **NO HARMONY** | Very high. |
| Folder tag / depth band overlay | WPF Adorner | **NO HARMONY** | High; already independently proven by no-Harmony P4.1. |
| Fold/unfold tag click itself | WPF event + metadata toggle | **FOLD CORE ONLY** | Already solved in no-Harmony. |
| Native item timing bands inside collapsed owner row | patched item Top / Height | **CURRENT IMPLEMENTATION USES HARMONY** | Needs a new no-Harmony visualization design; cannot copy the PatchInstaller mechanism. |
| Native AddItem right-click coordinate while folded | PatchInstaller AddPosition postfix | **CURRENT IMPLEMENTATION USES HARMONY** | Already independently solved by no-Harmony FoldMap/router. |
| Native item drag coordinate while folded | drag-prefix + delta postfix | **CURRENT IMPLEMENTATION USES HARMONY** | Already independently solved/proven by no-Harmony P0. |
| Native ScrollToItem while folded | PatchInstaller ScrollToItem postfix | **CURRENT IMPLEMENTATION USES HARMONY** | no-Harmony uses explicit fold-aware navigation and intentionally does not guess arbitrary bare same-item ScrollToItem. |
| Folded Group background geometry | patched Top / Height | **CURRENT IMPLEMENTATION USES HARMONY** | Needs no-Harmony compatibility handling if visual parity is required. |
| Project-file persistence | Project constructor/open patches | **CURRENT IMPLEMENTATION USES HARMONY** | Already independently replaced by no-Harmony P3 ToolState lifecycle. |

## Important architectural warning

The fact that `TimelineTracker.InsertLayers/DeleteLayers/MoveBlock` do not use Harmony does **not** mean they should be copied verbatim.

LayerPatan's plugin-owned structural operations directly apply a `LayerPlan` by:

- deleting items;
- rewriting `item.Layer`;
- rewriting `Timeline.LayerSettings.Items`;
- changing GroupRange;
- refreshing Timeline length/max layer.

The no-Harmony architecture intentionally froze a different boundary:

> YMM4/native command ownership remains authoritative; folder state joins the same native transaction.

Therefore use LayerPatan for:

- UX policy;
- pure planning algorithms;
- GroupRange formulas;
- visibility restoration;
- panel behavior;

but adapt structural execution to the existing no-Harmony P1 host/Undo path.

Do **not** import a second structural engine.

## What remains genuinely Harmony-specific

After removing features already independently solved by the no-Harmony core, only a small number of LayerPatan product behaviors remain genuinely tied to its Harmony approach:

1. **the original mechanism for physical row compaction**;
2. **thin timing bands created from hidden native item views**;
3. **patched Group/background/space geometry under folding**;
4. **its particular native coordinate patch implementation**;
5. **its particular project persistence injection implementation**.

Items 1, 4 and 5 already have no-Harmony replacements.

The most interesting unresolved visual parity item is therefore:

> **collapsed-owner-row inner timing summary / thin timing bands**

This should be designed as an overlay/summary owned by the no-Harmony product rather than by mutating hidden native item view Top/Height.

## Recommended reproduction order

Because most convenience features are Harmony-free, reproduction can proceed in increasing host risk.

### Tier A — low risk / mostly UI or pure logic

1. folder-head creation adjustment;
2. single-layer creation convenience policy;
3. double-click rename;
4. expand all / collapse all;
5. select items in folder;
6. folder colors;
7. apply folder color to YMM4 layers;
8. Tool panel list/tree + keyboard conveniences;
9. current-item-at-playhead display.

### Tier B — stateful but still no Harmony

10. folder-wide hide/show + restore original layer visibility;
11. Group Control fit;
12. Group Control add;
13. GroupRange warnings/lanes;
14. GroupRange auto-correction, integrated into no-Harmony P1 delta/history rather than copied tracker inference.

### Tier C — structural convenience

15. add layer to folder end;
16. add layer below inside folder;
17. destructive folder+layers delete;
18. panel block drag/drop/reordering.

These should reuse LayerPatan's pure plan/UX ideas but execute through no-Harmony's frozen native structural ownership.

### Tier D — Harmony-specific visual parity redesign

19. collapsed-row inner timing summary;
20. folded Group/background visual parity if hands-on shows it matters.

## Bottom line

The original assumption is confirmed:

> **LayerPatan's missing convenience features are mostly not Harmony features.**

Harmony is primarily the host-integration method for folded geometry and project injection.

That means the current no-Harmony project can use LayerPatan as a strong reference for most remaining product functionality **without reintroducing Harmony**, while retaining the no-Harmony FoldMap / native-command / persistence architecture already proven in P0-P3.
