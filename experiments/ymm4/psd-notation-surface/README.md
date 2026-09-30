# YMM4 PSD notation surface

## Goal

On the current target host, determine which PSD notation markers have direct static evidence in YMM4's built-in managed PSD implementation, with special attention to:

- `*`
- `!`
- `:flip`
- `:flipx`
- `:flipy`
- `:flipxy`

This experiment is discovery evidence for deciding whether a replacement PSD tachie plugin should reimplement notation handling or delegate it to YMM4.

## Environment

- Host: YukkuriMovieMaker4 Lite v4.56.1.0
- Official release asset: `YukkuriMovieMaker_v4.56.1.0_Lite.zip`
- Expected SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Runner: GitHub Actions `windows-latest`
- Runtime/toolchain: .NET 10 SDK supplied by the runner

The host version is intentionally pinned to the YMM4 latest release confirmed at experiment creation time (2026-10-01).

## Assertions

1. The exact official Lite archive is downloaded and its SHA256 matches.
2. The archive contains a runnable/inspectable YMM4 installation.
3. The static probe builds successfully.
4. The probe finds at least one managed PSD-related assembly or a managed assembly containing the requested notation strings.
5. The probe records:
   - PSD-related assembly references;
   - PSD/Tachie/Layer/Part-related type names;
   - matching `ldstr` literals with declaring method context;
   - raw ASCII/UTF-16 occurrences of multi-character notation tokens;
   - discovery-only integer constant hits for `!` (33) and `*` (42) in PSD-related method contexts.

## PASS means

The experiment successfully maps direct static evidence for the requested notation tokens in YMM4 v4.56.1.0's managed implementation.

A direct `ldstr` or raw hit for a notation token is evidence that the inspected build contains that token. Method/type context can narrow the likely parser implementation.

## NOT PROVEN

A static PASS does **not** by itself prove:

- that the UI/runtime actually reaches the discovered code path;
- exact `*` / `!` semantics;
- hierarchy, mixed-notation, duplicate-name, or preset interaction;
- that absence of a literal proves a feature is absent (the notation may be represented by character constants or another encoding);
- rendering correctness of flip notation;
- compatibility with third-party PSD plugins.

If static evidence does not settle a material question, follow with a bounded runtime PSD fixture probe rather than inferring behavior.

## Reproduction

Run the workflow:

`.github/workflows/ymm4-psd-notation-surface.yml`

The workflow downloads the pinned official release, builds this project, runs the static scan, and uploads compact evidence.

## Evidence

Expected artifact:

- `ymm4-psd-notation-surface`
  - `summary.txt`
  - `static-scan.json`
  - host archive hash
  - build log
  - provenance JSON

No YMM4 binary is committed or uploaded as evidence.
