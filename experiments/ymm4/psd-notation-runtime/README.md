# YMM4 PSD notation runtime probe

## Goal

On exact YukkuriMovieMaker4 Lite v4.56.1.0, run a synthetic PSD through the built-in PSD parser/file-source path and observe whether PSDTool-style layer-name markers change runtime state:

- leading `*`
- leading `!`
- `:flipx`
- `:flipy`
- `:flipxy`

This follows the static `psd-notation-surface` experiment, which found no obvious managed notation parser.

## Fixture

The probe creates a private synthetic PSD at runtime. It contains no user asset.

Control layers:

- `PlainVisible` — encoded visible
- `PlainHidden` — encoded hidden

Notation cases:

- `*StarA` — encoded visible
- `*StarB` — encoded visible
- `!BangHidden` — encoded hidden
- `Pose` — encoded visible
- `Pose:flipx` — encoded hidden
- `Pose:flipy` — encoded hidden
- `Pose:flipxy` — encoded hidden

All are root siblings so group construction cannot explain a difference.

## Assertions

The runtime callback executes inside real YMM4.

The first evidence boundary is the built-in `PsdParser.PsdFile -> YukkuriMovieMaker.Plugin.FileSource.Psd.PsdFolder.Parse` path.

Controls must prove that the fixture's visible/hidden flag is interpreted correctly:

- `PlainVisible == enabled`
- `PlainHidden == disabled`

Then the probe records whether:

- both `*StarA` and `*StarB` remain enabled or are automatically made exclusive;
- `!BangHidden` remains disabled or is automatically forced visible;
- flip suffix names remain literal in the built-in file-source model.

The probe also attempts to instantiate the built-in PSD layer editor ViewModel and records its constructor surface and resulting item state when possible.

## PASS means

PASS means the synthetic PSD was accepted by the exact host's built-in parser/file-source path and the control visibility states were interpreted correctly, so notation-case observations are meaningful at that boundary.

PASS does not mean a marker is supported. Marker support/non-support is an observation within the result.

## NOT PROVEN

Unless the editor ViewModel path is successfully loaded, the file-source result alone does not prove what the final interactive WPF control may do after a user gesture.

This experiment also does not prove final rendered flip pixels. If `:flip*` does not alter the model, rendering confirmation may still be added if a plausible downstream transform path is found.

## Environment

- YMM4 Lite v4.56.1.0
- official release archive SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Windows GitHub Actions runner
- .NET 10

No YMM4 binary or user PSD is committed.


## Observed result — 2026-10-01

Native runtime evidence:

- workflow run: https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/36796938896
- source HEAD: `e143059b16cee829302578c5755480e17fcc6602`
- YMM4 Lite v4.56.1.0 archive SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

The workflow, build, plugin installation, real YMM4 launch, synthetic PSD parse, built-in PSD editor ViewModel load, command exercise, provenance capture, and evidence upload all completed successfully.

### File-source observations

Control assertions passed:

- `PlainVisible` was enabled.
- `PlainHidden` was disabled.

Therefore the fixture's PSD visibility flags were interpreted correctly.

Notation cases:

- `*StarA` and `*StarB` were both enabled simultaneously.
- `!BangHidden` remained disabled, exactly like an ordinary hidden layer.
- `Pose:flipx`, `Pose:flipy`, and `Pose:flipxy` survived as literal layer names.
- The normal `Pose` layer and all flip-suffixed siblings remained separate ordinary PSD layers.

### Built-in PSD editor ViewModel observations

The actual `PsdLayerEditorViewModel` loaded successfully from the synthetic PSD.

Every tested layer, including marker-prefixed/suffixed names, exposed the same generic command surface:

- Toggle enable: executable
- Switch sibling layer: executable
- Shift: unavailable for root siblings in this fixture

Command exercise:

1. Toggle `*StarA` on -> StarA=true, StarB=false.
2. Toggle `*StarB` on -> **StarA=true, StarB=true**.
   - Leading `*` did not automatically impose radio/exclusive behavior.
3. Toggle `!BangHidden` on -> true.
4. Toggle `!BangHidden` again -> **false**.
   - Leading `!` did not force visibility or prevent disabling.
5. Enable an unrelated ordinary sibling, then execute the built-in `SwitchLayerCommand` on `*StarB`.
   - StarA=false, StarB=true, ordinary sibling=false.
   - This proves YMM4 has a **generic sibling switch/radio-like operation**, but it applies by command choice rather than by the `*` layer-name marker.

### Combined interpretation with the static experiment

For YMM4 v4.56.1.0 built-in PSD tachie:

- Generic checkbox-like layer enable/disable exists.
- Generic sibling-exclusive switching exists.
- PSDTool leading `*` is **not interpreted as an automatic radio-layer name convention** in the tested built-in path.
- PSDTool leading `!` is **not interpreted as a force-visible layer name convention** in the tested built-in path.
- `:flipx`, `:flipy`, and `:flipxy` remain literal names; no managed flip-name parser or dedicated flip state was found in the paired static scan.

The final rendered pixels for `:flip*` were not separately captured. However, the literal runtime state plus the static scan's absence of any flip-token path provides strong evidence that the built-in PSD implementation does not implement PSDTool flip suffix semantics.

This does not make a claim about older YMM4 versions or third-party PSD tachie plugins.

### PSDTool source comparison

PSDTool commit `5f40b67da531db5e689831aba71bd438d5ee0ae0` treats the special layer-name conventions explicitly:

- leading `!`: force-visible, disabled checkbox;
- leading `*`: radio input grouped by parent;
- suffix tokens `:flipx`, `:flipy`, `:flipxy`: paired flip groups.

Those explicit name-driven rules were not observed in YMM4's built-in v4.56.1.0 PSD path.
