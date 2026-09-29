# VoiceItem relative mora pitch — YMM4 4.56.1.0

## Question

Can Voice Quality Assist take the fresh built-in VOICEVOX Pronounce for a real VoiceItem, apply a **small relative pitch curve to public Mora.Pitch**, synthesize through public `IVoiceSpeaker.CreateVoiceAsync`, and keep persisted Serif/Hatsuon unchanged?

Two deterministic gestures are checked:

- light rise: baseline pitch + `[-0.08, -0.04, 0, +0.04, +0.08]`
- light fall: baseline pitch + `[+0.08, +0.04, 0, -0.04, -0.08]`

The baseline fixture is `[5.00, 5.08, 5.16, 5.08, 5.00]`.

## PASS boundary

PASS requires:

- real Timeline VoiceItems and real VoiceItem.FilePath;
- public built-in VOICEVOX speaker / Pronounce route;
- Mora.Pitch publicly writable;
- all vowel/consonant duration data left untouched by the pitch mutation;
- final public synthesis receives the exact relative-pitch values;
- regenerated Pronounce retains those values;
- Serif/Hatsuon remain unchanged;
- rise and fall syntheses produce distinct corrected WAV fixtures.

This experiment proves only the host mechanism for **relative per-mora pitch gestures**. It does not define product UX, effect persistence, or final gesture strengths.
