# YMM4 Plugin Surface Guide

This document records a **reference-first discovery policy** for YukkuriMovieMaker4 (YMM4) plugin work.

It is not native-host evidence. Its purpose is to reduce unnecessary reverse engineering by checking known plugin-facing routes before creating a new probe, while keeping this Lab as the canonical place for version-pinned host-behavior evidence.

The maintained external-source registry is [`YMM4_REFERENCE_SOURCES.md`](YMM4_REFERENCE_SOURCES.md). It separates official documentation, official samples, shipped/community source, API indexes, community notes and existing plugin implementations from Lab evidence.

Use that registry before broad reflection or a new native-host run.

## Reference implementation example

Reference:

- repository: [leftcontroller0518/YMM4plugin_template](https://github.com/leftcontroller0518/YMM4plugin_template)
- inspected commit: [`a11c25bf478bfa4d386ee81b69f5c747f0968ca5`](https://github.com/leftcontroller0518/YMM4plugin_template/commit/a11c25bf478bfa4d386ee81b69f5c747f0968ca5)
- inspected date: 2026-09-20
- repository license: MIT

The repository describes itself as a collection of YMM4 plugin templates and states that its code is build- and host-checked on Windows 11 when updated. That statement is useful provenance, but this Lab does **not** treat it as evidence for an undocumented YMM4 behavior until the relevant claim is reproduced or inspected here.

The reference currently contains examples for:

- Timeline tools;
- dockable Tool panels;
- YMM4 Settings integration;
- video/audio effects;
- shapes and writers;
- Harmony-based easing extension;
- Harmony-based internal draw-method hooking.

## Useful known entry points

### Timeline Tool

The Timeline sample uses:

- `IToolPlugin`;
- `IToolViewModel`;
- `ITimelineToolViewModel`;
- `TimelineToolInfo.Timeline`;
- `TimelineToolInfo.UndoRedoManager`.

The sample reads public Timeline state such as:

- `Items`;
- `SelectedItems`;
- `CurrentFrame`;
- `MaxLayer`.

This is a useful starting point for Timeline-oriented plugin work. It does **not** establish higher-level input intent, preview synchronization, playback behavior or UI event ordering.

### Tool panel state

The Tool sample uses `IToolViewModel.SaveState()` / `LoadState()` and stores panel-local data in `ToolState.SavedState`.

The inspected YMM4 Community source at commit `ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa` also uses this pattern in Browser, Explorer and Notepad. That makes it a strong public implementation precedent before inventing a custom store for short-lived Tool UI state.

It does not yet establish:

- exactly when YMM4 calls `SaveState` and `LoadState`;
- whether state survives panel close/reopen, YMM4 restart, project switch or layout change;
- the serialization/size/error boundaries of `SavedState`.

Those remain Lab questions if a downstream plugin needs them.

### Settings

The Settings sample uses `SettingsBase<T>`, `Set(ref field, value)`, `SettingsCategory`, and a Settings view.

The inspected YMM4 Community source also uses `SettingsBase<T>` across Tool, Voice, Effect and other features. This is a good candidate for simple persistent plugin preferences such as booleans, numbers and small strings.

Do not assume that it is a replacement for a product-specific store that needs schema migration, atomic replacement, conflict detection, corruption refusal or large structured state.

### Harmony / internal hooks

The reference contains Harmony examples that patch both public and internal YMM4 behavior, including:

- animation easing calculation/menu surfaces;
- an internal draw/update path resolved through `AccessTools`.

These examples prove that such implementation patterns exist in the ecosystem. They do **not** make an internal hook a preferred Lab or product route.

## Surface escalation ladder

Before adding a new YMM4 host dependency, use the least invasive surface that can answer the actual requirement.

| Level | Surface | Examples | Default stance |
| --- | --- | --- | --- |
| **S1** | Plugin-facing/public YMM4 API | `IToolPlugin`, `ITimelineToolViewModel`, `TimelineToolInfo`, `SettingsBase<T>`, public Timeline properties | Prefer first |
| **S2** | Public host/WPF surface | public events, `INotifyPropertyChanged`, routed input, `InputManager`, public members on known live DataContexts | Use when S1 lacks semantics |
| **S3** | Bounded reflection/adaptation | exact known host type + exact public member, fail-safe when absent/ambiguous | Require a narrow reason and version-sensitive record |
| **S4** | Harmony / non-public host internals | internal/private method patch, behavior replacement | Last resort; require a dedicated Lab claim before product dependence |

The ladder is about dependency risk, not about whether a technique is "allowed". A higher level is justified only when the lower level cannot express the needed behavior.

## Discovery workflow

For a new YMM4 question:

1. Define the narrow host behavior the product actually needs.
2. Search existing Lab observations.
3. Check the current reference registry in [`YMM4_REFERENCE_SOURCES.md`](YMM4_REFERENCE_SOURCES.md).
4. Prefer official documentation and official samples for intended public contracts.
5. Check `YukkuriMovieMaker.Plugin.Community` for implementation patterns already used by shipped/community tools.
6. Use the current API index and community notes to discover public symbols, standard controls and known pitfalls.
7. Search existing public plugins/templates for precedent before declaring a route unavailable or novel.
8. Use targeted static inspection only for facts not resolved by those references.
9. Create a native-host Lab experiment only when the remaining question is undocumented/version-sensitive behavior that matters downstream.
10. Product integration must still run its own acceptance; a Lab result does not certify the whole plugin.

After discovery, apply the S1 -> S4 surface ladder independently. Finding an S3/S4 example in external code is not a reason to skip a viable S1/S2 route.

Reference lookup is a **probe-design accelerator**, not evidence.

## Known example of why reference code is not authoritative

The inspected template uses literal strings such as `"タイムライン"` and `"カスタムツール"` for `DefaultGroupName`.

The Lab's YMM4 4.55.1.1 `tool-group-resolution` experiment found a stronger route for joining the existing Utilities group:

`YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName`

On the tested en-US host, that resource resolved to the existing `Utilities` group, while a literal Japanese group name formed a separate group.

The takeaway is intentional:

> runnable example code can be an excellent starting point without being the canonical answer for a specific host behavior.

When reference code and pinned Lab evidence differ, the pinned Lab observation governs the claim for that tested host version.

## High-value unresolved questions

Do not create experiments merely to fill a matrix. Create them when a downstream plugin actually needs the answer.

### ToolState lifecycle

Candidate narrow experiment:

> On an exact YMM4 build, when are `IToolViewModel.SaveState` and `LoadState` invoked, and which panel/lifecycle transitions preserve `ToolState.SavedState`?

Useful subcases can be split if needed:

- panel close/reopen;
- YMM4 restart;
- project/scene switch;
- dock/layout restoration;
- empty or malformed saved state.

### SettingsBase persistence boundary

Candidate narrow experiment:

> On an exact YMM4 build, what persistence lifecycle does `SettingsBase<T>` provide for a minimal plugin setting?

Only expand into corruption/path/migration behavior if a real plugin needs those guarantees.

### Harmony compatibility boundary

Do not run a generic "does Harmony work?" experiment.

If a plugin needs an internal hook, create an experiment for the **specific target method and semantic behavior**, record the exact YMM4 version/signature, and state the fallback when the target cannot be resolved.

## Development-loop note

The inspected template also demonstrates a convenient local development loop:

`build -> copy plugin DLLs into YMM4 user/plugin -> launch YMM4 from the IDE`.

That is useful for downstream developer ergonomics, especially the plugin Garage, but it is not a host-behavior observation by itself. Keep local path configuration out of committed personal-machine values when adopting the pattern.
