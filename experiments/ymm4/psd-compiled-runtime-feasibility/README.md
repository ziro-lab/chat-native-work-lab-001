# Compiled PSD runtime feasibility for YMM4

## Question

Can a PSD tachie plugin compile a source PSD into a lighter editing-time representation without losing the information used by YMM4 v4.56.1.0's built-in PSD renderer?

The goal is not to beat another plugin's loading benchmark. The goal is low and stable editing-time RAM/VRAM use while preserving visual output and responsive expression/eye/mouth updates.

## Evidence basis

### Exact YMM4 static observation

- YukkuriMovieMaker4 Lite v4.56.1.0
- official archive SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- resource/draw-path evidence run:
  https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/36799342737
- source HEAD:
  `096b250a5eb4f692d539d6107e7540f0530f1d21`

### Parser source

YMM4's bundled parser is the public MIT-licensed repository:

- `manju-summoner/PsdParser`
- inspected commit:
  `ff3aee18a95e5fb6e868585a5b0ad15f46decd89`

## Finding: the information needed for a lossless runtime representation is available

The public PsdParser surface exposes the source information YMM4's built-in renderer consumes.

### Document metadata

`PsdFile.Header` exposes:

- PSD/PSB version
- channel count
- canvas width / height
- bit depth
- color mode

### Per-layer structural and draw metadata

`LayerRecord` exposes:

- `Top / Left / Bottom / Right`
- channel descriptors
- `BlendMode`
- `Opacity`
- `Clipping`
- `LayerFlags` (including original visibility)
- `LayerName`
- layer mask/adjustment data
- additional layer information

The original layer name is available unchanged, so PSDTool-style conventions can be compiled separately without changing the PSD.

### Folder/group structure

`AdditionalLayerInformations.SectionDividerSetting` exposes:

- folder boundary/type
- group blend mode
- subtype

The built-in YMM4 path uses the section-divider blend mode for a folder when present, otherwise the layer-record blend mode.

Therefore a compiled manifest can preserve the same hierarchy and group blend semantics.

### Pixel data

`LayerRecordAndImage.Image` is a public `LayerImage`.

`LayerImage.Read()` produces the layer-local pixel buffer. It is already bounded to the layer's own `Left/Top/Right/Bottom` rectangle rather than a full-canvas transparent image.

Important consequence: **cropping transparent full-canvas layers is not a new optimization over current YMM4 for ordinary raster layers; YMM4 already operates on the layer record rectangle.**

The built-in `LayerImageBuffer` then premultiplies alpha and retains the CPU buffer lazily while that layer remains needed.

A compiled runtime format can store the equivalent canonical premultiplied pixel block directly and skip PSD channel/RLE decoding during ordinary editing.

### Masks

`LayerMaskAndAdjustmentLayerData` publicly exposes:

- mask rectangle
- default color
- flags
- mask parameters/density/feather metadata
- real mask metadata/rectangle

`LayerImage.ChannelImages` exposes the mask channel data and `ChannelImageData.ReadLine` can decode it.

The built-in YMM4 renderer creates a mask buffer from the mask channel and uses the mask rectangle and default color during Direct2D composition.

Therefore the compiled representation can preserve mask semantics without retaining the PSD file as the active runtime container.

### Blend modes / opacity / clipping

The YMM4 v4.56.1.0 draw path reads:

- per-layer blend mode
- per-layer opacity
- group blend mode
- group opacity
- clipping flag
- masks and mask offsets

and maps PSD blend modes through Direct2D blend/composite operations.

The inspected path contains cases for the PsdParser blend-mode set, including pass-through groups and the standard Photoshop blend families.

These values can be stored as compact enum/scalar fields in the compiled manifest.

## What can be replaced with a lighter runtime representation

After source compilation, ordinary editing does **not** require the original PSD container for the renderer if the compiled asset contains:

### Manifest

Per document:

- source identity/fingerprint
- canvas size / source format metadata
- compiler/schema version
- layer tree and stable compiled IDs
- original PSD layer names
- PSDTool parsed semantics
- default visibility

Per node:

- type (folder/layer)
- parent/order
- rectangle
- blend mode
- opacity
- clipping
- mask metadata
- pixel-block reference
- mask-block reference

### Pixel store

For each raster layer:

- exact layer-local dimensions / origin
- lossless premultiplied BGRA pixel data
- lossless mask data when present

The storage encoding can be optimized independently from rendering. It should be lossless and block-addressable so only required layers are materialized.

## Recommended editing-time runtime

```text
source.psd
    │
    ├── only on first import / source change
    ▼
PSD compiler
    │
    ├── manifest (small, always resident)
    └── compressed layer/mask blocks (disk-backed)
            │
            ▼ on demand
SharedCompiledDocument
    │
    ├── active CPU layer buffers only
    └── immutable structural metadata shared by all sources
            │
            ▼ transient upload
Direct2D composition
            │
            └── current composed GPU bitmap per active source
```

This preserves the useful low-footprint behaviors already present in YMM4:

- lazy layer decode;
- no recomposition when effective active layers do not change;
- transient per-layer GPU images;
- one current composed image rather than an unbounded expression-image cache;
- release of inactive decoded buffers.

It additionally makes these improvements possible:

- do not keep the entire compressed PSD byte stream resident during editing;
- share immutable compiled source data across multiple tachie sources;
- load individual layer blocks from a disk-backed cache;
- parse PSDTool notation once at compilation instead of repeatedly;
- invalidate/recompile only when the source PSD changes.

## Important non-improvement

Do not claim a saving from converting every PSD layer from “full canvas” to a crop unless the specific PSD actually encodes such a large layer rectangle.

YMM4 already uses each PSD layer's own record rectangle and `LayerImage.Read()` dimensions. The real gains are container removal, canonical predecode, sharing, and bounded cache lifetime.

## What should stay dynamic

Do not pre-render every expression.

The dynamic state should remain cheap metadata:

- active layer IDs
- expression stacking/inheritance state
- Extend/Override state
- eye animation state
- mouth animation state

Only a change in the effective visible layer set should trigger recomposition.

## What cannot safely be flattened unconditionally

Static subtree precomposition is optional, not a baseline requirement.

A group must not be flattened merely because its layers look static when:

- pass-through semantics can affect composition with outside siblings;
- clipping crosses the proposed boundary;
- masks/group opacity/blend depend on outside composition;
- the user may change any descendant through a palette/preset/face diff.

Lossless subtree flattening should require a proven closed composition boundary and immutable descendants.

## Public vs internal dependency

The source data required for compilation is available from the public PsdParser types.

The YMM4 built-in `PsdFolder`, `PsdItem`, `LayerImageBuffer`, and `PsdFileSourcePlugin` implementation details are useful as a behavioral reference, but the compiled format does not need to persist or depend on those internal runtime objects.

For maintainability, prefer:

`public PsdParser source data -> own versioned compiled manifest -> public YMM4 tachie/Direct2D plugin surface`

over binding the product to YMM4's private PSD implementation classes.

## OPEN validation

Before implementation is considered visually equivalent:

1. Build a tiny compiled renderer with the same blend/mask/clipping rules.
2. Compare YMM4 built-in PSD output vs compiled output pixel-for-pixel or with a zero/strict tolerance for a corpus covering:
   - all used blend modes;
   - folder pass-through vs normal;
   - group opacity;
   - clipping chains;
   - layer masks with both default colors;
   - nested groups;
   - PSDTool flip pairs.
3. Verify live eye/mouth/expression updates produce the correct active-layer state.
4. Verify source-file changes invalidate the compiled asset atomically.
5. Verify cache eviction does not cause unbounded RAM/VRAM growth.

A representative real user PSD is useful at this final acceptance stage, not as the design starting point.

## Conclusion

**Feasible.** YMM4/PsdParser exposes enough source information to build a lossless, lighter editing-time compiled PSD representation without treating the original PSD as the live runtime asset.

The highest-value optimization is not image-quality reduction or eager GPU caching. It is:

`parse once -> store exact canonical layer data -> disk-backed/on-demand materialization -> share immutable source data -> compose only on effective state changes`.
