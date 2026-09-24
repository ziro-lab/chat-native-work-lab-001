# YMM4 Long-form Plugin Guide Triage

This document classifies the current long-form YMM4 plugin-development guide before its contents are promoted into the AI-facing reference.

The source guide is useful, but it deliberately mixes API facts, sample patterns, runtime observations, implementation advice and performance tips. The AI reference must not flatten those into one authority level.

This document summarizes and classifies the source. It does not copy the guide verbatim.

## Classification

| Mark | Meaning | Import rule |
| --- | --- | --- |
| **A — Contract** | Current official/public development contract | Promote after checking the current official source |
| **B — Sample/reference** | Supported or established implementation pattern | Promote as OFFICIAL-SAMPLE or REFERENCE-IMPLEMENTATION, not as a host guarantee |
| **C — Verify** | Version-sensitive host/internal behavior | Require LAB-STATIC / LAB-NATIVE evidence before canonical use |
| **D — Guidance** | General C#/WPF/Direct2D implementation advice | Keep as guidance; do not present as YMM4 API fact |
| **E — Rewrite** | Useful section containing an over-broad, incomplete or risky claim | Split/rewrite before AI consumption |

## Section-by-section triage

| Source section | Primary class | Decision | Notes |
| --- | --- | --- | --- |
| **1. Overview and architecture** | C / E | Rewrite | DLL/plugin-interface concept is useful. Exact internal loader class names and loader sequence are implementation details and should be version-pinned before being treated as facts. |
| **2. Technical requirements and development environment** | A / C / D / E | Import selectively | Current .NET 10 target is Contract. `Directory.Build.props` is an OFFICIAL-SAMPLE pattern. The seed guide's mandatory `Private=false` claim is **not** present in the current official page/sample and must not be promoted as Contract. Exact assembly-to-namespace ownership claims still require current assembly/source verification. |
| **3. Plugin categories and interfaces** | B / E | Rewrite as "common official/sample surfaces" | Official samples cover more categories than the current table, so the table must not imply exhaustiveness. Separate actual interface requirements from convenience/default members. |
| **4. Tool Plugin details** | B / C | Import surface, verify lifecycle | `IToolPlugin`, `IToolViewModel`, Tool state and Timeline Tool entry points are high-value. Sample signatures belong in Sample/reference. Exact lifecycle timing, auto-binding semantics and persistence boundaries require evidence when product-sensitive. |
| **5. Media-source internal structure** | B / D | Import as implementation pattern | Factory/source split matches current official implementation for video/audio file sources. Current implementation/API confirm `IVideoFileSourcePlugin` + `IVideoFileSource`; note that the official VideoSource README itself contains the stale/mismatched `IVideoSourcePlugin` label. |
| **6. UI and parameter system** | B / C / D / E | High-priority import | Current official custom-editor baseline is `IPropertyEditorControl` + `PropertyEditorAttribute2`, with BeginEdit/EndEdit around edits. The seed guide's claim that `IPropertyEditorControl2` is mandatory is too broad; treat editor-info extensions separately. |
| **6.5. VideoEffect implementation** | B / C / E | Split into two routes | Current official sample uses `IVideoEffectProcessor` directly. Current shipped/community source actively uses `VideoEffectProcessorBase`. Preserve both as different evidence/routes; do not present the base as mandatory. Current Community overrides also use nullable `ID2D1Image?` signatures. |
| **7. Resource management** | D / B | Keep as Guidance | Disposal, event unsubscription and DirectX lifetime rules are useful. Wording such as "must" should be reserved for actual API/lifetime requirements, not general optimization preferences. |
| **7.5. Dynamic D2D resource management** | D / C | Keep as proven pattern, not contract | Change detection, rebuilding and detach-before-dispose are valuable patterns. Exact resource behavior should be tied to the involved D2D/YMM4 path rather than declared universally. |
| **7.6. Performance optimization** | D / E | Move out of core spec | Keep only as optional measure-first guidance. Avoid hard rules such as always freezing every brush/pen, fixed `stackalloc` safety ranges, or assuming a micro-optimization is worthwhile without profiling. |
| **8. Accessing YMM4 internals by reflection** | C / E | Replace with S3 policy | Do not publish a generic reflection recipe as the default. Use the Lab's S1 -> S4 ladder. Any reflection dependency should name the exact target/version, cache/failure behavior and fail closed if unresolved. |
| **9. Debugging and testing** | A / B / C / D | Split | Build/install loop and developer-mode guidance can be reference material. The troubleshooting table mixes API facts, implementation mistakes and version-specific behavior; promote each row separately only when sourced. |
| **10. Distribution/package** | A / C | High-priority import | `.ymme` packaging/install belongs near the official baseline. Update/replacement/preservation semantics are runtime behavior and should stay version-pinned Lab knowledge. |
| **11. Minimal Tool template** | B | Keep as generated starter, not spec | Good coding-agent scaffold after compile-checking it against current official/public surfaces. Keep the canonical interface contract separate from the example. |
| **12. Minimal VideoEffect template** | B / E | Keep after compile-check + constructor note | Useful starter. Must not encourage reading derived fields during an overridable `CreateEffect` call that occurs from the base constructor before the derived constructor body completes. |

## High-priority extraction order

### P0 — safe development baseline

Extract first:

1. current target framework/runtime;
2. official assembly/reference setup;
3. `Private=false` host-owned assembly rule;
4. current official plugin/sample category map;
5. `.ymme` packaging/install baseline.

Reason: these prevent an agent from generating a project that cannot load before feature work even starts.

### P1 — high-value public implementation surfaces

Then extract:

1. Tool / Timeline Tool interfaces;
2. ToolState and Settings patterns;
3. PropertyEditor / ItemProperty / Undo boundaries;
4. AudioEffect / VideoEffect sample structure;
5. media source / Shape / Tachie / Voice entry points.

Each item must distinguish "public symbol exists" from "host invokes it with these lifecycle semantics."

### P2 — version-pinned host behavior

Promote reusable Lab facts such as:

- selection/input ordering;
- current-frame / preview observations;
- ItemTemplate identity ambiguity;
- command routes;
- layer/display-row behavior;
- `.ymme` update behavior;
- source-time / playback mapping;
- VoiceItem/VOICEVOX behavior.

Open/Draft experiments remain Candidate until accepted into the canonical Lab record.

### P3 — implementation guidance

Keep a separate guidance layer for:

- resource lifetime patterns;
- WPF rendering choices;
- JSON/source-generator patterns;
- buffering/allocation techniques;
- local debugging ergonomics.

This layer should say "consider" / "measure" rather than pretending these are YMM4 requirements.

## Corrections and caveats to carry forward

### Plugin-category lists must not look exhaustive

The maintained reference registry shows official sample coverage beyond the current long-form table, including ImageSource, AudioSpectrum, FileWriter, Localization, PropertyEditor, TextCompletion and Transition.

The AI reference should therefore use terms such as "common surfaces" unless the list is generated from a current authoritative source.

### Separate default members from required implementation members

Do not say every plugin interface necessarily requires an explicit implementation of every shared property without checking the current interface/default-member definition.

For code generation, "symbol is available" and "class must explicitly implement symbol" are different facts.

### Constructor-order warning for effect processors

When a base constructor invokes an overridable method, that override runs before the derived constructor body.

Therefore an effect processor pattern like:

```csharp
public Processor(..., Effect effect) : base(devices)
{
    this.effect = effect;
}
```

must not assume `this.effect` is initialized inside an override that `base(devices)` may call.

This is a C# construction-order rule, not a YMM4-specific API guarantee; keep it in implementation guidance adjacent to the VideoEffect starter.

### Reflection must be feature-specific

Replace generic "reach MainWindow.DataContext and search methods" advice with:

1. prove S1/S2 are insufficient for the requirement;
2. identify one exact S3 target;
3. pin the host version/shape;
4. cache only the validated adapter;
5. fail closed on missing/ambiguous members;
6. revalidate when that subsystem changes.

### Performance guidance must be measure-first

The long-form guide contains useful WPF/allocation ideas, but the AI reference should not automatically apply them to ordinary code.

Especially avoid converting the following into universal rules:

- every reusable brush/pen must be frozen;
- StreamGeometry is always preferable;
- fixed numeric `stackalloc` limits are universally safe;
- `CollectionsMarshal.AsSpan` should be preferred for ordinary list access.

These belong behind an observed hot path or explicit profiling goal.

## Source-to-reference migration state

- **Structure triage:** complete
- **P0 extraction:** first official baseline complete — see [YMM4_AI_P0_OFFICIAL_BASELINE.md](YMM4_AI_P0_OFFICIAL_BASELINE.md)
- **P1 extraction:** public/reference effect/editor pass complete — see [YMM4_AI_P1_PLUGIN_SURFACES.md](YMM4_AI_P1_PLUGIN_SURFACES.md)
- **P1B surface map:** official sample category/interface map complete — see [YMM4_AI_P1B_OFFICIAL_PLUGIN_SURFACE_MAP.md](YMM4_AI_P1B_OFFICIAL_PLUGIN_SURFACE_MAP.md)
- **P2 canonical Lab card extraction:** started
- **P3 guidance rewrite:** first pass complete — see [YMM4_AI_P3_IMPLEMENTATION_GUIDANCE.md](YMM4_AI_P3_IMPLEMENTATION_GUIDANCE.md)

The next pass should verify P0/P1 against current official documentation/samples and then replace generic index entries with narrow knowledge cards.
