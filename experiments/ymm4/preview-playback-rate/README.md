# Preview playback rate beyond 8x

## Question

Is the apparent 8x preview-speed limit in YMM4 an actual playback-engine limit, or mainly a UI/interaction limit?

## Status

**Baseline observation recorded; public-Lab native UI-structure proof PASS; full playback-path reproduction still pending.**

- Date: 2026-09-16
- YMM4 version inspected: **v4.56.1.0**
- Evidence types: static inspection + manual live observation + public-Lab real-host UI inspection
- Public-Lab UI proof run: `35100142088`
- UI proof artifact: `ymm4-preview-playback-rate-ui`, artifact ID `10447194016`, SHA256 `c5ad43cadd78648a3880a12e44c597b7bd0e0bce2ebc3961f520ac8ca7e46c0d`

## Host identity

YMM4 Lite v4.56.1.0 was inspected using the exact official release asset.

- SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

## Observations

### Static inspection

Observed on YMM4 v4.56.1.0:

1. `PreviewViewModel.UpdatePlayerPlaybackRate()` computes the player rate from the setting using the equivalent of:

   ```text
   (YMMSettings.PlaybackRate + 1) * 25%
   ```

2. `YMMSettings.PlaybackRate` setter did not contain an 8x upper clamp in the inspected build.
3. The playback-rate-up command increments the setting by one step rather than clamping at the setting value corresponding to 8x.
4. The relevant command CanExecute path did not contain an 8x guard in the inspected build.
5. The inspected audio PlaybackRate setters did not contain an 8x upper clamp.
6. Provider construction for the native resampling/time-stretch playback path accepted rates above 8x in the prior probe. A synthetic standalone reset path had an unrelated missing-runtime-state failure at 8x, 16x and 32x alike, so that failure was not evidence of a >8x-specific limit.

### Public-Lab native UI inspection

A real YMM4 v4.56.1.0 Lite process was launched on `windows-latest`, and a temporary probe inspected the live WPF visual tree.

The playback-rate selector was identified as:

```text
type=System.Windows.Controls.ComboBox
data_context=YukkuriMovieMaker.ViewModels.PreviewViewModel
item_count=32
items_source=<null>
selected_index_binding=PlaybackRate
first=ComboBoxItem(content=x 0.25, visibility=Visible)
last=ComboBoxItem(content=x 8.0, visibility=Visible)
```

This establishes that, in the inspected build, the visible speed selector is an inline 32-item ComboBox whose `SelectedIndex` maps directly to `PlaybackRate`. The visible x8 ceiling is therefore represented by the selector's item count, not by an `ItemsSource` collection or a separate selected-value conversion layer.

### Manual live observation

In an interactive YMM4 session, continuing the normal playback-speed-up operation after the UI-visible 8x point made playback **clearly faster than 8x**, while audio continued to follow the accelerated preview.

The user also observed that the high-speed preview remained perceptually much smoother than expected for such a large rate increase.

## Current interpretation

For YMM4 v4.56.1.0, the available evidence indicates that **8x is not an upper clamp in the inspected playback-rate setting/command/audio propagation path**.

The live UI proof also shows a concrete UI-side boundary: the standard selector contains exactly 32 inline items from x0.25 through x8.0 and binds its selected index directly to `PlaybackRate`.

## PASS boundary

The current evidence supports these narrow claims for YMM4 v4.56.1.0:

- the inspected PlaybackRate setting/command path is not statically clamped to 8x;
- the inspected audio rate setters are not statically clamped to 8x;
- a real interactive session can run clearly faster than 8x with audio still following;
- the real-host standard speed selector is an inline 32-item ComboBox;
- its `SelectedIndex` is bound directly to `PlaybackRate`;
- its first/last visible entries are `x 0.25` and `x 8.0`.

## NOT PROVEN

This record does not yet prove:

- that 16x or 32x is stable on every project, codec, effect stack or machine;
- that every audio mode remains intelligible or artifact-free at 16x/32x;
- that YMM4 guarantees this behavior as a supported public API;
- that future YMM4 versions keep the same internals or UI structure;
- that a particular downstream plugin implementation remains compatible with every YMM4 layout/theme/version;
- that 32x should be the final practical upper limit;
- a public-Lab automated runtime proof of the entire playback/audio path at 16x/32x, until the earlier playback probes are migrated here.

## Next public-Lab experiment

Migrate the remaining minimal playback-rate probes into this repository so the public record also contains automated evidence for the setting/command/audio path at 8x / 16x / 32x, separate from the already-completed live UI-structure proof.

## Downstream impact

This observation is the basis for the Garage candidate:

- [`YMM4 プレビュー再生速度拡張プラグイン`](https://github.com/ziro-lab/ymm4-plugin-garage/tree/main/plugins/preview-speed-extension)

The intended plugin should **reuse YMM4's native playback-rate path** and only make high rates easier to select. It should not implement its own high-speed video/audio engine unless a later Lab experiment proves that native behavior is insufficient.
