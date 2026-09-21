# YMM4 Reference Sources

This document is the reference registry used before opening a new YukkuriMovieMaker4 (YMM4) host probe.

The goal is to avoid spending native-host runs on questions that can already be answered well enough from an official contract, a current API index, or a known implementation example.

These sources are **discovery material, not Lab evidence**. Version-sensitive or undocumented runtime behavior that a downstream product depends on still belongs in a pinned Lab observation.

## Source classes

| Class | Meaning | Typical use |
| --- | --- | --- |
| **Official documentation** | Author-maintained public guidance for YMM4/plugin development | Establish intended setup, supported extension points and packaging guidance |
| **Official/sample source** | Author-maintained sample implementation | Find supported API usage patterns before inventing a new route |
| **Shipped community source** | Community plugin code that is incorporated into YMM4 | Strong implementation reference for patterns used by built-in/community tools |
| **API index** | Generated or community-maintained symbol/reference documentation | Discover names, types and nearby public APIs quickly |
| **Community notes/tutorials** | Practical development notes and examples | Find controls, patterns, pitfalls and candidate APIs |
| **Existing plugin implementations** | Public plugins and templates | Search for precedent before designing a new dependency |
| **Lab observation** | Version-pinned static/native/manual evidence in this repository | Establish the actual host behavior relied on downstream |

A source being official or widely used does **not** automatically prove an undocumented runtime behavior. Conversely, a Lab probe should not be opened merely to rediscover a public type name or a documented setup step.

## Primary reference sources

### 1. Official YMM4 plugin documentation

- Site: [プラグインを作成する | 饅頭遣いのおもちゃ箱](https://manjubox.net/ymm4/faq/plugin/how_to_make/)
- Role: **official documentation**
- Inspected: 2026-09-21
- Good for:
  - current target framework/runtime guidance;
  - required YMM4 assembly references;
  - development setup;
  - plugin packaging and basic distribution guidance;
  - links to the official sample repository.
- Boundary:
  - intended/public guidance is not evidence for undocumented event ordering, lifecycle timing, rendering behavior or other version-sensitive host semantics.

At the inspected date the page describes a .NET 10 plugin project targeting `net10.0-windows10.0.19041.0`.

### 2. Official plugin samples

- Repository: [manju-summoner/YukkuriMovieMaker4PluginSamples](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples)
- Role: **official/sample source**
- Inspected branch: `master`
- Inspected commit: [`8e06e7247bc4a9d870c604927559b8be9a8e4910`](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples/commit/8e06e7247bc4a9d870c604927559b8be9a8e4910)
- Inspected: 2026-09-21
- Good for:
  - AudioEffect;
  - AudioSource;
  - AudioSpectrum;
  - FileWriter;
  - ImageSource;
  - Localization;
  - PropertyEditor;
  - Shape;
  - Tachie;
  - TextCompletion;
  - Transition;
  - VideoEffect;
  - VideoSource;
  - Voice.
- Boundary:
  - sample code demonstrates an implementation route; it does not by itself establish every lifecycle or host-behavior guarantee around that route.

### 3. YMM4 Community plugin source

- Repository: [manju-summoner/YukkuriMovieMaker.Plugin.Community](https://github.com/manju-summoner/YukkuriMovieMaker.Plugin.Community)
- Role: **shipped community source**
- Inspected branch: `master`
- Inspected commit: [`ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa`](https://github.com/manju-summoner/YukkuriMovieMaker.Plugin.Community/commit/ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa)
- Inspected: 2026-09-21
- Repository license at inspection: LGPL-3.0
- Good for:
  - implementation patterns used by tools that ship with YMM4;
  - real `IToolViewModel.SaveState()` / `LoadState(ToolState)` usage in Browser, Explorer and Notepad;
  - real `SettingsBase<T>` usage across Tool, Voice, Effect and other features;
  - localization/resource usage;
  - checking whether a proposed public surface is already used in a maintained YMM4-adjacent implementation.
- Boundary:
  - source usage can establish that a pattern exists and is actively used, but not exactly when YMM4 invokes lifecycle methods or how every transition behaves.

When this source already contains the same lower-risk pattern required by a downstream plugin, prefer understanding that pattern before adding reflection or a new Lab probe.

### 4. YMM API Documentation

- Site: [YMM API Docs](https://ymm-api-docs.vercel.app/)
- Repository: [p-rer/YMM-API-Docs](https://github.com/p-rer/YMM-API-Docs)
- Role: **community API index**
- Inspected branch: `master`
- Inspected commit: [`7d5535d72eea0aba2ea644826327436abb6b38b6`](https://github.com/p-rer/YMM-API-Docs/commit/7d5535d72eea0aba2ea644826327436abb6b38b6)
- Inspected: 2026-09-21
- Good for:
  - fast symbol/type discovery;
  - nearby public methods and properties;
  - plugin/effect/file-source/player API exploration;
  - narrowing what should be checked in current assemblies or official source.
- Boundary:
  - this is explicitly unofficial documentation;
  - generated/reference documentation is not proof of runtime semantics.

Use this before broad assembly-wide reflection when the question is primarily “what public API might exist for this?”

### 5. YMM4Plugin Scrapbox

- Site: [YMM4Plugin](https://scrapbox.io/ymm4plugin/)
- Role: **community notes/tutorials**
- Inspected: 2026-09-21
- Good for:
  - PropertyEditor and YMM4 control discovery;
  - effect/plugin implementation examples;
  - practical setup and UI patterns;
  - finding terminology and candidate APIs to verify elsewhere.
- Notable current index:
  - “プラグインから使えるコントロール一覧” documents controls such as animation/numeric/toggle editors, selectors and other YMM4 UI components.
- Boundary:
  - the site itself states that its contents are not guaranteed accurate;
  - treat code and claims as leads, not pinned host evidence.

This source is especially useful for avoiding unnecessary custom WPF controls when a suitable YMM4-native control already exists.

### 6. InuInu YMM4 Plugin notes

- Site: [YMM4 Plugin 制作メモ](https://zenn.dev/inuinu/scraps/287c1d83e7f67c)
- Role: **community implementation notes**
- Inspected: 2026-09-21
- Good for:
  - Tool plugin patterns;
  - `SettingsBase<T>`;
  - useful public helpers such as `AppVersion`, `AppDirectories` and `Log`;
  - local debug/build/package workflows;
  - dependency/DLL collision pitfalls;
  - pointers to additional community projects and discussion spaces.
- Boundary:
  - individual notes may target older YMM4/.NET versions;
  - verify current signatures and product-sensitive behavior before adoption.

### 7. YMM4 plugin template

- Repository: [leftcontroller0518/YMM4plugin_template](https://github.com/leftcontroller0518/YMM4plugin_template)
- Role: **existing plugin/template implementation**
- Inspected commit: [`a11c25bf478bfa4d386ee81b69f5c747f0968ca5`](https://github.com/leftcontroller0518/YMM4plugin_template/commit/a11c25bf478bfa4d386ee81b69f5c747f0968ca5)
- Inspected: 2026-09-20
- Repository license at inspection: MIT
- Good for:
  - Timeline tools;
  - dockable Tool panels;
  - Settings integration;
  - effects, shapes and writers;
  - examples of both public-surface and Harmony/internal techniques.
- Boundary:
  - runnable example code is not authoritative host evidence;
  - a higher-risk technique shown here should not bypass the Lab escalation policy.

The existing `tool-group-resolution` observation is an example where pinned Lab evidence found a stronger localized-resource route than a literal group-name pattern used in a reference implementation.

## Existing implementation discovery

Before declaring a feature or integration route novel, search existing public plugins.

Useful indexes:

- [Official YMM4 plugin list](https://manjubox.net/ymm4/faq/plugin/list/)
- [GitHub `ymm4-plugin` topic](https://github.com/topics/ymm4-plugin)

Use these to answer questions such as:

- Has another plugin already implemented a similar Timeline interaction?
- Is there a public API pattern already in active use?
- Is a custom control unnecessary because the ecosystem already uses a YMM4-native component?
- Is there a known dependency or packaging pitfall?

Existing implementations remain references, not evidence for the exact host/version under test.

## Required discovery order

For a new YMM4 question, use the cheapest trustworthy source that can answer it:

1. **Existing Lab observations** — avoid repeating already-pinned work.
2. **Official YMM4 documentation** — establish the intended public contract and current development baseline.
3. **Official plugin samples** — find author-provided usage patterns.
4. **YMM4 Community source** — check patterns that are actually used by shipped/community tools.
5. **Current API index** — locate public symbols and neighboring APIs.
6. **Community notes/tutorials** — find practical controls, pitfalls and implementation leads.
7. **Existing public plugins/templates** — look for precedent and competing implementation routes.
8. **Current YMM4 assembly static inspection** — use targeted reflection/IL only for facts not resolved above.
9. **New Lab probe** — execute the real host only for a concrete undocumented/version-sensitive behavior that matters downstream.
10. **Product acceptance** — re-test the adopted behavior in the downstream product when integration failure would matter.

Within implementation dependencies, continue to use the separate surface escalation ladder:

`public plugin API -> public host/WPF -> bounded reflection -> Harmony/non-public internals`.

## When a new Lab probe is justified

A probe is usually justified when the remaining question is about behavior rather than discoverability, for example:

- exact event/input ordering;
- whether a public setter causes a visible host update;
- save/load lifecycle timing;
- serialization round-trip semantics;
- runtime source-time mapping;
- host behavior at an edge condition;
- compatibility of a specific internal target that a product truly cannot avoid.

A probe is usually **not** justified merely to:

- discover a public class/property name;
- list standard controls;
- reproduce a documented project setup;
- show that a sample interface can compile;
- duplicate a maintained implementation example without a behavior question.

## Evidence and copying rules

- Link to external sources rather than copying large bodies of code or prose into this Lab.
- When an external repository materially shapes a probe, record the repository and exact commit when practical.
- Keep the source author's claim separate from the Lab observation.
- Respect each source's license and usage terms; do not assume this Lab's Apache-2.0 license applies to third-party material.
- If a reference conflicts with a version-pinned Lab observation, preserve both facts and use the Lab result for the exact tested host version.
- Re-check mutable web documentation when a future task depends materially on it.

Reference lookup is a **probe-design accelerator**. The Lab remains the place where undocumented host behavior is turned into reproducible evidence.
