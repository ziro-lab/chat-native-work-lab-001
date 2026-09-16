# VideoItem archive planning surface

## Question

On YMM4 v4.56.1.0, can a loaded Plugin discover the native `VideoItem` state needed to plan a non-destructive recording archive: source path, source offset, Timeline frame/length, and playback rate?

## Status

- Date: 2026-09-16
- YMM4 version: **v4.56.1.0 Lite**
- Evidence type: native automated observation
- Current state: experiment prepared; PASS requires the GitHub Actions native-host assertions.

## Host identity

- Official source: `manju-summoner/YukkuriMovieMaker4` release `v4.56.1.0`
- Asset: `YukkuriMovieMaker_v4.56.1.0_Lite.zip`
- Expected SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

## Assertions

The probe runs inside the real YMM4 process and asserts that the live `VideoItem` type exposes discoverable instance properties named:

- `FilePath`
- `ContentOffset`
- `Frame`
- `Length`
- `PlaybackRate`

For each property it records:

- property type;
- declaring type;
- getter/setter visibility;
- default value when a safe native instance can be constructed;
- nearby VideoItem members related to file/time/frame/rate/source semantics.

The experiment intentionally does **not** assume that all five properties are declared directly on `VideoItem`; inherited state is valid.

## PASS boundary

A PASS proves only that, on the pinned YMM4 build, a Plugin loaded in the real host can identify the native VideoItem state required as inputs to a recording-archive Source Range Planner.

It also records whether those members are public or require a narrow compatibility adapter.

## NOT PROVEN

This experiment does not prove:

- the exact mathematical mapping from Timeline duration to source duration;
- rounding semantics for 50% / 100% / 200% playback;
- that changing these properties and saving a Project preserves rendered media identity;
- scene enumeration or SceneItem dependency semantics;
- archive Project copy/save/reload behavior;
- future YMM4 compatibility.

Those are separate experiments.

## Reproduction

Run `.github/workflows/ymm4-videoitem-archive-surface.yml` or push/PR a change under this experiment.

The workflow downloads the exact official YMM4 asset, verifies its SHA256, builds the probe, installs it only into the temporary runner host, launches YMM4 and waits for machine-readable evidence.

## Evidence

Filled after a successful run:

- Source commit:
- Workflow run:
- Artifact:
- Digest/hash:

## Downstream impact

- Consumer: `ziro-lab/ymm4-plugin-garage/plugins/recording-archive`
- Decision supported: choose the smallest YMM4 adapter needed to snapshot VideoItem source/timing state before implementing the pure Source Range Planner.

## Revalidation notes

Re-run when YMM4 changes VideoItem/project timing behavior or when the recording-archive plugin raises its pinned host version.
