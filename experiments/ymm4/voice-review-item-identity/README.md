# Voice Review item identity — YMM4 4.56.1.0

## Goal

Validate whether VoiceItem.Guid is suitable as the primary candidate identity for Voice Review export/import.

## Required

- VoiceItem exposes a public Guid property.
- new VoiceItems receive non-empty unique Guid values.
- editing Serif/Frame/Layer does not change Guid.
- after adding the VoiceItem to the real Timeline, the same Guid remains observable.

Clone/copy methods and JSON round-trip behavior are recorded as observations, not acceptance requirements.
