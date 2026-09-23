# VOICEVOX Pronounce mutation surface — YMM4 4.56.1.0

## Goal

Answer one host question before building a pronunciation-boundary plugin:

> Can a normal YMM4 plugin reach the built-in VOICEVOX pronunciation model far enough to change a pause mora duration to zero?

This first slice is a **native automated surface/mutation probe**. It does not yet claim audible output or regeneration behavior.

## Environment

- GitHub Actions `windows-latest`
- YMM4 Lite 4.56.1.0
- host ZIP SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- no private project or voice asset

## Required assertions

The real host process must prove:

1. `VoiceItem.Pronounce` exists and is publicly readable/writable.
2. A built-in type implementing `IVoicePronounce` for VOICEVOX is discoverable.
3. The VOICEVOX pronounce type exposes an AudioQuery member.
4. AudioQuery exposes accent phrases.
5. An accent phrase exposes `pause_mora`.
6. A mora exposes writable `vowel_length`.
7. A synthetic pause mora can be changed from a positive value to exactly zero through the discovered public member.
8. The nested VOICEVOX model can be assembled enough for the zero duration to remain observable through the full Pronounce -> AudioQuery -> AccentPhrase -> pause mora path.
9. The host's `IVoiceItemEditService` surface is inventoried for the next regeneration slice.

## PASS boundary

PASS proves only that the pinned YMM4 host exposes a mutable VOICEVOX pronunciation object graph usable from plugin code, including zero-valued pause duration.

## NOT PROVEN

This slice does not prove:

- that changing an existing live VoiceItem through a Tool plugin is exposed by public API;
- that YMM4 regenerates audio after the mutation;
- that zero pause is perceptually gapless;
- that pitch/intonation across the boundary remains natural;
- save/reload or Undo behavior;
- behavior on other YMM4 versions.

Those are the next slice if this surface probe passes.

Initial native surface probe is executed from branch `lab/voicevox-pronounce-mutation` before any product prototype is created.


## Initial native result — PASS

Validated on real YMM4 Lite 4.56.1.0.

- Source HEAD: `4dc937cfd97ffd805aaa6c80bcad49c39f541bbb`
- Workflow run: `35827921050`
- Job: `107073846730`
- Artifact: `10735179941`
- Artifact SHA256: `f45dc4a4ae092450bc5057e8c8b8ff2b6f325c45c3c25d5e388d3be3b4bf3f9c`
- Native marker: `PASS_VOICEVOX_PRONOUNCE_MUTATION_SURFACE`
- Required assertions: **10/10 PASS**

Observed public host surface:

- `VoiceItem.Pronounce`: public get/set;
- built-in `YukkuriMovieMaker.Voice.VOICEVOXVoicePronounce`: available;
- `VOICEVOXVoicePronounce.AudioQuery`: public get/set;
- `IVOICEVOXAudioQuery.AccentPhrases`: public readable collection;
- `VOICEVOXAccentPhrase.PauseMora`: public get/set;
- `VOICEVOXMora.VowelLength`: public get/set;
- a pause mora duration was changed from **0.25 to exactly 0** and read back successfully through a nested Pronounce/AudioQuery/AccentPhrase object graph;
- `IVoiceItemEditService` exposes public `Hatsuon`, `IsHatsuonChanged`, and `CreateVoiceFileAsync(bool force)`.

This proves the core data-mutation feasibility for a plugin that turns selected punctuation-derived pauses into zero-length pauses.

Still not proven by this slice:

- mutation of a real already-generated VOICEVOX VoiceItem followed by YMM4 standard regeneration;
- audible equivalence / whether the resulting phrase boundary is perceptually gapless and natural;
- persistence/reload and Undo behavior.

Those are the next product-relevant native checks.
