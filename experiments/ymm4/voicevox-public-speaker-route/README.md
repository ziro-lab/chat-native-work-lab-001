# VOICEVOX public speaker route IL inventory — YMM4 4.56.1.0

## Goal

Inspect the real YMM4 implementation boundary behind
`VOICEVOXVoiceSpeaker.CreateVoiceAsync(...)`.

The previous fake-backend probe showed:

- direct internal engine synthesis writes the WAV and preserves modified AudioQuery;
- isolated public `IVoiceSpeaker.CreateVoiceAsync` returns Pronounce but does not synthesize.

This experiment inventories the public method's async state machine and resolved method/field/string references so the branch condition can be understood without guessing.

## PASS boundary

PASS only means the implementation surface was successfully inventoried from the pinned real host.
It does not claim a supported production route yet.
