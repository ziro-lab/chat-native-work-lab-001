# Project archive save/copy surface

## Question

On YMM4 v4.56.1.0, can a loaded Plugin discover the native Project/path/save surface needed to create an archive Project under a caller-chosen path without overwriting the original Project?

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

The probe runs inside the real YMM4 process and records Project/path/save/open/load-related fields, properties, commands and methods on:

- `MainViewModel`;
- its native model object;
- the active Timeline ViewModel;
- directly reachable Project-like objects.

PASS requires at least one discoverable save/copy/export/path candidate that can inform a follow-up behavioral save-as experiment.

## PASS boundary

A PASS proves only that the pinned host exposes a discoverable Project/path/save surface to a loaded Plugin. It does not prove that any candidate is safe to invoke yet.

## NOT PROVEN

This experiment does not prove:

- that Save As leaves the source `.ymmp` byte-for-byte unchanged;
- that an archive copy can be reloaded successfully;
- that non-selected scenes can be removed safely;
- that a nonpublic candidate is stable across YMM4 versions;
- VideoItem source-time equivalence.

A follow-up mutation/roundtrip experiment must select one exact route and verify before/after invariants.

## Reproduction

Run `.github/workflows/ymm4-project-save-archive-surface.yml` or push/PR a change under this experiment.

## Evidence

Filled after a successful run:

- Source commit:
- Workflow run:
- Artifact:
- Digest/hash:

## Downstream impact

- Consumer: `ziro-lab/ymm4-plugin-garage/plugins/recording-archive`
- Decision supported: choose whether the product should use a native Save/SaveAs route or a narrowly-scoped archive-copy serializer/rewriter.

## Revalidation notes

Re-run when YMM4 changes Project load/save behavior or the recording-archive plugin raises its pinned host version.
