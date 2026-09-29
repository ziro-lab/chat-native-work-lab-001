# VoiceItem regeneration lifecycle — YMM4 4.56.1.0

## Goal

Find and validate the real YMM4 host route used to regenerate a `VoiceItem` after pronunciation data has been patched.

This experiment is the next slice after:

- PR #116: mutable VOICEVOX pronunciation graph + `IVoiceItemEditService.CreateVoiceFileAsync(bool force)`
- PR #125: modified VOICEVOX pronunciation can reach `/synthesis` through public `IVoiceSpeaker.CreateVoiceAsync`

## First slice

Inventory the concrete `VoiceItem -> IVoiceItemEditService` acquisition path and the relevant public host surface on real YMM4 Lite 4.56.1.0.

The same experiment will then be advanced to:

1. generate a real VoiceItem,
2. patch its pronunciation,
3. force regeneration,
4. verify the patched pronunciation reaches synthesis without being replaced.

## PASS boundary

The initial inventory PASS only proves that the host surface was observed successfully. It does **not** yet prove regeneration or persistence.

## Not proven yet

- real VoiceItem generate -> patch -> regenerate roundtrip
- Undo / Redo
- save / reload
- assist Effect disable / removal behavior
