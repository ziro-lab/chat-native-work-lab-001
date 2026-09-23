# VOICEVOX direct synthesis round-trip — YMM4 4.56.1.0

## Goal

Prove that YMM4's built-in VOICEVOX speaker can synthesize from an already-supplied, modified pronunciation object without re-running audio_query.

A local fake VOICEVOX HTTP server records every request. The probe constructs a VOICEVOX AudioQuery with a pause mora whose vowel_length is exactly 0, wraps it as IVoicePronounce, and calls the built-in speaker through IVoiceSpeaker.CreateVoiceAsync.

## Required assertions

1. Built-in VOICEVOX speaker can be constructed against a local URL.
2. The supplied pronunciation contains pause vowel length 0 before synthesis.
3. CreateVoiceAsync completes and writes a WAV.
4. Fake backend receives /synthesis.
5. Fake backend receives zero pause duration in the synthesis JSON.
6. Fake backend receives no /audio_query request during this direct-pronounce synthesis.
7. Returned pronunciation remains a VOICEVOX pronunciation object.

This slice deliberately bypasses initial text analysis. It proves the critical second half:
modified Pronounce -> built-in YMM4 VOICEVOX synthesis route.
