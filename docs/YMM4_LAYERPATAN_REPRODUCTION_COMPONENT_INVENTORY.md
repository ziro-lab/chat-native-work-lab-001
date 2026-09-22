# YMM4 no-Harmony Layer Folder — Reference Component Inventory

Date: **2026-09-22**

Purpose:

> Reproduce the **feature set** of LayerPatan while allowing a different internal structure, with maintainability as the primary architectural constraint.

This document is a **parts inventory**, not a final design freeze.

The guiding rule is:

> Prefer a small number of reusable product components over one implementation path per feature.

## 1. Source registry used

### Existing Lab / frozen evidence

- P0 FoldMap / DirectDisplay / interaction spine;
- P1 FolderRangeTracker;
- P1 StructuralDeltaDetector;
- P1 same-transaction native Undo bridge;
- P2 fold-aware navigation;
- P3 FolderDocument / schema / ToolState lifecycle;
- P4 layer-label Adorner / ContextMenu surface;
- P4 pure FolderUxCommands;
- P4 metadata-only native history;
- P4 installable hands-on candidate.

These are product-mechanism candidates with version-pinned evidence.

### LayerPatan reference

Repository:

- `bluemistel/YMM4-LayerPatan`
- inspected commit: `aa58e7e772deb829853742afc65e42036cf09d47`
- license: MIT

High-value files:

- `Core/FolderTree.cs`
- `Core/LayerOps.cs`
- `Integration/LayerMenu.cs`
- `Integration/TimelineOverlay.cs`
- `Services/TimelineTracker.cs`
- `ViewModels/LayerPanelViewModel.cs`
- `Views/LayerPanelView.xaml(.cs)`

Use as:

- feature behavior reference;
- pure algorithm reference;
- UX reference.

Do **not** copy its Harmony patch architecture or its direct structural execution as a second host engine.

### Official YMM4 / maintained ecosystem references

Registry:
`docs/YMM4_REFERENCE_SOURCES.md`

Important references:

- official YMM4 plugin documentation;
- official plugin samples;
- `manju-summoner/YukkuriMovieMaker.Plugin.Community`;
- YMM API Docs;
- YMM4Plugin Scrapbox;
- InuInu YMM4 plugin notes;
- `leftcontroller0518/YMM4plugin_template`.

## 2. Proposed small component vocabulary

The current evidence suggests that full LayerPatan feature parity does **not** need one service/class per feature.

A maintainable target can likely be built around the following reusable components.

---

## A. Folder Core

### A1. FolderDocument

**Already exists.**

Owns persisted folder state only.

Current v1:

- Id;
- Start / End;
- Name;
- IsCollapsed;
- per-Timeline identity.

Likely future fields required for parity:

- Color;
- hidden-folder state;
- visibility restoration metadata.

Do not store:

- WPF rows;
- display Y;
- Tool panel row objects;
- parent pointers;
- live YMM4 objects.

### A2. FolderRules / FolderTree

**Existing no-Harmony rules + LayerPatan pure reference.**

Responsibilities:

- nested/disjoint validity;
- parents / children derived from interval containment;
- innermost-containing lookup;
- folders within range;
- folder-head conflict policy;
- deterministic order.

Useful LayerPatan reference:

- `FolderTree.Ordered`
- `Parents`
- `SelfAndAncestors`
- `SelfAndDescendants`
- `InnermostContaining`
- `Within`
- `AdjustForFolderHeads`

This is almost entirely pure logic and is a good place to absorb LayerPatan convenience semantics without adding host dependencies.

### A3. FolderActions

**Generalize current `FolderUxCommands`.**

One pure intent layer should eventually cover:

- Create;
- Rename;
- ToggleCollapsed;
- ExpandAll / CollapseAll;
- Ungroup;
- set Color;
- set Hidden;
- create-one-layer convenience policy;
- create-head adjustment;
- move/reparent folder metadata;
- remove metadata after destructive delete.

This should return a new FolderDocument or a small result/intent object.

It should not call YMM4.

### A4. FolderRangeTracker

**Frozen P1 component.**

Keep as the only positional folder-range mutation engine for observed host structural edits.

Do not add a second range engine from LayerPatan `LayerOps`.

LayerPatan algorithms may be used as test/reference input, but product range authority remains FolderRangeTracker.

---

## B. Structural Observation & Native History

### B1. StructuralDeltaDetector

**Frozen P1 component.**

Commonizes:

- InsertLayers;
- DeleteLayers;
- SwapAdjacentLayers;
- ambiguity refusal.

This is a major simplification over command-specific adapters.

### B2. NativeHistoryBridge

**Already proven in P1/P4.2b.**

Two modes:

1. structural host command:
   - `UndoRedoManager.AddCommand(...)`;
   - do not call `Record()`;
   - let native command commit one transaction.

2. metadata-only plugin command:
   - AddCommand;
   - plugin calls one `Record()`.

This one adapter should own Undo semantics for all future folder features.

Avoid feature-specific Undo stacks.

### B3. TimelineOps facade

**New thin candidate component.**

Purpose:

> Keep all actual YMM4 mutations behind one host boundary.

Candidate operations:

- add layer;
- delete layer;
- move layer/block where a supported route exists;
- add GroupItem;
- set GroupRange;
- select items;
- set layer visibility;
- set layer color;
- refresh Timeline max/length if required;
- scroll/follow layer.

Sources:

- existing Lab standard structural command observations;
- LayerPatan TimelineTracker behavior;
- public Timeline APIs;
- YMM4 API reference.

Important rule:

Do not expose arbitrary `item.Layer = ...` / `LayerSettings.Items = ...` rewriting to feature services unless a dedicated host gate proves that route is needed.

This facade is where LayerPatan's structural planning must be adapted to no-Harmony native ownership.

---

## C. Fold Display & Timeline Interaction

### C1. FoldMap

**Frozen authority.**

Single logical Layer <-> display row mapping.

All future features must use this instead of introducing another layout map.

### C2. FoldDisplay

**Existing DirectDisplay candidate.**

Owns:

- row/item display placement;
- collapsed extent;
- virtualization;
- gesture-time geometry freeze;
- detach/restore.

Feature services never manipulate Top/Height directly.

### C3. LayerLabelSurface

**Existing P4 surface, should become a reusable product component.**

Owns:

- current LayerLabels control discovery;
- current realized ContextMenu owner lookup;
- bounded Window input router;
- input-transparent Adorner;
- virtualization-safe reacquisition;
- tagged menu attach/remove;
- small folder hit regions.

This can serve:

- folder tag;
- rename gesture;
- folder context menu;
- colors;
- hide/show indicator;
- nesting indicator;
- all-open/all-close menu;
- later timing summary.

Do not let every feature subscribe independently to the Window or LayerLabels tree.

### C4. NavigationBridge

**Existing P2 component.**

Use for:

- Tool panel Follow Timeline;
- selecting/following an item/layer;
- reveal hidden target;
- same-item navigation owned by product.

LayerPatan's `TimelineScroller` reflection route is useful reference, but no second navigation policy should be introduced.

---

## D. Persistence & Runtime State

### D1. FolderStateStore

**P4.4 candidate.**

One runtime owner of current FolderDocument.

This prevents normal Timeline UI from depending on Tool panel ViewModel lifetime.

### D2. FolderPersistenceSession

**P3 recovery component.**

Owns:

- schema decoding;
- unreadable/future-state quarantine;
- exact raw preservation;
- explicit Replace / Reset.

### D3. ProjectStateCoordinator

**P3 lifecycle component.**

Owns:

- project ToolState sync;
- project switch;
- new-project reset;
- Timeline.ID association.

No feature should individually handle project lifecycle.

### D4. SettingsBase preferences

Use YMM4 `SettingsBase<T>` only for **global plugin preferences**, for example:

- default folder color;
- whether Tool panel follows Timeline;
- whether current-item column is shown;
- optional UI density;
- destructive-action confirmation preference.

Do not put project folder structure into SettingsBase.

Reference:

- official/Community usage;
- `leftcontroller0518/YMM4plugin_template` Settings sample.

---

## E. Feature Services

These are intentionally small services over FolderCore + TimelineOps.

### E1. VisibilityService

Feature parity:

- folder hide/show;
- restore each layer's prior visibility;
- toggle one layer's post-unhide visibility while hidden.

LayerPatan reference:

- `SetFolderHidden`
- `ToggleLayerVisibility`
- `ReconcileVisibility`
- persisted `Suppressed` map.

Suggested simplification:

- visibility restoration state belongs in FolderDocument;
- one service computes desired changes;
- TimelineOps applies them;
- NativeHistoryBridge owns history.

No UI-specific visibility logic.

### E2. GroupControlService

Feature parity:

- find group-range issues;
- fit GroupRange;
- add Group Control for folder;
- auto-correct GroupRange after structural edits;
- supply warning data for panel.

LayerPatan reference:

- `LayerOps.FindGroupIssues`;
- `FixGroupsAfterExternalChange`;
- `TimelineTracker.FitGroupsToFolder`;
- `AddGroupControlToFolder`.

Suggested simplification:

- pure `GroupControlRules` computes issues/fixes;
- one `GroupControlService` applies via TimelineOps;
- auto-correction consumes the **existing StructuralDeltaDetector output**.

Do not copy LayerPatan's separate structural inference stack.

### E3. LayerStructureService

Feature parity convenience:

- one-layer folder creation -> insert one layer;
- add layer at folder end;
- add layer below current inside folder;
- destructive folder+layers delete;
- panel block move/reparent.

Suggested simplification:

- feature emits a small structural intent;
- TimelineOps executes the supported YMM4 operation;
- StructuralDeltaDetector + FolderRangeTracker remains positional authority;
- NativeHistoryBridge joins plugin metadata into the native transaction.

LayerPatan `PlanInsert/PlanDelete/PlanMove` is reference logic, not product execution authority.

### E4. SelectionService

Feature parity:

- select all items in folder.

Likely tiny wrapper around public Timeline selection.

Do not create a general selection subsystem unless another feature needs it.

### E5. TimingSummaryService

Feature parity:

- collapsed owner row shows inner item timing information.

LayerPatan implementation is Harmony-specific because it patches hidden native item Top/Height.

Suggested no-Harmony component:

- derive timing intervals from Timeline items;
- render them inside LayerLabelSurface overlay;
- no native item-view mutation.

This is the main feature that requires redesign rather than porting.

---

## F. Tool Panel

### F1. FolderPanelProjection

Do not make Tool panel rows authoritative state.

Projection should be derived from:

- FolderDocument;
- current Timeline layer snapshot;
- GroupControlService diagnostics;
- current frame / item lookup.

Produces simple row VMs:

- FolderRow;
- LayerRow;
- optional group warning;
- current item summary.

### F2. Reusable WPF interaction patterns

Useful maintained Community-source references:

`YukkuriMovieMaker.Plugin.Community/Tool/Explorer/`

- `RightClickSelectionBehavior`
- `ScrollSelectedItemIntoViewBehavior`
- `RenameTextBoxBehavior`
- `DropHighlightAdorner`

License note:

- Community repository is LGPL-3.0 at inspected revision.

Preferred use:

- copy the **interaction pattern**, not source text, unless license handling is intentionally accepted.

These patterns can simplify:

- context-menu selection;
- Follow Timeline panel scrolling;
- F2/inline rename;
- drag/drop target feedback.

### F3. Tool shell

References:

- official Tool plugin contract;
- Community Notepad/Explorer tools;
- MIT YMM4plugin_template Timeline Tool.

Reusable public surfaces:

- `IToolPlugin`;
- `IToolViewModel`;
- `ITimelineToolViewModel`;
- `TimelineToolInfo`;
- `ToolState`;
- localized Utilities group resource.

Keep Tool panel optional for normal folder editing.

---

## G. YMM4-native UI controls

YMM4 official documentation currently directs plugin authors to reference:

- `YukkuriMovieMaker.Plugin.dll`;
- `YukkuriMovieMaker.Controls.dll`.

The YMM4Plugin reference lists plugin-facing editor controls including:

- `ColorPicker`;
- `TextEditor`;
- `EnumComboBox`;
- `ToggleSlider`;
- `FrameNumberEditor`;
- `TimeSpanEditor`;
- selectors for files/directories/scenes/etc.

For LayerPatan parity, the immediately interesting component is:

### G1. ColorPicker

Use as a candidate for:

- default color setting;
- panel folder color editing.

This may allow the no-Harmony version to avoid maintaining a custom color-picker implementation.

The current LayerPatan fixed palette can still be offered as shortcuts.

Before product adoption, verify the exact current control/API signature against the pinned host or current assemblies.

---

## 3. Simplification opportunity

LayerPatan distributes functionality across:

- FolderTree;
- LayerOps;
- TimelineTracker;
- TimelineHost;
- TimelineOverlay;
- LayerMenu;
- LayerPanelViewModel;
- panel code-behind;
- PatchInstaller;
- ProjectPersistence;
- FolderStore;
- TimelineScroller;
- service/glue classes.

The no-Harmony product does **not** need to mirror this topology.

A smaller target could be:

```text
FolderCore
  FolderDocument
  FolderRules
  FolderActions
  FolderRangeTracker

HostBridge
  TimelineOps
  StructuralObserver
  NativeHistoryBridge
  ProjectStateCoordinator
  NavigationBridge

Presentation
  FoldMap / FoldDisplay
  LayerLabelSurface
  FolderPanelProjection

FeatureServices
  VisibilityService
  GroupControlService
  LayerStructureService
  TimingSummaryService
```

The Tool panel and Timeline overlay both consume the same FolderCore + FeatureServices.

No feature owns its own YMM4 reflection, Undo stack, persistence, or folder tree.

## 4. Source / license handling

| Source | License / role | Preferred use |
| --- | --- | --- |
| no-Harmony Lab | project evidence | reuse implementation/components directly inside project lineage |
| LayerPatan | MIT | pure algorithms / UX reference can be adapted; preserve attribution as required |
| YMM4 official samples/docs | official reference | public API / intended patterns |
| YMM Community source | LGPL-3.0 | prefer behavioral pattern/reference; avoid casual source copying |
| YMM4plugin_template | MIT | Tool/Settings shell patterns |
| YMM4Plugin Scrapbox | community notes | discovery of native controls; verify current API before use |
| YMM API Docs | unofficial current API index | symbol discovery only; verify behavior where necessary |

## 5. First components worth harvesting

Highest payoff before implementing more features:

1. **FolderTree utility set**
   - parent/child derivation;
   - innermost containing;
   - folder-head adjustment.

2. **GroupControlRules**
   - pure issue detection/fix calculation from LayerPatan.

3. **Visibility state model**
   - suppressed/original visibility semantics.

4. **LayerLabelSurface extraction**
   - turn P4 probe/controller logic into one reusable product surface.

5. **TimelineOps facade**
   - one place for public/native host mutations.

6. **FolderPanelProjection**
   - derive panel rows from core state; no second model.

7. **YMM4 native ColorPicker viability**
   - avoid a custom color editor.

8. **Community WPF interaction patterns**
   - right-click selection;
   - scroll selected row into view;
   - inline rename;
   - drag/drop highlight.

9. **TimingSummary overlay design**
   - intentionally new no-Harmony implementation.

## 6. Questions that still deserve targeted Lab probes

Do not probe everything.

Likely narrow host questions only:

- exact simplest native route for arbitrary layer insertion requested by plugin UI;
- exact simplest native route for destructive multi-layer delete;
- whether GroupItem.GroupRange mutation participates in native history as expected when performed through the shared adapter;
- current YMM4 ColorPicker/control usage if its API is not clear enough from current assemblies;
- panel block move semantics if strict LayerPatan drag/drop parity is required.

Everything else above can be designed first from existing evidence/reference material.

## Bottom line

The available reference material supports a **feature-parity / architecture-non-parity** strategy.

Most remaining LayerPatan functionality can be expressed using a small shared set of no-Harmony components.

The biggest maintenance win will come from refusing to duplicate these concerns per feature:

- YMM4 mutation;
- Undo;
- persistence;
- fold mapping;
- navigation;
- Tool panel state.

Those should remain centralized while new LayerPatan features become mostly pure intents + thin UI.
