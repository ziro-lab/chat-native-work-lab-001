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


## Native result — modified AudioQuery reaches synthesis unchanged

Final run `35862420856`, job `107185599961`, source `69023e9197d4b91557d115f7c8c81167aa9bfcf2` completed GREEN on real YMM4 Lite 4.56.1.0.

Observed:

- supplied VOICEVOX AudioQuery / Pronounce / Parameter all reported `HasErrors=false`;
- public `IVoiceSpeaker.CreateVoiceAsync(...)` returned the supplied VOICEVOX pronounce but did **not** create a WAV and made no HTTP request in this isolated invocation context;
- direct invocation of YMM4's internal `VOICEVOXEngine.CreateVoiceFileAsync(audioQuery, filePath, param)` completed;
- the fake backend received exactly the synthesis path, without an `/audio_query` request;
- captured `/synthesis` JSON preserved `pause_mora.vowel_length = 0.0`;
- the returned fake WAV was written as `engine-direct.wav` (4844 bytes).

Captured synthesis fragment:

```json
"pause_mora": {
  "text": "、",
  "vowel": "pau",
  "vowel_length": 0.0,
  "pitch": 0.0
}
```

Artifact `10751315999`, SHA256 `d76416efc6cc799f1f42a2d3ef2415b7e8a7b095861fd4dac85fa5ea5d3857f5`.

### Proven boundary

This proves the built-in YMM4 VOICEVOX engine synthesis layer accepts an already-modified AudioQuery and serializes that modified query directly to `/synthesis` without re-running `/audio_query`.

It does **not** yet establish the correct public/product route for asking YMM4 to perform this synthesis. The public speaker wrapper no-op observed here must be understood separately before choosing the production integration boundary.
