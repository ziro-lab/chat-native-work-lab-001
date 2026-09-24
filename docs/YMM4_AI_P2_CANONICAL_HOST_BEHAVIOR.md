# YMM4 AI P2 — Canonical Host Behavior

This document is the middle layer between public/reference API discovery and implementation guidance.

It points coding agents to **version-pinned YMM4 host behavior that has already been accepted into the Lab's main branch**.

The full experiment remains the evidence authority. The compact knowledge cards are the AI-consumption layer.

## Rule

Use P2 only for claims that depend on actual YMM4 host behavior rather than merely on a public symbol existing.

Examples:

- what happens to object identity after an edit;
- which events actually fire;
- whether a public state change also implies a visible host effect;
- whether an apparent identifier is actually unique across restart;
- what source-time semantics a host path actually uses.

All claims remain bounded by their exact cards/experiments.

## Timeline / interaction

### Existing Utilities Tool group

- Card: [Tool Plugin existing Utilities group should use the localized resource](ymm4-ai-knowledge/tool-group-localized-resource.md)
- Host: YMM4 4.55.1.1 Lite
- Key negative fact: literal display-language group strings are not a language-independent route to the built-in group.

### Preview refresh public boundary

- Card: [Timeline CurrentFrame change is not proof of visible preview repaint](ymm4-ai-knowledge/preview-refresh-public-boundary.md)
- Host: YMM4 4.55.1.1 Lite
- Key negative fact: CurrentFrame PropertyChanged traffic is not pixel-level repaint proof.

### Input intent

- Card: [CurrentFrame change alone is not user time-click intent](ymm4-ai-knowledge/currentframe-is-not-user-click-intent.md)
- Host: YMM4 4.55.1.1 Lite
- Key negative fact: blank/ruler pointer, keyboard and playback can all move CurrentFrame.

## Template / Tachie

### ItemTemplate identity

- Card: [ItemTemplate SceneId is not a durable unique identity](ymm4-ai-knowledge/itemtemplate-sceneid-not-unique.md)
- Host: YMM4 4.55.1.1 Lite
- Key negative fact: SceneId and obvious locator metadata are not API-enforced unique template identity.

### Clone vs Character rebinding

- Card: [Template clone fidelity and Character rebinding are separate concerns](ymm4-ai-knowledge/template-clone-character-rebind.md)
- Host: YMM4 4.55.1.1 Lite
- Key fact: cloned content state and destination canonical Character binding are separate operations.

## VideoItem edit lifecycle

### Split object replacement

- Card: [VideoItem split replaces the original object](ymm4-ai-knowledge/videoitem-split-replaces-original.md)
- Host: YMM4 4.56.1.0 Lite
- Key negative fact: the original object reference does not survive a normal split.

### Rebinding after trim/move/copy/split/UndoRedo

- Card: [VideoItem edit rebinding cannot rely on object identity or source range alone](ymm4-ai-knowledge/videoitem-edit-rebind-not-object-or-source-alone.md)
- Host: YMM4 4.56.1.0 Lite
- Key negative fact: object identity alone and source identity + range alone each fail under different normal edit operations.

## Source time / playback

### ContentLength is not consumed source range

- Card: [VideoItem ContentLength is not consumed source range](ymm4-ai-knowledge/videoitem-contentlength-not-source-range.md)
- Host: YMM4 4.56.1.0
- Key negative fact: ContentLength stayed media duration across the tested rate/offset matrix.

### PlaybackRateMap constant positive mapping

- Card: [PlaybackRateMap constant positive source-time mapping](ymm4-ai-knowledge/playbackratemap-constant-source-time.md)
- Host: YMM4 4.56.1.0
- Surface: S3 for the map getter
- Key fact: the tested native map uses `sourceTime = ContentOffset + itemTime * rate / 100` for constant positive 50/100/200%.

## Host-bundled resources

### FFmpeg

- Card: [YMM4 bundled FFmpeg has a public in-host locator on the tested host](ymm4-ai-knowledge/ffmpeg-bundled-public-locator.md)
- Host: YMM4 4.56.1.0 Lite x64
- Key fact: the tested host bundled ffmpeg/ffprobe and exposed a public in-host FFmpeg locator.

## Other canonical experiment families

The Lab main branch contains additional accepted YMM4 experiments that are not all carded yet.

Examples include:

- public Timeline selection context;
- playhead Quick Drop;
- Character-relative layer placement;
- navigation target context;
- preview playback-rate behavior;
- recording/archive detached serialization and scene closure;
- variable/edge playback-rate archive behavior;
- relative-path/archive portability;
- additional VideoItem timing/source surfaces.

Do not bulk-promote these merely to make the index exhaustive.

Create a card when the fact is:

- reused by multiple downstream features;
- easy for an agent to infer incorrectly;
- narrow enough to state with a clean PASS boundary;
- supported by a stable main-branch evidence record.

## Candidate-only work

The Lab also contains open/Draft experiments.

At the time of this curation pass, high-value Candidate areas include:

- standard command execution routes (Undo/Redo/split/frame seek);
- no-Harmony Timeline folding/navigation;
- ToolState project persistence/lifecycle;
- VoiceItem / VOICEVOX correction and synthesis surfaces.

A Draft reporting GREEN is not enough to enter this P2 canonical layer.

Promote only after the result is accepted into the Lab's canonical main-branch record.

## AI use rule

For a feature:

1. use P0/P1 to find the intended/public/reference surface;
2. check P2 for actual host behavior the feature depends on;
3. if no P2 fact exists and behavior matters, create/extend a narrow Lab experiment;
4. use P3 only for implementation technique after the supported behavior is understood;
5. run downstream product acceptance for integration-sensitive behavior.
