# No-Harmony Full Fold — work plan

## Current conclusion

The no-Harmony full-fold direction remains viable.

Already reproduced without Harmony in real YMM4:

- transformed-item click hit-testing;
- right-click / standard add-position correction;
- Shift-marquee replacement;
- native item drag with post-corrected logical layer;
- native horizontal/frame movement retained;
- Ctrl+Z / Ctrl+Y retained;
- multi-item drag retained;
- file-drop routing through YMM4 AddFileItem with post-corrected logical layer;
- generic display-row ↔ logical-layer calculation for multiple collapsed folders;
- generic nested collapsed-folder ownership/mapping;
- generic right-click/add mapping under non-uniform row jumps.

The current blocker is **not FolderLayout math**.

The current blocker is mixing:

1. generalized interaction mapping; and
2. a more product-like direct Top/Height Timeline layout strategy.

The direct layout experiment can reach marquee and drag, but currently hangs around the first right-click phase after direct layout mutation.

## Rule for the next work

Do not debug input generalization and direct host layout in the same probe.

From here, split work into three tracks and only integrate after each track is independently green.

---

## Track A — finish generalized interaction semantics on a known-stable visual model

### Baseline

Use the already-proven WPF/RenderTransform visual model.

Do **not** mutate TimelineItemViewModel Top/Height in this track.

Use the generalized `FolderLayout` only for:

- logical layer ownership;
- display-row list;
- display Y -> logical Layer;
- logical Layer -> visual row;
- point remapping.

### Required scenarios

Use at least these two layouts:

#### Layout A — multiple disjoint closed folders

- closed `[2..3]`
- closed `[6..8]`

Expected visible logical rows:

```
0,1,2,4,5,6,9,10
```

This deliberately creates the non-uniform visible transition:

```
display L6
  ↓ one visible row
logical L9
```

#### Layout B — nested closed folder under a closed parent

- closed parent `[1..5]`
- closed child `[2..3]`
- closed `[6..8]`

Expected visible logical rows:

```
0,1,6,9,10
```

Hidden child rows must resolve to the visible parent owner.

### Acceptance criteria

Track A is complete only when all are green on YMM4 4.55.1.1 and 4.56.1.0:

- normal item click on a transformed visible row;
- right-click cursor mapping;
- standard add-position converter mapping;
- Shift-marquee selection;
- one-item diagonal native drag across a non-uniform jump such as L6 -> L9;
- native frame delta preserved;
- Ctrl+Z restores original frame/layer;
- Ctrl+Y restores corrected frame/layer;
- multi-selected drag across the same non-uniform mapping;
- dynamic switch from Layout A to Layout B;
- normal click still works after nested parent collapse;
- no Harmony assembly/reference/runtime patch.

### Important constraint

If a custom input adapter is needed, it should only replace behavior that cannot be safely post-corrected.

Prefer:

```
YMM4 native behavior
    +
logical-layer post-correction
```

over a full custom reimplementation.

---

## Track B — stabilize product-like direct Timeline display compression

Track B is visual/layout-only.

Do not test marquee, native drag, right-click add, or file drop until basic direct layout stability is proven.

### Goal

Determine whether host Timeline ViewModel geometry can be compressed without Harmony by changing internal geometry through bounded reflection, while avoiding layout/reentrancy loops.

### Test order

1. Apply direct Top compression to visible item rows only.
2. Verify idle stability for several seconds.
3. Scroll vertically.
4. Change viewport.
5. Change YMMSettings.LayerHeight.
6. Force Timeline canvas refresh if necessary.
7. Expand/collapse repeatedly.
8. Switch from Layout A to Layout B.
9. Restore identity layout.
10. Only after all above are stable, test a normal click.
11. Then test right-click.
12. Then test marquee/drag.

### Do not do initially

- do not change Top and Height repeatedly inside mouse-move;
- do not call full relayout from bubble MouseMove;
- do not force FastCanvas UpdateAll from every input event;
- do not combine Viewport mutation and geometry rewrite inside one reentrant path.

### Likely failure mode to investigate

Current evidence places the hang after:

```
layout A applied
marquee completed
drag completed
before right-click A
```

Likely suspects:

- direct Top/Height mutation plus YMM4 layout regeneration;
- Viewport mutation after compressed geometry;
- FastCanvas refresh + input hit-testing reentrancy;
- host re-writing Top while the plugin writes Top again;
- direct hidden-row height mutation causing virtualization churn.

### Instrumentation requirement

Before each host-facing geometry mutation, record:

- logical Layer;
- old Top/Height;
- desired Top/Height;
- current Viewport;
- current input phase;
- whether layout application is already in progress.

Use a reentrancy guard and count repeated layout applications.

A repeated application threshold should fail the probe instead of allowing a hang.

---

## Track C — integrate only after A and B are independently green

Integration target:

```
FolderLayout
  ├─ display row <-> logical Layer
  ├─ owner row for hidden/nested folders
  └─ all coordinate mapping

Display adapter
  └─ stable host/WPF compression

Interaction adapter
  ├─ normal click: native
  ├─ right-click/add: cursor remap
  ├─ marquee: minimal replacement
  ├─ item drag: native frame/snap + layer post-correction
  ├─ multi drag: group layer post-correction
  └─ file drop: YMM4 AddFileItem + layer post-correction
```

### Integration acceptance

- no Harmony;
- no host freeze;
- no permanent Timeline geometry corruption;
- opening all folders restores identity geometry;
- project can continue editing after repeated fold/unfold;
- Undo/Redo remains host-owned where possible;
- all previously-green interaction probes remain green;
- YMM4 4.55.1.1 and 4.56.1.0 both pass.

---

## Deferred until after Track C

Do not expand scope yet to:

- folder persistence;
- folder creation UX;
- labels/colors;
- Project.ToolStates;
- group-control range repair;
- full folder tree editor;
- installer/package;
- performance optimization;
- public release compatibility promises.

These are productization concerns, not architecture blockers.

---

## Decision gates

### Gate 1 — Track A fails

If generalized interaction cannot preserve native behavior even on RenderTransform layout, identify the exact route that still requires host patching.

Only that route becomes a candidate for minimal Harmony.

Do not fall back to the old broad Harmony patch set automatically.

### Gate 2 — Track A passes, Track B fails

Then no-Harmony full interaction is still proven, but direct host geometry is too unstable.

Use a safer WPF/overlay/transform display architecture instead of forcing private Top/Height mutation.

This is an acceptable product architecture.

### Gate 3 — A and B pass

Proceed to Track C and build the first real no-Harmony folder prototype.

### Gate 4 — integrated prototype regresses

Bisect by adapter:

```
FolderLayout
Display
Click
RightClick/Add
Marquee
Drag
MultiDrag
FileDrop
```

Never debug all routes at once.

---

## Current recommendation

The next commit should **not** continue patching the current direct-layout probe in place.

First restore/create a clean Track A probe using generalized FolderLayout + the already-proven RenderTransform interaction baseline and finish the two missing generalized cases:

1. non-uniform L6 -> L9 native drag;
2. normal transformed click after nested parent collapse.

Once those are green, freeze Track A evidence and then resume Track B as a separate probe.
