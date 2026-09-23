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

Final run `35862648969`, job `107186361371`, source `1bfa9e7f6e55365da393d8bba6d93a77e521d29d` completed GREEN on real YMM4 Lite 4.56.1.0.

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

Artifact `10750448495`, SHA256 `563d021b230a881d0cbd7a1ec83b4e9a8dc4d19d6bcb9df2d77569dd87bffe91`.

### Proven boundary

This proves the built-in YMM4 VOICEVOX engine synthesis layer accepts an already-modified AudioQuery and serializes that modified query directly to `/synthesis` without re-running `/audio_query`.

It does **not** yet establish the correct public/product route for asking YMM4 to perform this synthesis. The public speaker wrapper no-op observed here must be understood separately before choosing the production integration boundary.
