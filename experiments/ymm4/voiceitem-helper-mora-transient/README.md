# VoiceItem transient helper mora — YMM4 4.56.1.0

## Question

Can Voice Quality Assist add helper mora **only to the transient VOICEVOX analysis text**, mutate that helper mora to zero-duration form, synthesize back into the real VoiceItem, and leave persisted Serif/Hatsuon unchanged?

Two cases are validated on real Timeline VoiceItems:

1. vowel helper
   - persisted Serif: `えええ`
   - persisted Hatsuon: `エエエ`
   - transient analysis: `エウエウエ`
   - helper `ウ` mora vowel_length -> `0`

2. consonant helper
   - persisted Serif: `ええ`
   - persisted Hatsuon: `エエ`
   - transient analysis: `エセエ`
   - helper `セ` consonant_length -> `0`
   - helper vowel `e` remains non-zero

## Why

A2 should not permanently insert helper kana into Serif or Hatsuon merely to coax VOICEVOX. Durable helper intent can live in Assist Effect settings; augmented reading and Pronounce are derived runtime state.

## PASS boundary

PASS requires:

- both cases use real Timeline VoiceItems and real `VoiceItem.FilePath`;
- public built-in VOICEVOX speaker returns editable Pronounce/AudioQuery;
- helper mora are uniquely observable in the transient analysis;
- requested duration component reaches exactly `0`;
- the non-target duration component remains unchanged where required;
- final public synthesis receives the mutated AudioQuery without another analysis;
- VoiceItem Serif/Hatsuon remain byte-for-byte unchanged;
- regenerated Pronounce is attached and corrected WAV exists.

This experiment proves the transient helper-mora synthesis mechanism. It does **not** yet freeze durable helper-anchor storage or re-resolution after text edits.
