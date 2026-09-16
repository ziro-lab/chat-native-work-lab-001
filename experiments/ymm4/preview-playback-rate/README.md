# Preview playback rate beyond 8x

## Question

Is the apparent 8x preview-speed limit in YMM4 an actual playback-engine limit, or mainly a UI/interaction limit?

## Status

**Baseline observation recorded; public Lab-native reproduction still to be migrated.**

- Date: 2026-09-16
- YMM4 version inspected: **v4.56.1.0**
- Evidence types available from the prior investigation: static inspection + manual live observation
- Native automated evidence existed in the prior validation workspace, but it has not yet been reproduced from this public Lab repository. Therefore this record does not claim a public-Lab automated PASS yet.

## Host identity

YMM4 Lite v4.56.1.0 was inspected using the exact official release asset in the prior investigation.

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

### Manual live observation

In an interactive YMM4 session, continuing the normal playback-speed-up operation after the UI-visible 8x point made playback **clearly faster than 8x**, while audio continued to follow the accelerated preview.

The user also observed that the high-speed preview remained perceptually much smoother than expected for such a large rate increase.

## Current interpretation

For YMM4 v4.56.1.0, the available evidence indicates that **8x is not an upper clamp in the inspected playback-rate setting/command/audio propagation path**.

The working model is that the apparent 8x ceiling is primarily an exposed UI/interaction boundary rather than a hard playback-engine boundary.

## PASS boundary

The current baseline supports these narrow claims for the inspected build:

- the inspected PlaybackRate setting/command path is not statically clamped to 8x;
- the inspected audio rate setters are not statically clamped to 8x;
- a real interactive session can run clearly faster than 8x with audio still following.

## NOT PROVEN

This record does not yet prove:

- that 16x or 32x is stable on every project, codec, effect stack or machine;
- that every audio mode remains intelligible or artifact-free at 16x/32x;
- that YMM4 guarantees this behavior as a supported public API;
- that future YMM4 versions keep the same internals;
- that the standard speed selector can be extended cleanly by a plugin without Reflection or UI integration work;
- that 32x should be the final practical upper limit;
- a public-Lab-native automated PASS, until the prior probes are migrated/re-run here.

## Next public-Lab experiment

Migrate a minimal playback-rate probe into this repository and run it against an exact official YMM4 release so that this public record contains:

1. exact host download + SHA256 verification;
2. static PlaybackRate/command/audio inspection;
3. a native-host or closest-safe runtime assertion for 8x / 16x / 32x;
4. preserved probe output as an Actions artifact;
5. explicit separation between automated results and perceptual/manual smoothness checks.

## Downstream impact

This observation is the basis for the initial Garage candidate:

- [`YMM4 プレビュー再生速度拡張プラグイン`](https://github.com/ziro-lab/ymm4-plugin-garage/tree/main/plugins/preview-speed-extension)

The intended plugin should **reuse YMM4's native playback-rate path** and only make high rates easier to select. It should not implement its own high-speed video/audio engine unless a later Lab experiment proves that native behavior is insufficient.
