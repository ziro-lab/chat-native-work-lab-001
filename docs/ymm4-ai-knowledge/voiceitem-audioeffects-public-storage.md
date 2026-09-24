# VoiceItem AudioEffects is a public persistent effect-storage surface

- Status: evidence-qualified
- Repository state: Draft PR #142
- Knowledge class: LAB-NATIVE
- Surface: S1 / public VoiceItem AudioEffect surface
- YMM4 version: 4.56.1.0 Lite
- Tested source: `ea0ecae8925c40adcd7bb28c4cac7559468da4ef`
- Revalidation trigger: YMM4 changes VoiceItem.AudioEffects or Item Editor Audio effects behavior

## Claim

On the tested YMM4 4.56.1.0 host, a custom `AudioEffectBase` attached through public `VoiceItem.AudioEffects` was:

- discoverable by the host;
- enumerable/addable/removable through the public collection;
- stored separately from `JimakuVideoEffects`;
- observable through `IsEnabled` and custom-property notifications;
- represented by the real Item Editor's `Audio effects` selector and typed property editors;
- included in membership Undo/Redo;
- persisted through real project save/reload and publicly enumerable after reload;
- removable without damaging ordinary VoiceItem source state.

No Harmony/private collection traversal was required for the proven surface.

## Safe use

For VoiceItem-specific assist metadata/behavior that naturally belongs to an audio effect, public `VoiceItem.AudioEffects` is a viable persistent host surface on the tested version.

When migrating from another storage surface, attach/configure the new effect successfully before removing the old representation.

## Do not infer

- This does not prove every custom AudioEffect DSP behavior.
- It does not prove automatic migration from arbitrary legacy subtitle effects.
- It does not replace downstream product migration/UX acceptance.
- It does not prove future-version behavior.

## Failure behavior

If the public collection or effect host discovery is unavailable on a target version, keep the prior durable representation rather than destructively migrating.

## Evidence

- Lab PR: [#142](https://github.com/ziro-lab/chat-native-work-lab-001/pull/142)
- Host ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Workflow run: `36021832446`
- Native job: `107708395267`
- Artifact: `10817525881`
- Artifact SHA256: `70a2abf469378bc2a074107e8e1cc68e5e9f8b9937323aef6878ec30218feb7a`
- Marker: `PASS_VQA_AUDIO_EFFECT_SURFACE_E2E`
