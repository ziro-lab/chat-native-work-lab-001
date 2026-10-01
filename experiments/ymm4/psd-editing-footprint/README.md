# YMM4 PSD editing footprint

## Question

What resource-lifetime and caching behavior does built-in YMM4 PSD tachie use that matters when designing a PSD tachie plugin for low editing-time RAM/VRAM footprint?

## Observed on

- YukkuriMovieMaker4 Lite v4.56.1.0
- inspected 2026-10-01
- official archive SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

## Evidence

Static managed-code observation produced by:

- workflow run: https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/36799342737
- source HEAD: `096b250a5eb4f692d539d6107e7540f0530f1d21`
- artifact: `ymm4-psd-notation-surface`
- artifact digest: `sha256:dcbae999e973b3cea4e2eb29db0dc47c147959ad764fbb669de77ac71c9cd365`

This record reuses the exact-version static dump for a different downstream question. It does not widen the original runtime-notation PASS boundary.

## Observations

### 1. PSD file lifetime is owned by each built-in `PsdTachieSource`

`PsdTachieSource.Update` creates `new PsdParser.PsdFile(filePath)` on first use or when the PSD file path changes.

When the path changes, the previous `PsdFile` is removed from the source's disposer and disposed before the replacement is created.

`Clear` also disposes the current `PsdFile`.

No process-global/shared PSD-document cache is visible in `PsdTachieSource` itself.

**Boundary:** the number of simultaneously existing `PsdTachieSource` instances created by the host has not yet been established. Therefore exact duplicate-file-memory multiplicity is OPEN.

### 2. Layer selection resolution is cached

The source stores:

- `lastEnableLayersInput`
- `lastEnableLayerPathsInput`
- `resolvedEnableLayers`

If the layer inputs are the same object references as the previous update, resolution is reused rather than recomputed.

Resolved eye/mouth/vowel animation settings are also cached and refreshed only when the settings objects change.

### 3. Final composition is regenerated only when the effective active-layer set changes

After animation is applied, `PsdTachieSource.Update` compares the new active layer list against the previous `activeLayers` using `SequenceEqual`.

If the file path and active layer set are unchanged, it reuses the existing final bitmap.

If the effective layer set changes:

1. the old final bitmap is disposed;
2. the PSD root's active layers are updated;
3. `PsdFileSourcePlugin.CreateBitmap` recomposites the active layers into a new bitmap;
4. the new final bitmap is retained by the source.

This means ordinary playback frames without a visible PSD-state change do not continually rebuild the PSD image.

### 4. Layer pixels are decoded lazily on first use

`PsdItem.get_LayerImageBuffer` creates `LayerImageBuffer` only when the layer buffer is first requested.

The `LayerImageBuffer` constructor decodes that layer through `PsdParser.LayerImage.Read` and stores premultiplied RGBA bytes in its `cache` field.

Masks use a similar lazy `LayerMaskBuffer` path.

### 5. Disabled layer decoded buffers are discarded after composition

At the end of `PsdFileSourcePlugin.CreateBitmap`, `ClearCache(root, true)` recursively clears `imageBuffer` / `maskBuffer` for layers that are not enabled under the active hierarchy.

Enabled layers retain their decoded CPU buffers; inactive layers do not remain permanently expanded.

This is a strong low-memory behavior: many available expression layers do not imply that every layer remains decoded to RGBA.

### 6. Per-layer GPU bitmaps are transient, not retained in `LayerImageBuffer`

`LayerImageBuffer.ToD2DBitmap` calls `ID2D1DeviceContext.CreateBitmap` from the cached CPU RGBA bytes each time it is used for a composition.

`LayerImageBuffer` stores CPU bytes, not an `ID2D1Bitmap`.

The draw path disposes temporary Direct2D objects as composition proceeds. The source retains the composed final bitmap rather than a GPU bitmap for every PSD layer.

### 7. Direct2D is used for the actual composition

The built-in draw path uses Direct2D bitmaps/effects for masks, clipping, opacity, transforms, composite modes, and the final canvas.

Therefore the built-in design is approximately:

`PSD compressed/file data -> lazy CPU layer decode -> transient GPU layer bitmap/effects -> persistent current composed GPU bitmap`.

It is not a model where all PSD layers are permanently uploaded to VRAM.

### 8. Clear/Dispose explicitly releases GPU and PSD resources

`PsdTachieSource.Clear` disposes the current PSD file and current final bitmap and replaces the bitmap with a tiny empty bitmap.

`Dispose` disconnects the centering effect input and disposes the source's `DisposeCollector`.

## Design implications for a low-footprint replacement

The built-in YMM4 posture is a good baseline and should not be replaced by an unbounded “cache every expression” design.

Prefer:

- lazy decode of only layers that are actually used;
- current-state composition cache rather than a large final-expression history;
- transient per-layer GPU resources;
- immediate/bounded release of inactive decoded layer buffers;
- explicit disposal on source clear/path change/device lifecycle;
- no thumbnail/preset eager loading when the palette is closed.

A replacement may additionally consider sharing immutable parsed PSD/file data across multiple source instances, but only after host source multiplicity and thread/device safety are verified.

## OPEN / next high-value observation

1. How many `ITachieSource` instances YMM4 keeps alive for a typical editing session with multiple tachie/voice/face items.
2. Whether repeated references to the same PSD cause meaningful duplicate source-owned PSD memory in practice.
3. Actual RAM/VRAM footprint on a representative real PSD under:
   - one visible character;
   - multiple characters;
   - rapid expression switching;
   - mouth/eye animation;
   - palette open vs closed.

Those measurements should be used to set cache limits. A faster benchmark is not the goal; stable low editing-time footprint is.
