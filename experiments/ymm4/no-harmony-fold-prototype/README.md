# YMM4 no-Harmony fold prototype

## Goal

Test whether a LayerPatan-style compressed layer layout can preserve YMM4's standard Timeline interaction semantics **without Harmony**.

The experiment intentionally starts with the smallest decisive case:

- logical Layer 1 is the visible head of a collapsed block representing Layers 1-2;
- logical Layer 3 is moved upward visually by one layer-height using only WPF `RenderTransform`;
- the visual result therefore places Layer 3 one displayed row below Layer 1.

Then real OS mouse input checks two things:

1. whether the transformed Layer 3 item is still hit-testable/selectable as Layer 3;
2. whether dragging the Layer 1 item by one displayed row moves it to logical Layer 3, as compressed-layout semantics require, or only to Layer 2 under YMM4's native fixed-row geometry.

No Harmony package or runtime patching is used.

## Why this is decisive

A visual-only fold can be useful only if YMM4's standard interaction path understands the same non-linear mapping:

```
display row 1 -> logical Layer 1
display row 2 -> logical Layer 3
```

If a native drag by one displayed row still produces `Layer += 1`, then WPF-only visual compression is insufficient for LayerPatan-equivalent behavior. A product would still need either:

- an internal coordinate remap hook; or
- replacement/custom implementations of the affected standard interactions.

## Environment

- YMM4: **4.55.1.1 Lite**
- Windows GitHub-hosted runner
- .NET 10
- exact release ZIP + SHA256 inherited from the other pinned YMM4 4.55.1.1 Lab probes

## Evidence

The probe writes:

- `result.txt` — semantic observations
- `geometry.txt` — before/after screen geometry and layer height
- `events.txt` — Timeline property and pointer-route trace
- `error.txt` — exception details if the probe fails

## PASS boundary

`PASS_NO_HARMONY_FOLD_OBSERVATION` means the experiment completed and produced an interpretable observation.

It does **not** require the drag to reach Layer 3. The point of the probe is to determine whether that happens.

A useful result should contain:

- `visual_transform_applied=True`
- `visual_gap_matches_one_row=True`
- `transformed_target_selected=True`
- `drag_actual_layer=...`
- `drag_matches_fold_semantics=...`
- `drag_matches_native_one_row=...`

## Scope boundary

This first probe does not implement a full folder UI, layer labels, range selection, file drop, or context-menu item insertion. It answers the earlier and narrower feasibility question first: **can a WPF-only visual fold preserve the native item hit-test and drag layer mapping?**
