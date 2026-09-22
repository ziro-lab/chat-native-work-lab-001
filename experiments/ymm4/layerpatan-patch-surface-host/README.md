# LayerPatan Harmony patch-surface audit

## Question

After the no-Harmony fold experiment showed that visual-only compression is insufficient, audit the current LayerPatan Harmony surface and ask a narrower question:

> Is any current Harmony patch actually unnecessary under the existing LayerPatan product behavior?

This audit is **not** a request to redesign LayerPatan. It evaluates the current architecture and distinguishes:

- core patches whose behavior has direct evidence;
- display/behavior fidelity patches that are optional by design;
- candidates worth replacing through a lower-risk surface;
- the broad IL scan itself.

Reference implementation inspected:

- repository: `bluemistel/YMM4-LayerPatan`
- branch: `main`
- inspected implementation contains Harmony 2.4.2 and the current required/optional split in `PatchInstaller.cs`.

## Native patch-surface evidence

Reference workflow run: **35613845709**

### YMM4 4.55.1.1 Lite

- artifact: **10645321915**
- artifact SHA256: `b626d6fdf3587553eb0c11b7d78fd22b43712a5d574e25afbc2aa544b8255f90`

### YMM4 4.56.1.0 Lite

- artifact: **10645875260**
- artifact SHA256: `2395a1f88564a0007f8546b5118e22038197615b8c8d3d98ec976d837a908cf7`

Both in-host probes returned `PASS_LAYERPATAN_PATCH_SURFACE`.

## Broad LayerHeight transpiler is smaller than its code shape suggests

LayerPatan scans all methods of `MainViewModel` and `TimelineViewModel` (including nested compiler-generated types) for exact IL patterns equivalent to:

- `y / LayerHeight -> int layer`
- `layer * LayerHeight -> y`

On both tested YMM4 versions the pattern matched exactly **11 sites**.

Common semantic surface:

| Root | Method | Direction / sites |
| --- | --- | ---: |
| MainViewModel | `GetCurrentLayer` | y→layer ×1 |
| MainViewModel | `CreateCommandBindings` compiler lambda ×6 | y→layer ×6 |
| TimelineViewModel | `GetCurrentLayer` | y→layer ×1 |
| TimelineViewModel | `ScrollLayer` | layer→y ×1 |
| TimelineViewModel ctor display-class lambda | y→layer ×2 |
| **Total** | | **11** |

The two versions have the same count and structure.

The only observed name drift was in compiler-generated MainViewModel lambdas:

- 4.55.1.1: `...b__135_429`, `...b__135_430`
- 4.56.1.0: `...b__135_431`, `...b__135_432`

### Implication

Replacing the current pattern scan with an explicit list of method names would **reduce** update tolerance, because compiler-generated lambda names already changed between two supported versions.

The current pattern matching is therefore not equivalent to "blindly patch everything". It searches broadly but modifies only 11 exact LayerHeight arithmetic sites on the tested versions.

## Target visibility

The in-host probe observed the following current targets on both versions:

| LayerPatan patch target | Host visibility |
| --- | --- |
| Timeline item `Top` setter | private |
| Timeline item `Height` setter | private |
| `UpdateItemHeight` | private |
| Layer-label `Top/Height` setters | private |
| Layer-line `Top/Height` setters | private |
| `ComputeMaxLayerCount` local function | internal/compiler local |
| `GetDeltaFrameAndLayer` | private |
| `TimelineItemView.OnMouseMove` | protected |
| `AddItemCommandParameterConverterBase.GetTimelinePosition` | protected |
| `TimelineItemSpaceViewModel.Top/Height` getters | public |
| `TimelineViewModel.ScrollToItem` | public |
| `TimelineItemBackgroundViewModel.Top/Height` getters | public |

A public getter does not provide a supported way to replace the returned geometry. The public `ScrollToItem` target is an exception in visibility, but LayerPatan's correction still needs the active Viewport, which currently has no proven plugin-facing vertical viewport setter and is reached through bounded reflection.

## Patch-by-patch disposition

### Core / keep

#### 1. Timeline item position + collapsed-item height

**Keep.**

This is the current display model itself:

- compress visible item rows;
- map items inside collapsed folders onto the folder row;
- preserve the thin timing strip for hidden contents;
- re-run private `UpdateItemHeight` when an item changes layer.

The earlier no-Harmony experiment proved that WPF item transforms can move a rendered item, but that does not supply the full virtualized Timeline layout, collapsed height lifecycle, labels, lines, spaces and host geometry.

Removing this patch while retaining the current product behavior would require a redesign, not a simplification.

#### 2. Layer-label position/height

**Keep.**

The product promise is that the **native Timeline itself** collapses. Labels have private setters and need the same logical-row mapping as items.

Moving only items is insufficient and produces a mismatched layer column.

#### 3. Layer divider position/height

**Keep.**

Same coordinate model as layer labels. Removing it leaves native row chrome at uncompressed logical positions.

#### 4. Max-layer row count correction

**Keep.**

Compressed rows mean the host must still create enough logical layer rows to reach lower layers. This patch is part of the current virtualized-row strategy, not redundant decoration.

#### 5. Native item vertical drag correction

**Keep — directly proven.**

The no-Harmony native run observed:

```
displayed one-row drag from logical L1
expected folded target = L3
actual without remap = L2
```

So standard item drag does not infer the compressed logical layer from transformed visuals.

#### 6. Standard add-position correction

**Keep — directly proven.**

A blank right-click on the displayed L3 row was interpreted by the host as L2, and `AddItemCommandParameterConverterBase.GetTimelinePosition` returned L2.

Observed derived converters include file, template, character, voice and generic item additions, so this is a shared add-position seam rather than a one-off menu fix.

#### 7. Generic LayerHeight coordinate transpiler

**Keep, but strengthen its guard.**

The no-Harmony probe directly showed at least one behavior handled outside the dedicated drag/add patches:

- Shift-drag rectangular selection works on the untransformed baseline;
- the same selection rectangle around a visually shifted L3 item does not select it.

The in-host surface probe then showed the broad scan patches only 11 exact IL sites on both supported versions.

The best change is **not to remove this transpiler** and **not to hard-code compiler-generated method names**.

Recommended hardening:

- record the matched method/site manifest;
- for known supported YMM4 versions, require the expected semantic shape / site count (currently 11);
- if the pattern count or shape changes unexpectedly, fail closed and disable folding just like other required patches;
- include the manifest in `LayerPatan.log` for issue reports.

This reduces silent partial coverage without sacrificing the useful pattern-based version tolerance.

### Optional / keep unless intentionally accepting degraded fidelity

#### 8. Timeline item-space geometry

**Already correctly classified as optional.**

It only adjusts the rendered gap/space geometry around items. It is a reasonable patch to omit if the product explicitly accepts visual misalignment, but then the Timeline is no longer visually equivalent to the compressed layout.

No evidence here justifies calling it dead or redundant.

#### 9. `ScrollToItem` vertical correction

**Already correctly classified as optional.**

The target method itself is public, but the correction uses current vertical Viewport geometry. The Lab has no proven plugin-facing API for setting the native Timeline vertical viewport in compressed logical-row coordinates.

Therefore "public method" does not mean the Harmony correction can simply be deleted while preserving native scroll behavior.

This is the best candidate for a future lower-risk replacement experiment, but not for deletion today.

#### 10. Group-control background band

**Already correctly classified as optional.**

This is visual fidelity for a standard YMM4 feature that LayerPatan explicitly supports. Removing it would not necessarily corrupt item data, but the group band would no longer match compressed rows.

It is optional in failure policy, not unnecessary in UX.

## Final audit result

Under the current LayerPatan behavior contract, this audit found **no required Harmony patch that can be removed without either breaking a proven native interaction or redesigning the Timeline rendering model**.

The three existing optional patches are sensibly optional; none is demonstrated dead code.

The highest-value change is instead:

> keep the existing patch architecture, but turn the generic 11-site LayerHeight scan into a version-auditable manifest with a fail-closed shape/count guard.

That gives a materially better safety story than deleting patches for the sake of lowering the raw Harmony count.

## Practical patch risk ranking

| Patch group | Recommendation | Reason |
| --- | --- | --- |
| Item Top/Height | Keep | core layout |
| Label Top/Height | Keep | core layout |
| Line Top/Height | Keep | core layout |
| Max row count | Keep | virtualized logical-row reach |
| Drag delta | Keep | native failure reproduced |
| Add position | Keep | native failure reproduced |
| LayerHeight transpiler | Keep + stronger guard | 11 stable exact sites; marquee failure reproduced |
| Item spaces | Optional keep | visual fidelity |
| ScrollToItem | Optional keep; future public/WPF probe candidate | behavioral fidelity; no proven public vertical viewport route |
| Group background band | Optional keep | visual fidelity |

## Scope

This audit covers YMM4 4.55.1.1 Lite and 4.56.1.0 Lite. It does not certify future YMM4 versions. The point of the proposed manifest guard is precisely to make future changes visible instead of silently assuming compatibility.
