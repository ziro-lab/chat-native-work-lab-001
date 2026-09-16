# Scene archive dependency surface

## Question

On YMM4 v4.56.1.0, can a loaded Plugin identify the native Project/Timeline scene collection and the `SceneItem` state needed to determine which scenes must be retained in a selected-scene recording archive?

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

The probe runs inside the real YMM4 process and records:

- the active Timeline and MainViewModel/model types;
- Project/Scene/Timeline-related fields, properties and methods on the active host objects;
- candidate enumerable scene/timeline collections reachable from the root/model;
- the native `SceneItem` type, its constructors and Scene/Timeline/Id/Name-related properties;
- candidate scene-reference members that could support an explicit dependency graph.

PASS requires both:

1. at least one Project/Scene/Timeline collection-like host member is discoverable; and
2. the native `SceneItem` type exists and exposes at least one discoverable Scene/Timeline/Id-like reference member.

## PASS boundary

A PASS proves only that, on the pinned YMM4 build, a Plugin can discover enough native scene/project structure to design a narrow selected-scene/dependency adapter.

It does not claim which candidate is the final supported production route until a follow-up mutation/roundtrip experiment selects and verifies one exact path.

## NOT PROVEN

This experiment does not prove:

- safe deletion of non-selected scenes;
- recursive dependency closure correctness;
- save-as/archive-copy behavior;
- that a discovered nonpublic member is stable across future versions;
- VideoItem source-time semantics;
- UI interaction for choosing scenes.

## Reproduction

Run `.github/workflows/ymm4-scene-archive-surface.yml` or push/PR a change under this experiment.

## Evidence

Filled after a successful run:

- Source commit:
- Workflow run:
- Artifact:
- Digest/hash:

## Downstream impact

- Consumer: `ziro-lab/ymm4-plugin-garage/plugins/recording-archive`
- Decision supported: determine the smallest adapter needed to enumerate candidate archive scenes and detect SceneItem dependencies before any Archive Project rewrite is implemented.

## Revalidation notes

Re-run when YMM4 changes scene/timeline/project structure or the recording-archive plugin raises its pinned host version.
