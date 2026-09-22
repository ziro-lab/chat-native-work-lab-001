# YMM4 no-Harmony Full — P4 UX Plan v0.1

Status: **P4 entry plan; host surface validation first**.

P0-P3 are frozen. P4 must turn the proven engine into a normal editing workflow without reintroducing Harmony or requiring a separate management window for ordinary use.

## 1. Primary UX surface

Default product surface:

**YMM4 Timeline layer-label column.**

Use two lightweight mechanisms:

1. a small overlay/Adorner on folder-owner rows for folder identity + collapse/expand;
2. the existing layer-label ContextMenu for create / rename / ungroup and other metadata actions.

A Tool panel may exist later as an optional overview/management surface, but normal folder work must not require opening or selecting it.

## 2. Why this surface

The frozen no-Harmony architecture already owns display/logical row mapping and folded display geometry.

The Timeline layer-label column is therefore the closest semantic surface to the thing being edited:

- folder ownership is layer-range state;
- users already select layers there;
- right-click is already a standard layer action surface;
- P0 already proves right-click mapping remains correct under folding.

External reference only, not Lab evidence:

- `bluemistel/YMM4-LayerPatan`;
- inspected main commit `aa58e7e772deb829853742afc65e42036cf09d47`;
- MIT license.

That implementation uses ordinary WPF Adorner + ContextMenu event integration for folder UI. Its Harmony dependency is used for the original folding/coordinate patch, not as proof that those UI surfaces require Harmony.

P4.1 reproduces only the needed UI-surface facts on pinned YMM4.

## 3. Minimum Full actions

Keep P4 minimum narrow:

### Owner-row overlay

- visible folder marker;
- collapse / expand by a small explicit chevron/button;
- clear nested depth/ownership indication;
- do not cover the whole layer-label row.

### Layer-label context menu

When selected layer range can form a valid folder:

- `Lxx-Lyy をフォルダにまとめる...`.

When invoked on / inside an existing folder:

- open / close;
- rename;
- **フォルダを解除（レイヤーは残す）**.

Delete-items / delete-layers is not the same action as deleting folder metadata and is not part of the minimum folder-delete action.

## 4. Creation policy — initial P4 candidate

Avoid surprising implicit range expansion.

Initial candidate:

- require at least two selected layers;
- require selected layers to be contiguous;
- menu shows the exact range before creation;
- reject crossing / duplicate-head states through the existing FolderDocument / range validation;
- nested ranges are allowed only when the resulting range is valid and fully contained rather than partially crossing another folder.

Do not automatically insert a new layer merely to make a one-row folder useful.

This is a UX candidate, not yet frozen. Hands-on may justify a later convenience path.

## 5. Interaction constraints

- clicking outside the small folder control must preserve native layer-label interaction;
- standard YMM4 right-click menu items must remain available;
- injected menu items are removed cleanly when the menu closes / plugin detaches;
- overlay must follow native scroll / row-height changes through the same folded display authority;
- no synthetic full-row input replacement;
- no pixel-perfect test requirement;
- no Harmony.

## 6. State ownership

UI commands mutate the existing product state only:

```text
overlay / context menu
        ↓
FolderDocument command
        ↓
FolderHistoryAdapter
        ↓
YMM4 UndoRedoManager
        ↓
FoldMap / FoldDisplay + Persistence
```

P4 must not introduce a second folder model inside ViewModels or WPF elements.

### Metadata history boundary

P4.2b exact-host evidence proves a FolderDocument-only change can use one public `UndoRedoActionCommand + UndoRedoManager.Record()` transaction to:

- mark the YMM4 project unsaved;
- restore the prior FolderDocument through real Ctrl+Z;
- restore the forward FolderDocument through real Ctrl+Y;
- return to saved state through normal SaveProject.

Therefore create / rename / collapse / ungroup share one small FolderHistoryAdapter. No custom history stack or separate dirty flag is needed.

Hands-on may later choose to keep collapse/expand out of visible Undo history for ergonomics, but that would be a UX refinement over a proven safe mechanism.

## 7. P4 validation sequence

### P4.1 — UI surface viability

Exact-host V1 on YMM4 4.56.1.0:

- find the real layer-label list;
- attach/remove an Adorner without Harmony;
- small native-clickable overlay control;
- click outside overlay still reaches the normal layer-label row;
- observe layer-label ContextMenuOpening;
- append/remove a tagged folder menu without replacing the native menu;
- attach/detach without stale controls.

### P4.2 — pure UX command policy

Host-independent:

- creation eligibility;
- exact selected contiguous range;
- nested/disjoint validity;
- rename validation;
- ungroup removes metadata only;
- collapse/expand changes metadata only.

### P4.3 — integrated minimum workflow

Primary host hands-on + focused native smoke:

1. select contiguous layers;
2. create named folder;
3. collapse;
4. expand;
5. rename;
6. ungroup;
7. verify items/layers remain unchanged;
8. save/reopen and repeat collapse.

### P4 exit

- core tasks work without Lab-only state injection;
- user does not need to open a separate Tool panel for normal work;
- no standard Timeline gestures are stolen;
- unsupported creation state is visible and non-destructive;
- manual hands-on confirms discoverability/comfort;
- both-host testing only for host-dependent UI surfaces at phase freeze.

## 8. Explicitly deferred

Not minimum P4 Full:

- folder color palette;
- folder-wide visibility/hide;
- automatic Group Control creation/fitting;
- drag-reorder management panel;
- elaborate animations;
- large-project performance claims;
- broad special-item compatibility.

These may be revisited after the minimum workflow is stable.
