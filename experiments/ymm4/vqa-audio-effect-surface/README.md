# VQA vNext Audio Effect Surface Gate

Status: **GREEN / NATIVE AUTOMATED EVIDENCE**

## Result

Voice Quality Assist can use the normal VoiceItem **Audio Effects** collection as a durable settings host on YMM4 Lite 4.56.1.0 without Harmony or private collection traversal.

Final accepted evidence:

- tested source: `ea0ecae8925c40adcd7bb28c4cac7559468da4ef`
- workflow run: `36021832446`
- job: `107708395267`
- artifact: `10817525881`
- artifact SHA256: `70a2abf469378bc2a074107e8e1cc68e5e9f8b9937323aef6878ec30218feb7a`
- host: YMM4 Lite 4.56.1.0
- host ZIP SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- final marker: `PASS_VQA_AUDIO_EFFECT_SURFACE_E2E`

All 14 native assertions passed.

## Goal

Determine whether Voice Quality Assist can move its durable per-VoiceItem settings from the subtitle/video-effect collection to the normal **Audio Effects** area without brittle private reflection or Harmony.

Preferred product shape:

`VoiceItem -> Audio Effects -> Pronunciation Assist`

## Proven host surface

The real pinned host exposes:

- `VoiceItem.AudioEffects` as
  `ImmutableList<IAudioEffect>`;
- public getter and public setter;
- normal `AudioEffectSelectorAttribute` metadata on the property;
- public add/remove through immutable collection operations + public property assignment;
- custom `AudioEffectBase` discovery through the normal plugin surface;
- Item Editor `Audio effects` UI;
- normal per-effect property editors after selecting the effect in `AudioEffectSelector`;
- `IsEnabled` notification;
- custom property notification;
- real project save/reload;
- public enumeration after reload;
- pass-through audio processing;
- membership Undo/Redo through the normal host UndoRedoManager route.

The custom probe effect is absent from `JimakuVideoEffects`, proving that the test exercised the audio-effect path rather than the legacy subtitle/video-effect path.

## Proven assertions

1. `custom_audio_effect_host_loaded`
2. `voice_audio_collection_public_enumerable`
3. `audio_effect_public_attach`
4. `audio_effect_not_in_subtitle_collection`
5. `effect_enabled_notification`
6. `effect_custom_property_notification`
7. `pass_through_sample_identity`
8. `audio_effect_membership_undo_redo`
9. `audio_effect_visible_in_item_editor`
10. `audio_effect_property_editors_visible`
11. `audio_effect_settings_save_reload`
12. `audio_effect_public_enumeration_after_reload`
13. `audio_effect_public_remove`
14. `remove_preserves_ordinary_voice_source`

All passed at the accepted source.

## UI observation

The `Audio effects` section is backed by YMM4's normal `AudioEffectSelector`.

The attached custom effect appears as a real selector list item whose DataContext is the custom `ProbeAudioEffect`. Selecting that list item materializes the effect's normal properties editor; the probe then observes both representative typed settings:

- string setting: `入力記号`;
- enum setting: `モード`.

Earlier failed runs were probe/harness issues rather than host limitations:

- startup dialogs initially prevented `TimelineToolInfo` delivery;
- reload initially inspected a stale pre-open Timeline;
- Item Editor observation initially failed to select the attached effect in `AudioEffectSelector`.

Those harness issues were corrected before the accepted GREEN run.

## Persistence observation

The saved project contains the custom effect under `AudioEffects`, including:

- custom effect type;
- `Token`;
- `Mode`;
- `IsEnabled`.

After reopening the project, a different live `VoiceItem` object exposes the restored effect through the public audio-effect collection. The effect can then be removed through the same public collection/property route without changing ordinary VoiceItem source state.

## Product consequence

The vNext storage decision is **GREEN**:

- introduce an Audio Effect storage adapter;
- dual-read the legacy subtitle/video Effect and the new audio Effect;
- new-write the Audio Effect;
- migrate candidate state only after the new audio Effect is fully configured/attached;
- preserve `IsEnabled`, helper rules/settings, prosody, and future typed settings;
- remove the old Effect only after the new one is safely attached;
- keep the migration user-visible and Undo-able.

The runtime/core should not directly depend on `JimakuVideoEffects` once the storage adapter is introduced.

## PASS means

This PASS proves that Audio Effects is a viable durable settings host for this plugin on YMM4 Lite 4.56.1.0 using product-acceptable public host surfaces.

## NOT PROVEN

This experiment does not prove:

- final product migration implementation;
- final Pronunciation Assist UI wording/layout;
- compatibility with future YMM4 versions;
- subjective audio quality;
- behavior of unrelated third-party audio effects.

Those remain product integration/acceptance work.
