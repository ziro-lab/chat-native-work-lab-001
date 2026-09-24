# Voice speaker reading-prefix mapping — YMM4 4.56.1.0

## Question

Can A1 map an official Serif `<w0>` boundary into the actual reading domain by asking the same YMM4 voice speaker for the reading, instead of shipping a separate Japanese morphological analyzer?

Candidate public API:

```csharp
IVoiceSpeaker.ConvertKanjiToYomiAsync(
    string text,
    IVoiceParameter voiceParameter)
```

## Native experiment

Use the real built-in YMM4 VOICEVOX speaker connected to a deterministic fake VOICEVOX backend.

Ask the same speaker for:

- full text: `東京大学`
- prefix text: `東京`
- already-kana text: `トウキョウダイガク`

Then generate a real VOICEVOX Pronounce for the full reading and record its AccentPhrase / Mora structure.

The fake backend deliberately returns deterministic text-dependent mora structures so the experiment can answer:

1. what host/API route `ConvertKanjiToYomiAsync` actually uses;
2. what string shape it returns;
3. whether the prefix reading can be matched to a cumulative mora boundary without using Serif character index as mora index.

## PASS boundary

PASS requires the public method to complete for full/prefix text and produce a non-empty observable reading. Request traces and the returned strings are evidence.

A successful synthetic prefix match proves the host mechanism, **not Japanese linguistic correctness for every real sentence**. Product code must still fail closed when prefix/full/current-Hatsuon mapping is not exact.

## Intended safe product rule

Only auto-apply a zero-pause boundary when all of the following resolve exactly:

```text
Serif marker prefix
  -> same speaker ConvertKanjiToYomiAsync(prefix)
  -> normalized reading prefix
  -> exact prefix of current Hatsuon / full speaker reading
  -> exact cumulative Mora.Text boundary
  -> target AccentPhrase has PauseMora
```

Otherwise return unresolved/ambiguous and do not mutate audio.
