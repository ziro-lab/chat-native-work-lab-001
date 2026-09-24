# VQA vNext Forced Boundary — Transient Comma Gate

Status: **GREEN / NATIVE AUTOMATED EVIDENCE**

## Result

Voice Quality Assist vNext can implement the forced-boundary semantic required by the product:

`forced boundary intent -> transient comma -> VOICEVOX automatic phrase split -> injected pause only = 0`

Final accepted evidence:

- tested source: `592115e48f67a0f52fb829fc70ff4e26d411a4e5`
- workflow run: `36013239960`
- job: `107679049115`
- artifact: `10813671222`
- artifact SHA256: `6320601ffc2e5f383bbcf4d78b0b022c7697a17f15b28798624f1b1192180f8d`
- host: YMM4 Lite 4.56.1.0
- host ZIP SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- final marker: `PASS_VQA_FORCED_BOUNDARY_TRANSIENT_COMMA_E2E`

All 14 required assertions passed.

## Goal

Prove the exact product requirement for Voice Quality Assist vNext:

> a user marks a clean-text boundary, the plugin temporarily presents that boundary to VOICEVOX as a Japanese comma so VOICEVOX creates a phrase boundary and performs normal automatic accent analysis on each side, then only the plugin-injected comma pause is reduced to zero.

This experiment is intentionally different from the earlier zero-pause experiment that only targeted a pre-existing AccentPhrase boundary.

## Canonical user fixture

Baseline Serif:

`これは、テスト音声です実験のために生成しました。`

Forced-boundary Serif:

`これは、テスト音声です実<w0>験のために生成しました。`

Observed transient analysis input is equivalent to:

`これは、テスト音声です実、験のために生成しました。`

The injected comma is analysis-only. It does not replace the source comma and does not persist as product source state.

## Proven assertions

1. YMM4 `ControlTagParser` returns clean text without `<w0>` and the exact clean-text boundary position.
2. The product-style planner builds a transient input with one plugin-injected `、` at that boundary.
3. VOICEVOX analysis returns an additional phrase/pause boundary at the injected position.
4. The existing source comma after `これは` remains independently identifiable.
5. The plugin-injected comma maps to exactly one `PauseMora`.
6. Only that injected `PauseMora` is mutated to `VowelLength = 0`.
7. The original source comma retains its normal non-zero pause.
8. Final synthesis succeeds through the normal public voice route.
9. Durable Serif remains exactly the source with `<w0>`.
10. Durable Hatsuon contains neither `<w0>` nor the injected analysis comma.
11. Two forced markers produce two distinct injected phrase boundaries and two zero-length injected pauses.
12. Ambiguous/non-unique mapping fails closed rather than zeroing an arbitrary comma.
13. Helper-mora transient insertion coexists with forced-boundary punctuation without losing injected-boundary identity.
14. Save/reload restores the durable marker and re-derives the same forced-boundary behavior.

## Product consequence

The old A1 meaning is superseded for vNext.

Product implementation may use the official durable `<w0>` marker as correction intent, derive transient punctuation for fresh VOICEVOX analysis, resolve injected phrase ends one-to-one, and mutate only the corresponding injected pauses.

Existing source punctuation is not a correction target.

The resolver must continue to fail closed on ambiguity, missing pause data, incompatible readings, or non-unique mapping.

## Evidence artifact

The accepted artifact contains:

- baseline and forced-boundary request/response observations;
- original-vs-injected `PauseMora` observations;
- durable Serif/Hatsuon evidence;
- multi-marker case;
- ambiguity/fail-closed case;
- helper + forced-boundary case;
- save/reload re-derivation;
- `result.json`;
- `e2e.json`;
- `provenance.json`;
- fake VOICEVOX request log.

## PASS means

This PASS proves the structural product mechanism on YMM4 Lite 4.56.1.0 with a deterministic VOICEVOX-compatible test endpoint and a real YMM4 `VoiceItem`.

It proves that the host/public surfaces required by the product mechanism are viable.

## NOT PROVEN

This experiment does not prove:

- the best user-facing marker character;
- final property-editor UX;
- compatibility with every synthesis provider;
- subjective speech quality or naturalness;
- future YMM4 version compatibility.

Those remain separate acceptance layers.
