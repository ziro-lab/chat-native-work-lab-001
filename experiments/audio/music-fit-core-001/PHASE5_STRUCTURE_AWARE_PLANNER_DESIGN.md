# Music Fit Phase 5 — Structure-aware Planner Design

**Status:** design only; **not implemented**.  
**Baseline:** Music Fit Core v0.1.1 / Phase 4.  
**Design date:** 2026-09-19.

This document defines the next Music Fit architecture without widening the current Phase 4 PASS boundary. It records implementation intent, not measured quality.

## Goal

Keep the existing CPU-only Music Fit core as the lower-level connection/rendering engine and add a thin layer that decides:

- what source material should remain,
- what may repeat,
- what may be skipped,
- where the arrangement should enter the original ending,
- and how meaningfully different alternatives should be generated.

The product-facing modes are:

```text
Music Fit
├─ Loop
├─ Extend
└─ Shorten
```

Phase numbers remain development/internal concepts. The eventual YMM4 UI should expose user intent, not "Phase 3/4/5".

## Preserve the existing core

Phase 5 does **not** replace the following implemented components:

- recurrence discovery,
- harmonic / texture fallback,
- transition scoring,
- waveform registration,
- constant-sum crossfade,
- exact target-duration accounting,
- backward / forward transition candidates,
- source-end bridges,
- bounded graph search,
- renderer,
- stereo / PCM24 behavior,
- cancellation, digest and resource guards.

The working assumption is that local seam handling is already sufficiently developed that the next quality bottleneck is arrangement structure.

## New layers

Only four essential additions are planned:

1. multi-scale boundary hints,
2. intro / highlight / ending candidates,
3. mode-specific planner policies,
4. family-first candidate selection with a simple diversity guard.

Do **not** build a semantic music-structure recognizer.

Music Fit does not need hard labels such as Verse, Chorus, Bridge or Outro. It needs application-specific hints such as:

- "this looks like a meaningful boundary",
- "preserve the head through roughly here",
- "this section is worth retaining in a short version",
- "entering here gives a plausible path through the original source ending".

## Target architecture

```text
Audio
  ↓
Existing Analysis
├─ chroma / harmonic features
├─ timbre / spectral features
├─ energy
├─ feature change
├─ recurrence
└─ transition candidates
  ↓
StructureHints
├─ boundaries[]
├─ sections[]
├─ intro_candidates[]
├─ highlights[]
└─ ending_entries[]
  ↓
Arrangement Policy
├─ LoopPolicy
├─ ExtendPolicy
└─ ShortenPolicy
  ↓
Shared Graph Search
  ↓
Candidate Families
├─ A
├─ B
└─ C + diversity guard
  ↓
Existing Renderer
  ↓
Preview / Review
```

Graph search, transition scoring, duration solving and rendering stay shared. Policies constrain the edge set and planner state.

## StructureHints

StructureHints should initially be computed from the existing Analysis object rather than making Analysis itself much larger.

Conceptual schema:

```text
StructureHints {
    boundaries: [
        {
            frame,
            strength,
            scale,       // fine | coarse
            confidence
        }
    ]

    sections: [
        {
            start,
            end,
            energy_rank,
            recurrence_rank,
            contrast_rank
        }
    ]

    intro_candidates: [
        {
            end_frame,
            confidence
        }
    ]

    highlights: [
        {
            start,
            end,
            score,
            energy_score,
            recurrence_score,
            contrast_score
        }
    ]

    ending_entries: [
        {
            frame,
            confidence,
            kind
        }
    ]
}
```

Hints are not ground truth. Keep multiple candidates where useful and retain explicit fallback behavior.

## Boundary hints — P0

Reuse existing features to build a lightweight fused novelty signal:

```text
chroma novelty
+ timbre novelty
+ energy change
+ recurrence change
```

Normalize cues robustly before fusion. Do not start with learned weights.

Use only two scales:

- **fine** — phrase-ish / local structural boundaries; useful around loop endpoints,
- **coarse** — larger structural changes; useful for intro, shortening and ending candidates.

Candidate extraction only needs peak prominence, minimum spacing and nearby-peak merging at first.

Do not add Laplacian / spectral clustering in P0. Re-open it only if simple novelty finds boundaries but fails because repeated global sections cannot be represented well enough.

## Intro hints

Do not classify "Intro".

Define an intro candidate as:

> A point through which continuous playback from source start is likely to preserve a coherent song opening.

Score early coarse boundaries using cheap cues such as:

- boundary strength,
- pre/post texture contrast,
- recurrence becoming stronger after the boundary,
- optional energy-rise evidence.

The particularly useful pattern is:

```text
relatively unique head
→ boundary
→ more recurrent main body
```

Return multiple `intro_candidates[]` when appropriate.

The current fixed `keep_intro_seconds` remains the fallback, so uncertain structure analysis does not break existing behavior.

## Highlight hints

Do not build a Chorus detector.

Define a Highlight as:

> A source section worth retaining when the song must be shortened.

Score whole sections, not isolated frames.

P0 cues:

```text
relative energy
+ recurrence / repetition strength
+ local contrast
+ late reappearance bonus
```

A short loud spike must not automatically win. Use section-level scoring, nearby-candidate suppression and keep only a small number of candidates.

Novelty itself is primarily a boundary cue and should not dominate highlight score.

## Ending hints — P0

Ending quality is the highest-ROI structural improvement for Shorten.

Retain the current fixed tails and late novelty candidates as fallbacks, but place better candidates above them using:

- late coarse boundaries,
- source-end reachability,
- terminal recurrence evidence,
- final-occurrence evidence,
- optional energy-decay / fade evidence.

Do not force one "correct outro start". Return multiple candidates.

Example:

```text
Ending candidates
1. coarse boundary → source_end
2. final recurrent section → source_end
3. fade start → source_end
4. fixed fallback → source_end
```

A simple fade detector may be added in P1 using smoothed log-RMS slope and endpoint level drop. No model is needed.

## Loop mode

Loop remains an independent user-facing capability, not merely an internal Phase 4 primitive.

Existing `analyze()` / `audition_edges()` stay the basis.

P0 keeps:

- recurrence similarity,
- transition score,
- waveform registration,
- crossfade,
- period diversity.

The UI/output layer should make the best loop hypotheses auditionable/selectable rather than exposing them only as diagnostic metadata.

Avoid three near-identical loop hypotheses from the same source region.

P1 quality improvements:

- prefer a longer loop when transition quality is otherwise comparable,
- penalize excessive repeat count / repetition fatigue,
- prefer loop endpoints near useful fine/coarse boundaries.

Do not add beat tracking yet.

## Extend policy

Extend means:

> Preserve the source progression as much as possible while revisiting earlier material to reach a longer target.

### Default edge set

```text
normal forward playback
+ backward recurrence jumps
+ path to original source ending
```

Non-linear forward jumps are disabled by default.

### Hard constraints

- start from source frame 0,
- no jump inside the protected intro region,
- exact target duration,
- transition/seam threshold,
- minimum dwell between edits,
- maximum edit count,
- preserve/reach the original ending when feasible,
- no jump after entering the selected ending.

### Soft preferences

- fewer edits,
- longer repeated regions,
- fewer repetitions of the same region,
- boundary-aligned repeats,
- avoid repeating intro,
- avoid repeating ending,
- preserve chronological progression between repeats.

### Extend candidate families

**A — Simple Loop**

- one repeat type,
- prefer a longer natural loop,
- minimum edit count,
- seam quality first.

**B — Structure Preserve**

- one or two backward jump types,
- preserve the original flow,
- strongly prefer the original ending.

**C — Alternate Repeat**

- repeat a structurally different source region from A/B,
- emit only when it is meaningfully different.

Never manufacture a third candidate solely to fill A/B/C.

## Shorten policy

Shorten is not "find a good seam". It is:

> Decide which source storyline can fit the target duration and still sound like a completed track.

### Default edge set

```text
normal forward playback
+ forward recurrence jumps
+ explicit ending-entry transitions
```

Non-linear backward jumps are disabled by default.

This keeps the source timeline effectively monotonic and removes routes such as:

```text
forward skip → backward jump → another forward skip
```

which can be locally cheap but structurally strange.

### Hard constraints

For a normal target:

- start at source frame 0,
- play continuously through the selected intro candidate,
- non-linear jumps are forward only,
- minimum dwell,
- exact target duration,
- once the selected ending entry is reached, play continuously to source end,
- no jump after entering the ending.

### Soft preferences

- small edit count,
- avoid tiny skips,
- align jump endpoints near useful boundaries,
- retain high-salience sections,
- prefer stronger ending candidates,
- avoid severe energy-story discontinuities.

Shorten should use a relatively strong edit-count penalty. A few large, explainable skips are generally preferable to many locally good edits.

## Shorten candidate families

### A — Structure Preserve

Hard:

- intro preserved,
- forward-only non-linear jumps,
- original ending when feasible.

Preferences:

- minimum edit count,
- original progression,
- Highlight is soft.

Typical shape:

```text
Intro → A → skip → B → Ending
```

### B — Highlight Preserve

Hard:

- intro preserved,
- at least one Highlight visited,
- forward-only non-linear jumps,
- original ending when feasible.

A more aggressive skip is allowed.

Planner state only needs small additions such as:

```text
highlight_seen : bool
ending_entered : bool
```

Typical shape:

```text
Intro → Highlight → Ending
```

### C — Alternate Reconstruction

C must be structurally different from A/B. Do not obtain it by changing weights slightly.

Require at least one of:

- different jump section pair,
- sufficiently different section coverage.

If no meaningful alternative exists, omit C.

## Candidate diversity — P0

Do not start with a complex edit-map metric, MMR or DPP.

### Jump pair difference

Quantize each non-linear transition as:

```text
(source_section_id, destination_section_id, direction)
```

### Section coverage

Record how much output time comes from each source section:

```text
coverage[section_id] = output duration contributed by that section
```

A simple duration-weighted overlap/Jaccard-like similarity is enough initially.

Candidate C is accepted only when:

```text
jump pair differs
OR
coverage similarity < threshold
```

Tune the threshold through real listening tests.

## Short-target fallback ladder

Do not push all impossible target cases into one cost function.

Relax constraints explicitly:

```text
Intro + Highlight + Ending
↓
shorter Intro candidate + Highlight + Ending
↓
Intro + Ending
↓
short head + best Ending
↓
boundary-aligned graceful fade
```

For Shorten, a fade ending is a final fallback rather than a normal peer of source-end arrangements.

## Auto mode

The eventual host may expose:

```text
Auto / Loop / Extend / Shorten
```

Auto:

```text
target > source      → Extend
target < source      → Shorten
approximately equal  → minimal/no edit
```

Loop remains explicitly selectable because a user may want a clean repeat rather than a reconstruction.

## Compatibility and baseline

Do not immediately remove Phase 3 or Phase 4.

During development keep:

```text
phase=3
phase=4
phase=5
```

so Phase 4 remains the regression/listening baseline until Phase 5 is demonstrated on real local BGM.

The final YMM4 UI should not expose phase numbers.

## Machine-checkable invariants

Automate structural contracts so human listening can focus on musical quality.

Check at least:

- exact duration,
- continuous intro preservation,
- original ending preservation,
- Highlight passage for the Highlight family,
- allowed jump direction,
- protected-region rules,
- seam threshold,
- family edit-count limits,
- minimum dwell,
- repeat-count limits,
- monotonic source order for Shorten,
- A/B/C diversity,
- ending reachability,
- rejection of pathological micro-loop repetition.

For Shorten, the output-to-source map should be monotonically increasing apart from ordinary continuous playback mapping; non-linear source jumps must be forward.

## Human listening remains required

Do not convert these questions into fake confidence scores:

- Does the result sound like one completed track?
- Does it still feel like the song reached a meaningful peak?
- Does the transition into the ending feel intentional?
- Does rhythm/phrase flow stumble?
- Does a loop become annoying after repeated cycles?
- Does Shorten sound like a composition rather than a cut-off source?
- Do A/B/C actually sound like different choices?

Compare Phase 4 baseline vs Phase 5 blindly where practical.

Also add a separate question:

> Do A/B/C sound meaningfully different from each other?

## Synthetic fixtures

Synthetic fixtures test planner contracts, not full musical naturalness.

Minimum fixture set:

| Fixture | Structure | Expected machine behavior |
|---|---|---|
| Basic Shorten | `Intro-A-B-A-Outro` | preserve Intro, forward skip, preserve Outro |
| Highlight Shorten | `Intro-A-B-A-C-Outro` | visit configured/high-scoring Highlight |
| Basic Extend | `Intro-A-B-A-B-Outro` | backward repeat then return to source end |
| Unique Ending | `A-B-A-uniqueOutro` | uniqueOutro boundary becomes ending candidate |
| Final Chorus | `A-B-A-B-Coda` | final B start may be an ending candidate |
| Fade | `A-B + decreasing RMS tail` | detect fade-start candidate |
| Loud Spike Trap | moderate repeated A + short loud spike | do not make the spike the only Highlight |
| Short Loop Trap | 2 s recurrence + 20 s recurrence | prefer longer loop if quality is comparable |
| Boundary Modalities | chroma-only / timbre-only / energy-only change | fused boundary reacts appropriately |
| Ambient / weak structure | slow continuous change | avoid inventing many high-confidence sections |
| Diversity | multiple safe jump pairs | families produce meaningfully different routes |

The ambient case is especially important: low-confidence structure should fall back to the current core rather than fabricate confident Intro/Highlight/Ending claims.

## Implementation sequence

### Step 0 — Freeze Phase 4 baseline

Keep current tests and behavior as the comparison baseline.

### Step 1 — StructureHints data model

Implement data structures and expose/debug them without changing planning.

### Step 2 — Boundary hints

Implement fine/coarse fused novelty using existing features.

Inspect hints on local BGM before wiring them into the planner.

### Step 3 — Ending hints

Add late-boundary / terminality / final-occurrence ending candidates.

Run a real listening checkpoint here because Ending directly targets the current "cut-off song" failure.

### Step 4 — Shorten Family A

Implement the smallest forward-only reconstruction:

```text
intro fallback
→ forward-only skips
→ selected ending
```

Compare against Phase 4 before adding more features.

### Step 5 — Intro + Highlight hints

Add candidate scorers after Family A proves the structural direction useful.

### Step 6 — Shorten Family B

Add `highlight_seen` state and require a Highlight.

### Step 7 — Shorten Family C + diversity

Use jump-section pairs and section coverage only.

### Step 8 — Extend policy

Reuse the same graph search with backward-first edge policy.

### Step 9 — Loop UX / P1 loop quality

Make loop candidates directly selectable/auditionable, then add only:

- long-loop preference,
- repeat-count penalty,
- boundary proximity.

### Step 10 — Failure classification before more MIR

Classify real failures into categories such as:

```text
seam click
harmonic mismatch
texture mismatch
rhythm phase
phrase cut
repetition fatigue
bad arrangement
bad ending
```

Only add rhythmic/beat analysis if `rhythm phase` remains a material real-world failure.

## Deferred / unnecessary now

Do not add these in Phase 5 P0:

- beat tracker,
- downbeat / bar tracker,
- BPM-only detector,
- key detector,
- chord recognition,
- Verse/Chorus classifier,
- deep semantic structure model,
- neural embeddings,
- stem separation,
- generative music,
- Laplacian / spectral clustering,
- complex edit-map diversity optimizer.

These are not permanently forbidden. Re-open them only when a measured failure cannot be solved with the cheaper architecture.

## Phase 5 completion definition

Phase 5 is complete when the architecture can demonstrate, through machine checks plus local listening:

- **Loop:** natural loop hypotheses are directly selectable/auditionable;
- **Extend:** backward-first repetition can extend a track while preserving the original flow and ending where feasible;
- **Shorten:** a long track can be reconstructed as a shorter completed arrangement rather than simply fading at an arbitrary cutoff;
- **Candidates:** A/B/C are family-first, meaningfully different alternatives rather than neighboring top-k paths;
- **Core:** existing lightweight CPU, rendering and safety boundaries remain intact.

Phase 5 is **not** complete merely because it recognizes more musical labels or produces a more complex structure model.
