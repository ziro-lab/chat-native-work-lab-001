# Music Fit Phase 5 — Checkpoint A (v0.2.0a1)

Status: experimental implementation for the first listening checkpoint, **not full Phase 5 completion**.

Design authority: [Phase 5 architecture](PHASE5_STRUCTURE_AWARE_PLANNER_DESIGN.md).
Design baseline commit: `eb301419efceb8df5cac77ad8012603d95c1c8ae`.
Initial PR base after rebasing unrelated lab work: `5748cc3f691ca4029e701c5fef0d145ff0d4ee2f`.

## Implemented

The existing `plans()` entry point accepts `phase=5`. Default `phase=4` remains unchanged.
`fit()` and the JSON worker support the same opt-in flag. `core.py::_graph_plans()` is the single search implementation; Phase 3/4 use its legacy profile, while Phase 5 Shorten A supplies a constrained profile.

- Two-scale boundary hints reuse existing whitened chroma/timbre and log-RMS energy. A rectangular signed checkerboard under dot-product similarity is computed from feature prefix sums, not a dense N x N matrix. A brute-force kernel equivalence test guards this calculation.
- Bounded section statistics retain RMS and sparse recurrence support. There are at most 32 peaks per scale and 24 ending candidates.
- Ending candidates include late coarse boundaries, fixed 8/12/16-second tails and legacy ending-entry fallbacks. Strength is heuristic, never a calibrated probability.
- Shorten A prunes non-forward jumps **before** shared search and rejects actual seams below the existing threshold. It allows up to `min(config.max_jumps, 4)` edits including the final ending bridge.
- First search substantial/structural source endings, then shorter source tails. Fade is searched only if neither tier found a source-end route. Budget exhaustion and cancellation still fail; they are not fallback success.
- One candidate is returned for Shorten A. B/C are not filled with near-duplicate paths. Larger/equal targets explicitly delegate to Phase 4 and say so in `planner.implementation`.
- `result.json` includes structural hints, planner version, attempted tiers, selected ending and fallback provenance. The local runner retains the report and its referenced preview/audio files after temporary work is removed.
- The local runner accepts only actually implemented phases 3/4/5 (previously it accepted unsupported 1/2). Phase 5 ending previews retain up to 24 seconds; Phase 4 remains 8 seconds.

## Deliberately not implemented yet

Automatic Intro endpoint scoring, Highlight candidates, Shorten B/C, diversity selection, dedicated Extend policy, selectable/applied Loop hypotheses, full final-occurrence/terminality estimation, fade-start detection and P1 loop-fatigue refinements remain future steps.

**Reason for the recurrence limit:** `Analysis.edges` is a sparse list of selected recurrence edges, not a complete recurrence matrix. Missing edges cannot establish that an outro is unique or that a section never recurs. Checkpoint A reports positive interval support only; it does not invent semantic terminality or use a fabricated full recurrence-change cue. Existing loop detection/audition metadata is preserved, but an interactive Loop selector is not claimed here.

The full design is unchanged as a goal. This checkpoint is its smallest executable Boundary → Ending → Shorten A slice, with the above unimplemented cues explicitly deferred.

## Run locally

From the core experiment:

```sh
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
python -m musicfit request.phase5.example.json
```

The example uses a new output directory. Never overwrite the source.

For private BGM, use the [local runner](../musicfit-local-realworld-001/README_JA.md):

```text
setup_windows.bat
run_windows.bat          # baseline Phase 4
run_phase5_windows.bat   # new Shorten A; targets in settings.phase5.json
```

There is no network access in fitting/analysis. Initial pip setup still downloads dependencies. No third-party decoder fallback has been added.

## Invariants and interpretation

All positions are original-source sample frames, exclusive-end, and exact output length is unchanged. Only exact frame equality takes the unchanged-source route; near equality must still satisfy the requested frame count. A one-frame shortening may use a safe one-frame forward bridge; the legacy Phase 4 bridge threshold is unchanged.

For source-end output, the chosen ending entry lies within the last continuous source span, whose end equals the source frame count. Shorten source positions are strictly forward at edits. The renderer still applies its original post-cut crossfade handles, global peak protection and 12 ms terminal safety fade. Therefore preserving an ending is an edit-map guarantee, **not bit-identical preservation of every ending sample**, semantic outro recognition or proof of human naturalness.

`keep_intro_seconds` remains the protected head length. `keep_outro_seconds` remains a lower bound for retained source-ending tails/eligible edits; a labelled fade fallback does not claim to retain the original ending. Impossible requested protection/duration combinations are rejected instead of clamped.

`no_source_end_route_in_bounded_search` means no route was found within the configured bounded search; it does not prove no valid route exists globally.

## Regression and evidence

`tests/legacy_phase34_golden.json` freezes eight canonical edit-map digests generated with the unmodified v0.1.1 core, not with the new implementation. Its baseline blob is `9a73c5bc1c29196df594e20b8b25fd8d6ab41c95`; canonical floating values are rounded to nine decimals before hashing. The golden source came from the verified Windows artifact `10558097162`, run `35370982684`, ZIP SHA256 `cfbc17c9d5682edc85f83d7c58387ada5e0b02bc0f52e76dba6e0d566977134b`. Core/fixture source blobs were matched to the base commit after CRLF normalization.

Tests cover mathematical novelty equivalence, single-modality boundaries, silence/slow-change abstention, bounded output size, no Analysis mutation, exact duration, forward-only maps, source-end-before-fade, explicit short-tail fallback, phase validation, equal/near-equal duration, legacy Extend delegation, original source immutability, cancellation cleanup, UTF-8 worker IPC and retained local reports with isolated bad-MP3 failure.

**PASS boundary:** only assertions actually executed in the recorded run. CI identity and package digests belong in the PR evidence record; do not assume an unexecuted workflow passes.

**NOT PROVEN:** human acceptance improvement, semantic Intro/Highlight/Outro accuracy, full Phase 5 completion, arbitrary MP3 decoding, native YMM4 plugin integration or executable bundle size.

## Next listening checkpoint

Compare the same private BGM/target against Phase 4. Keep the existing overall/ending ratings; inspect planner details only when a failure needs diagnosis. If source-end A improves the perceived completion, proceed to Intro/Highlight and families B/C. No additional MIR models or permanent analysis cache are prerequisites.
