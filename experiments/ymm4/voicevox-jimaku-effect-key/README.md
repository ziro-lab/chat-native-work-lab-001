# Jimaku effect as VOICEVOX pronunciation-assist key — YMM4 4.56.1.0

## Goal

Test the proposed product skeleton:

> A custom subtitle (JimakuVideoEffects) effect attached to a VoiceItem acts as the per-item opt-in key/settings container for pronunciation assistance, while the plugin uses editor context to reach YMM4's standard voice-regeneration service.

This experiment intentionally does not implement control-character parsing, subtitle stripping, VOICEVOX mora mutation, or perceptual audio evaluation.

## Questions

1. Can a custom VideoEffect be stored in a real VoiceItem's JimakuVideoEffects collection?
2. Can plugin code reliably detect the effect and respect its IsEnabled state as the opt-in key?
3. When that effect is shown in the VoiceItem editor, does its custom property editor receive IEditorInfo?
4. Does that editor context expose VoiceItemEdit and allow CreateVoiceFileAsync(force:true) to complete through YMM4's standard route?

## Environment

- GitHub Actions windows-latest
- YMM4 Lite 4.56.1.0
- host ZIP SHA256 49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a
- real YMM4 process
- a synthetic text VoiceItem created through YMM4's own MainModel voice-add path
- no user project/assets

## PASS boundary

PASS proves only the host integration skeleton above on the pinned version.

## NOT PROVEN

- filtering control markers from the rendered subtitle;
- VOICEVOX-specific pause/vowel/consonant mutation;
- exact lifecycle timing for automatic regeneration after arbitrary text edits;
- save/reload or Undo/Redo persistence;
- behavior when the effect UI is never materialized;
- behavior on other YMM4 versions.