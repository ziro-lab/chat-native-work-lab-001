# YMM4 AI Development Reference

This document is the AI-facing entry point for YukkuriMovieMaker4 (YMM4) plugin development knowledge maintained in this Lab.

The goal is not to turn every note into a timeless "specification". The goal is to give coding agents a compact, provenance-aware reference that separates:

- intended/public contracts;
- implementation examples;
- version-pinned host observations;
- negative findings and failed approaches;
- product-specific conclusions that must not be generalized.

Use this document together with:

- [YMM4 Reference Sources](YMM4_REFERENCE_SOURCES.md)
- [YMM4 Plugin Surface Guide](YMM4_PLUGIN_SURFACE_GUIDE.md)
- [YMM4 Observation Policy](YMM4_OBSERVATION_POLICY.md)
- [YMM4 AI Knowledge Index](YMM4_AI_KNOWLEDGE_INDEX.md)
- [Current Official Development Baseline](YMM4_AI_P0_OFFICIAL_BASELINE.md)
- [Public and Reference Plugin Surfaces](YMM4_AI_P1_PLUGIN_SURFACES.md)
- [Official Plugin Surface Map](YMM4_AI_P1B_OFFICIAL_PLUGIN_SURFACE_MAP.md)
- [Implementation Guidance](YMM4_AI_P3_IMPLEMENTATION_GUIDANCE.md)
- [YMM4 AI Knowledge Card Template](YMM4_AI_KNOWLEDGE_CARD_TEMPLATE.md)
- [Long-form Plugin Guide Triage](YMM4_AI_SEED_GUIDE_TRIAGE.md)

## Why this exists

YMM4 plugin work mixes several different kinds of information:

1. public interfaces and documented setup;
2. sample code that demonstrates one working route;
3. host behavior that is only known after static inspection or native execution;
4. implementation habits learned from real plugins;
5. version-sensitive internal behavior;
6. approaches that looked plausible but were disproved.

A coding agent can produce bad code when those categories are presented with equal authority.

This reference therefore treats provenance and scope as part of the technical fact itself.

## Knowledge classes

Every reusable claim should fit one of these classes.

| Class | Meaning | May be treated as |
| --- | --- | --- |
| **OFFICIAL-CONTRACT** | Current author-maintained YMM4 documentation or public API contract | Intended supported behavior, within the documented scope |
| **OFFICIAL-SAMPLE** | Current official sample implementation | A supported usage example, not a universal lifecycle guarantee |
| **REFERENCE-IMPLEMENTATION** | Community/shipped/open-source implementation | Precedent and discovery material |
| **LAB-STATIC** | Version-pinned reflection / IL / structure inspection | Static fact for the inspected build |
| **LAB-NATIVE** | Version-pinned automated real-host observation | Reproducible host behavior inside the stated PASS boundary |
| **LAB-MANUAL** | Version-pinned interactive human observation | UI/perceptual behavior inside the stated manual boundary |
| **NEGATIVE-FINDING** | A plausible route was tested and found insufficient / unsafe / ambiguous | A route to avoid under the tested conditions |
| **CANDIDATE** | Useful lead that is not yet canonical evidence | Investigation input only |

Do not silently promote OFFICIAL-SAMPLE, REFERENCE-IMPLEMENTATION or CANDIDATE into a host guarantee.

## Dependency-risk surface

Use the existing Lab surface ladder:

| Level | Surface | Default |
| --- | --- | --- |
| **S1** | Plugin-facing/public YMM4 API | Prefer |
| **S2** | Public host / WPF surface | Use when S1 lacks required semantics |
| **S3** | Bounded reflection/adaptation | Require a narrow reason, exact bounds and fail-safe behavior |
| **S4** | Harmony / non-public internals | Last resort; require dedicated evidence |

The existence of an S3/S4 example elsewhere is not a reason to skip an S1/S2 route.

## AI consumption rules

A coding agent using this repository should follow this order:

1. Define the narrow feature/host behavior required.
2. Search [YMM4 AI Knowledge Index](YMM4_AI_KNOWLEDGE_INDEX.md).
3. Check existing Lab observations before opening a new experiment.
4. Check [YMM4 Reference Sources](YMM4_REFERENCE_SOURCES.md) for official/public entry points.
5. Prefer the lowest-risk surface that satisfies the requirement.
6. Treat version-pinned observations as version-pinned, not timeless.
7. Preserve PASS boundaries and NOT PROVEN boundaries.
8. Preserve negative findings; do not retry disproved routes without a new reason.
9. When behavior is ambiguous or a version-sensitive route cannot be resolved, fail closed rather than inventing host semantics.
10. Product integration must still run its own acceptance when failure matters.

## Source-conflict rule

Do not flatten every “official” or “community” source into one authority level.

Choose evidence by the question being answered:

- **exact symbol/signature/compile shape** -> current implementation/API/assembly evidence;
- **intended setup/workflow** -> current official documentation/sample guidance;
- **undocumented runtime behavior** -> version-pinned Lab evidence;
- **product behavior** -> downstream product acceptance.

When sources disagree, keep the disagreement visible. Do not silently rewrite one source to make the set look consistent.

A concrete current example is the official VideoSource sample: its README says `IVideoSourcePlugin`, while the implementation and current API index use `IVideoFileSourcePlugin`.

## Language for claims

Use wording that preserves evidence strength.

Good:

> Observed on YMM4 4.56.1.0: public `Timeline.CurrentFrame` writes emitted `PropertyChanged("CurrentFrame")`.

Good:

> The official sample uses `IToolViewModel.SaveState()` / `LoadState()`.

Bad:

> YMM4 always refreshes the preview when `CurrentFrame` changes.

Bad:

> `ToolState.SavedState` is guaranteed to survive every project/restart transition.

Unless a claim's evidence proves the stronger statement, keep the narrower wording.

## Baseline development scope

The initial AI reference is intended to cover these areas.

### Project and packaging baseline

- target framework / runtime;
- YMM4 assembly references;
- `Private=false` rules for host-owned assemblies;
- local path externalization;
- `.ymme` packaging and update behavior.

### Plugin surfaces

- Tool / Timeline Tool;
- Audio and Video Effects;
- PropertyEditor / custom editor integration;
- media source, Shape, Tachie, Voice and related official plugin categories;
- Settings and localization.

### Timeline / host interaction

- selection context;
- playhead/current-frame behavior;
- add/drop/split/command routes;
- Undo/Redo;
- template identity and clone fidelity;
- layer/display-row behavior;
- event ordering and refresh surfaces.

### Persistence

- ToolState;
- SettingsBase;
- project serialization and detached save patterns;
- plugin-local durable storage where host persistence is insufficient.

### Media / graphics / audio

- Direct2D resource lifetime;
- dynamic GPU resource rebuild/disposal;
- playback-rate/source-time mapping;
- VOICEVOX / VoiceItem public surfaces where proven.

## Seed-document handling

A long-form YMM4 plugin development guide can be useful as a coding-agent seed, but its statements must be imported into this reference by classification rather than copied wholesale as one authority level.

When promoting material from such a guide:

- documented setup -> OFFICIAL-CONTRACT when verified against the current official source;
- sample usage -> OFFICIAL-SAMPLE;
- real-host behavior -> LAB-* only when backed by a Lab observation;
- implementation advice -> label as guidance, not API fact;
- unverified internals -> CANDIDATE until inspected or tested.

This prevents a useful guide from becoming an accidental source of timeless claims.

## What belongs in the knowledge index

Promote a result into the AI index when it is:

- likely to be reused by more than one plugin;
- easy for an agent to get wrong without the result;
- supported by a clear source/evidence record;
- narrow enough to state without hiding important conditions.

Do not promote every experiment detail. The full experiment remains the canonical evidence record.

## Negative knowledge is first-class

Record failed routes when they are likely to be retried by an agent.

Examples of useful negative knowledge:

- a public-looking setter does not provide the required UI semantic;
- an identifier is not stable/unique across restart;
- a literal group name creates a separate localized group;
- a source-time property looks plausible but does not represent consumed source range;
- a marker in one text surface does not automatically map to a synthesis structure.

A negative finding should state:

- what was attempted;
- on which YMM4 version;
- what actually happened;
- what narrower alternative is supported;
- what remains unproven.

## Version policy

For undocumented host behavior:

- always store the tested YMM4 version;
- keep host identity/hash where practical;
- revalidate when a newer YMM4 release materially touches the subsystem;
- never silently rewrite old evidence to match a new version.

For current public contracts and samples, record the inspected date/commit when practical.

## Current status

**v0 curation in progress**

The knowledge model and index structure are established. The long-form plugin guide now has a section-by-section triage in [YMM4_AI_SEED_GUIDE_TRIAGE.md](YMM4_AI_SEED_GUIDE_TRIAGE.md), and the first canonical Lab findings have been extracted into narrow knowledge cards under `docs/ymm4-ai-knowledge/`.

Current next steps:

1. keep the verified P0 baseline current as official docs/samples change;
2. keep the P1/P1B surface maps current as official samples/API shapes evolve;
3. continue promoting canonical Lab negative findings and edit-lifecycle facts into cards;
4. keep implementation/performance advice in the Guidance layer instead of promoting it into API contracts;
5. keep open/Draft PR results out of the canonical index until their evidence is accepted.
