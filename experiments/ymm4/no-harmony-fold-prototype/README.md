# YMM4 no-Harmony fold prototype

## Goal

Test whether a LayerPatan-style compressed layer layout can preserve YMM4's standard Timeline interaction semantics **without Harmony**.

The decisive model is intentionally tiny:

```
logical:   Layer 1 / Layer 2 / Layer 3
display:   Layer 1 /           Layer 3
```

Layer 3 is moved upward by one layer-height using only WPF `RenderTransform`.
No Harmony package or runtime patching is used.

The probe then checks real YMM4 4.55.1.1 behavior for:

- native item hit-testing / click selection;
- native item vertical drag;
- Shift+drag rectangular selection;
- right-click Timeline position used by standard add commands;
- the shared standard add-position converter hierarchy;
- an exploratory WPF/OLE FileDrop attempt.

## Environment

- YMM4: **4.55.1.1 Lite**
- Windows GitHub-hosted runner
- .NET 10
- exact release ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

## Reference runs

### Decisive pointer / add-position / marquee run

- workflow run: **35610695965**
- source head: `45c930196fb8ba5976c753530d60457a3250f710`
- result: `PASS_NO_HARMONY_FOLD_OBSERVATION`

Observed:

```
harmony_reference_present=False
visual_transform_applied=True
visual_gap_matches_one_row=True
transformed_target_selected=True

head_selected_before_drag=True
drag_actual_layer=2
drag_matches_fold_semantics=False
drag_matches_native_one_row=True

marquee_baseline_selected=True
marquee_transformed_selected=False

right_click_cursor_y=80.00
right_click_native_layer=2
right_click_matches_fold_layer=False
right_click_matches_native_row=True

add_position_converter_found=True
add_position_converter_layer=2
file_drop_converter_candidate_present=True
```

### Exploratory external-like FileDrop run

- workflow run: **35611313672**
- source head: `bd4447bb0467b63a2fb83dcf9d4cdcb51e7b5baf`
- artifact: **10644615095**
- artifact SHA256: `0afcfd44b50c37d5fcf601cc840b5be844fee4961b9b863b15bc455eb0a961a8`

The generated 1px PNG was accepted and created an `ImageItem`, but the synthetic in-process OLE drag added it at `Layer 0 / Frame 0` even when the cursor was moved to the intended Timeline target. Therefore that run proves FileDrop acceptance only; it is **not** treated as valid evidence for Explorer-equivalent drop-position semantics.

## Findings

### 1. Visual compression + item hit-testing can work without Harmony

Layer 3 was translated upward by exactly one 32px row:

```
Layer 1 before: y=560
Layer 3 before: y=624
Layer 3 after:  y=592
LayerHeight:     32
```

Clicking the visually moved Layer 3 item selected the logical Layer 3 item.

So a WPF visual transform does affect native hit-testing for the transformed item itself.

### 2. Native item drag does not follow compressed-row semantics

Dragging logical Layer 1 downward by one **displayed** row moved it to logical Layer 2, not logical Layer 3.

```
display row delta: +1
expected folded logical target: Layer 3
actual YMM4 target: Layer 2
```

This proves a visual-only fold is insufficient for native vertical item drag.

### 3. Native rectangular selection does not follow the transformed visual item

YMM4 rectangular selection is Shift+drag (historically also right-drag).

The exact same OS-injected Shift+drag gesture was validated first against an untransformed Layer 1 fixture:

```
marquee_baseline_selected=True
```

Repeating the gesture around the visually shifted Layer 3 fixture did not select it:

```
marquee_transformed_selected=False
```

So the failure is not just an automation failure; the baseline gesture works while the visually transformed target does not participate at its displayed geometry.

### 4. Right-click standard add position stays on the native row

A blank right-click on the displayed Layer 3 row produced:

```
right_click_cursor_y=80
LayerHeight=32
native row/layer = 2
```

The host's standard helper:

```
YukkuriMovieMaker.Views.Converters.AddItemCommandParameterConverterBase
    .GetTimelinePosition(object[])
```

also returned logical Layer 2 for the same point.

So a visual-only fold does not remap the standard add position to Layer 3.

### 5. File-item addition shares the same add-position converter family

The pinned host exposes these concrete subclasses of the same add-position base converter:

```
AddCharacterItemCommandParameterConverter
AddFileItemCommandParameterConverter
AddItemCommandParameterConverter
AddTemplateItemCommandParameterConverter
AddVoiceItemCommandParameterConverter
```

This is strong static/runtime evidence that file-item, template, character, voice and generic item additions share the same position-helper family.

The separate synthetic OLE FileDrop experiment is deliberately **not** used as positional proof because its in-process drag source caused the host to add the file at Layer 0 / Frame 0.

## Interpretation

The experiment does **not** prove that the Harmony library itself is mandatory.

It proves a narrower and more useful claim:

> WPF-only visual compression is not enough for LayerPatan-equivalent native interaction semantics.

At minimum, a no-Harmony implementation still needs equivalent adaptation for multiple independent input-to-layer paths:

- vertical item drag;
- rectangular selection;
- right-click / standard add-position mapping;
- likely other add paths sharing `GetTimelinePosition`.

That changes the engineering trade-off.

A hybrid design is technically possible:

```
display / transformed item hit-testing
    -> WPF

input-to-logical-layer mapping
    -> targeted host adaptation
```

But replacing every affected input path individually risks becoming more complex and more version-sensitive than one centralized coordinate-remap layer.

## Product implication for LayerPatan

The current Harmony approach is justified more strongly after this probe.

The useful optimization target is **not "zero Harmony"**. It is:

> keep Harmony/internal intervention only where the host must translate displayed rows into logical layers, and use lower-risk WPF/public surfaces where they genuinely work.

Before reducing the current patch set, test each candidate removal against the same real-host acceptance cases rather than assuming visual transforms preserve native semantics.

## Evidence files

The workflow stores:

- `result.txt`
- `geometry.txt`
- `converter.txt`
- `file-drop.txt`
- `events.txt`
- `error.txt` when present
- `host-log.txt`

## Scope boundary

This probe does not yet prove:

- layer-label / separator-line compression through WPF-only transforms;
- every YMM4 add/drop route;
- scroll-to-item behavior under a full folder implementation;
- other YMM4 versions;
- physical Explorer-process drag-and-drop equivalence.

Those should remain separate claims.
