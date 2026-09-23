# VoiceItem <w0> VOICEVOX path — YMM4 4.56.1.0

## Question

Before implementing the A1 boundary resolver, determine whether YMM4 itself turns an official zero-wait control tag into a distinct VOICEVOX analysis/synthesis structure.

Three real Timeline VoiceItems are compared with the same fake VOICEVOX speaker:

1. baseline: `Serif = アイ`, `Hatsuon = アイ`
2. Serif-tagged: `Serif = ア<w0>イ`, `Hatsuon = アイ`
3. Hatsuon-tagged: `Serif = アイ`, `Hatsuon = ア<w0>イ`

The fake backend records the exact HTTP request sequence between explicit test-only case markers.

## PASS boundary

PASS means:

- all three real VoiceItems complete normal `CreateVoiceFileAsync()`;
- the official parser sees the expected zero-wait marker in the Serif-tagged case;
- each case has an isolated backend request trace and at least one synthesis request;
- VoiceItem/Hatsuon/Pronounce state is recorded.

PASS does **not** mean the marker already solves boundary mapping. The recorded differences determine the next product design.

## Why this matters

If Serif-only `<w0>` already changes YMM4's VOICEVOX request/Pronounce structure, A1 should reuse that host structure.

If it does not, the product needs an explicit semantic resolver rather than pretending clean-Serif character offsets are VOICEVOX mora indexes.
