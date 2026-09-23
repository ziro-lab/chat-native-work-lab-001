# VOICEVOX fake-API regeneration — YMM4 4.56.1.0

## Goal

Run YMM4's built-in VOICEVOX speaker end-to-end against a tiny localhost VOICEVOX-compatible fake API.

The critical product question is:

> If a generated VOICEVOX Pronounce/AudioQuery is modified (for example PauseMora.VowelLength = 0), does a second built-in CreateVoiceAsync call reuse that Pronounce and send the modified query to synthesis without rebuilding it?

## Native sequence

1. Start a localhost fake VOICEVOX HTTP API.
2. Construct a public VOICEVOXEngine pointing at it.
3. Construct a minimal VOICEVOXCharacter and the built-in VOICEVOXVoiceSpeaker.
4. Call CreateVoiceAsync with pronounce = null.
5. Verify a VOICEVOXVoicePronounce / AudioQuery is returned.
6. Set the returned PauseMora.VowelLength from 0.2 to 0.
7. Call CreateVoiceAsync again with that pronounce.
8. Verify the second synthesis request contains pause_mora.vowel_length = 0.
9. Verify the second call does not need a fresh /audio_query request.

No Harmony and no real VOICEVOX models are used.

## PASS meaning

PASS proves that the built-in YMM4 speaker can consume a plugin-mutated pronunciation object during standard synthesis. It does not prove perceptual naturalness.
