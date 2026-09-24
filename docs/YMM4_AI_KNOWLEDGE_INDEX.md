# YMM4 AI Knowledge Index

This is the compact discovery layer for coding agents.

It points to reusable YMM4 facts already recorded in the Lab. It does not replace the full experiment README or evidence chain.

## How to read this index

- **Canonical** entries are backed by material already recorded on the Lab's main branch.
- **Candidate** entries may exist in open/Draft work but are not promoted here as reusable facts.
- All undocumented host behavior is version-sensitive unless the underlying evidence explicitly says otherwise.

## Development and reference discovery

### Reference-first discovery

- Status: **Canonical**
- Class: policy / reference discovery
- Use: before creating a new native-host probe, check official docs, official samples, shipped/community source, API index, notes and existing implementations.
- Source: [YMM4 Reference Sources](YMM4_REFERENCE_SOURCES.md)

### Dependency-risk ladder

- Status: **Canonical**
- Class: integration policy
- Rule: `S1 public plugin API -> S2 public host/WPF -> S3 bounded reflection -> S4 Harmony/non-public internals`.
- Source: [YMM4 Plugin Surface Guide](YMM4_PLUGIN_SURFACE_GUIDE.md)

## Timeline and Tool behavior

### Timeline selection context

- Status: **Canonical**
- Class: LAB-NATIVE / version-pinned observation
- Reusable fact: the Lab contains a real-host proof for public Timeline selection state, Character context and selection-change observation.
- Evidence: [timeline-selection-context](../experiments/ymm4/timeline-selection-context/)

### Playhead quick drop

- Status: **Canonical**
- Class: LAB-NATIVE / version-pinned observation
- Reusable fact: the Lab contains proof for public playhead access, independent Template cloning and intrinsic-length placement.
- Evidence: [playhead-quick-drop](../experiments/ymm4/playhead-quick-drop/)

### Character-relative layer placement

- Status: **Canonical**
- Class: LAB-NATIVE / version-pinned observation
- Reusable fact: the Lab records same-Character Front/Back baselines and full-span Layer collision behavior.
- Evidence: [character-layer-placement](../experiments/ymm4/character-layer-placement/)

### ItemTemplate identity

- Status: **Canonical**
- Class: NEGATIVE-FINDING + LAB-NATIVE
- Reusable fact: `SceneId` is not a unique restart-persistent ItemTemplate identity in the tested behavior.
- Safe use: do not design durable template identity around `SceneId` alone.
- Knowledge card: [ItemTemplate SceneId is not a durable unique identity](ymm4-ai-knowledge/itemtemplate-sceneid-not-unique.md)
- Evidence: [template-identity](../experiments/ymm4/template-identity/)

### Tool group localization

- Status: **Canonical**
- Class: LAB-NATIVE
- Reusable fact: the Lab records YMM4 4.55.1.1 Tool Plugin group-key behavior and the existing Utilities-group route.
- Important negative finding: a literal language-specific group label is not equivalent to joining the host's localized existing group.
- Evidence: [tool-group-resolution](../experiments/ymm4/tool-group-resolution/)

### Preview refresh surface

- Status: **Canonical**
- Class: LAB-NATIVE
- Reusable fact: the Lab records YMM4 4.55.1.1 plugin-facing preview-refresh surface plus `CurrentFrame` / `ScrollFrame` event observations.
- Boundary: do not generalize this into a universal "set CurrentFrame = force preview refresh" guarantee without reading the experiment.
- Evidence: [preview-refresh-surface](../experiments/ymm4/preview-refresh-surface/)

### Timeline input intent

- Status: **Canonical**
- Class: LAB-NATIVE
- Reusable fact: the Lab records real input-route ordering for item click, re-click, blank, ruler, drag, keyboard and playback-style actions on YMM4 4.55.1.1.
- Evidence: [timeline-input-intent](../experiments/ymm4/timeline-input-intent/)

## Preview playback

### Preview playback-rate surface

- Status: **Canonical**
- Class: LAB-STATIC / native observations as recorded by experiment
- Reusable fact: the v4.56.1.0 inspected preview PlaybackRate path is not statically capped at 8x; the experiment defines the remaining public-Lab reproduction boundary.
- Evidence: [preview-playback-rate](../experiments/ymm4/preview-playback-rate/)

## Recording/archive and source-time mapping

The recording-archive chain contains several reusable facts. Read the individual experiment before using one in product code.

### Live project should remain read-only during archive generation

- Status: **Canonical**
- Class: LAB-NATIVE
- Reusable fact: the tested live `SaveProject(archive)` route changes live project/path state; the accepted archive spine uses detached project loading/mutation/serialization instead.
- Evidence index: [YMM4 experiments](../experiments/ymm4/README.md#recording-archive-investigation--ymm4-v45610)

### PlaybackRate2 is the authoritative speed surface in the tested archive path

- Status: **Canonical**
- Class: LAB-NATIVE / LAB-STATIC chain
- Reusable fact: the recording-archive evidence treats `PlaybackRate2` as authoritative and legacy `BaseItem.PlaybackRate` as obsolete.
- Evidence index: [YMM4 experiments](../experiments/ymm4/README.md#recording-archive-investigation--ymm4-v45610)

### ContentLength is not consumed source range

- Status: **Canonical**
- Class: NEGATIVE-FINDING + LAB-NATIVE
- Reusable fact: in tested 50/100/200% + ContentOffset cases, `ContentLength` remained media duration and was not suitable as source-range planning data.
- Knowledge card: [VideoItem ContentLength is not consumed source range](ymm4-ai-knowledge/videoitem-contentlength-not-source-range.md)
- Evidence index: [YMM4 experiments](../experiments/ymm4/README.md#recording-archive-investigation--ymm4-v45610)

### PlaybackRateMap participates in source-time mapping

- Status: **Canonical**
- Class: LAB-STATIC + LAB-NATIVE
- Reusable fact: the tested native video path routes source-time calculation through `PlaybackRateMap`; constant positive rates were proven with forward/inverse mapping inside the experiment's interval boundary.
- Knowledge card: [PlaybackRateMap constant positive source-time mapping](ymm4-ai-knowledge/playbackratemap-constant-source-time.md)
- Evidence index: [YMM4 experiments](../experiments/ymm4/README.md#recording-archive-investigation--ymm4-v45610)

## Seed-guide migration

The current long-form YMM4 plugin-development guide has been triaged before import:

- [YMM4 Long-form Plugin Guide Triage](YMM4_AI_SEED_GUIDE_TRIAGE.md)

The guide is not treated as one authority level. Current/runtime setup, sample patterns, Lab behavior and implementation guidance are promoted separately.

## Candidate knowledge not yet promoted

Open/Draft Lab work currently contains additional potentially high-value findings, including areas such as:

- standard command execution routes (Undo/Redo, split, frame seek);
- no-Harmony fold-aware Timeline behavior;
- ToolState project persistence/lifecycle;
- VoiceItem/VOICEVOX reading, synthesis, cache and correction surfaces.

These are intentionally **not copied into the canonical index here merely because a Draft PR reports GREEN**.

Promote them only after their evidence is accepted into the Lab's canonical main-branch record.

## Backlog for this index

High-value next curation targets:

1. ToolState and SettingsBase persistence once canonical.
2. Standard YMM4 command execution once canonical.
3. Item/template clone fidelity.
4. `.ymme` install/update preservation behavior.
5. No-Harmony Timeline structural/navigation facts.
6. VoiceItem / VOICEVOX public mechanisms.
7. PropertyEditor, AudioEffect and VideoEffect implementation facts from official/current sources.
8. Reusable negative findings from all completed experiments.
