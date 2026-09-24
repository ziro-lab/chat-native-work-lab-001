# VQA vNext Forced Boundary — Transient Comma Gate

Status: **PREP / NOT YET PROVEN**

## Goal

Prove the exact product requirement for Voice Quality Assist vNext:

> a user marks a clean-text boundary, the plugin temporarily presents that boundary to VOICEVOX as a Japanese comma so VOICEVOX creates a phrase boundary and performs normal automatic accent analysis on each side, then only the plugin-injected comma pause is reduced to zero.

This experiment is intentionally different from the earlier zero-pause experiment that only targeted a pre-existing AccentPhrase boundary.

## Environment

- YMM4 Lite 4.56.1.0
- .NET 10
- pinned YMM4 ZIP SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

Use a deterministic fake VOICEVOX endpoint for structural assertions and a real VoiceItem in the real YMM4 host.

## Canonical user fixture

Baseline Serif:

`これは、テスト音声です実験のために生成しました。`

Forced-boundary Serif:

`これは、テスト音声です実<w0>験のために生成しました。`

Expected transient analysis input must be equivalent to:

`これは、テスト音声です実、験のために生成しました。`

The inserted comma is analysis-only and must not be persisted into Hatsuon.

## PASS assertions

PASS requires all of the following.

1. YMM4 ControlTagParser returns clean text without `<w0>` and the exact clean-text boundary position.
2. The product-style planner builds a transient input with one plugin-injected `、` at that boundary.
3. VOICEVOX analysis returns an additional phrase/pause boundary at the injected position.
4. The existing source comma after `これは` remains independently identifiable.
5. The plugin-injected comma maps to exactly one PauseMora.
6. Only that injected PauseMora is mutated to `VowelLength = 0`.
7. The original source comma retains its normal non-zero pause.
8. Final synthesis succeeds through the normal public voice route.
9. Durable Serif remains exactly the source with `<w0>`.
10. Durable Hatsuon contains neither `<w0>` nor the injected analysis comma.
11. Two forced markers produce two distinct injected phrase boundaries and two zero-length injected pauses.
12. Ambiguous or non-unique mapping fails closed rather than zeroing an arbitrary comma.
13. Helper-mora transient insertion can coexist with forced-boundary punctuation without losing injected-boundary identity.
14. Save/reload restores the durable marker and re-derives the same forced-boundary behavior.

## Evidence to record

The artifact should include:

- baseline request/response summary;
- forced-boundary request/response summary;
- original-vs-injected PauseMora observations;
- durable Serif/Hatsuon before/after;
- multi-marker case;
- helper + forced-boundary case;
- result.json;
- provenance.json.

## PASS means

A PASS proves the structural product mechanism:

`forced boundary intent -> transient comma -> VOICEVOX automatic phrase split -> injected pause only = 0`

It does not prove perceived naturalness through a physical speaker.

## NOT PROVEN

This experiment does not prove:

- the best user-facing marker character;
- property-editor UX;
- Audio Effect placement;
- compatibility with every synthesis provider;
- subjective speech quality.

Those are separate acceptance layers.
