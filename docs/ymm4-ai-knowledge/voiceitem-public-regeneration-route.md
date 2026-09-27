# Real VoiceItem corrected synthesis can use the public speaker route

- Status: evidence-qualified
- Repository state: Draft PR #128
- Knowledge class: LAB-NATIVE
- Surface: S1 / public VoiceItem and voice-speaker APIs
- YMM4 version: 4.56.1.0 Lite
- Tested source: `8baff1eb9a2505e4c42cd9efe55e8e8c338949d0`
- Revalidation trigger: YMM4 changes VoiceItem / VoiceDescription / IVoiceSpeaker regeneration semantics

## Claim

On the tested real YMM4 4.56.1.0 Timeline VoiceItem, a corrected VOICEVOX pronunciation graph could be synthesized through public voice APIs directly into the audio file owned by the VoiceItem.

The proven route included:

```text
real Timeline VoiceItem
 -> normal VoiceItem.CreateVoiceFileAsync()
 -> public IVoiceSpeaker analysis/synthesis
 -> obtain Pronounce
 -> mutate target pronunciation data
 -> public IVoiceSpeaker.CreateVoiceAsync(... patched Pronounce ..., VoiceItem.FilePath)
 -> overwrite real VoiceItem audio
 -> VoiceItem.ClearVoiceCache()
 -> keep regenerated Pronounce attached
```

The final patched synthesis was observed without requiring the internal `VOICEVOXEngine.CreateVoiceFileAsync` route.

Public surfaces proven in the evidence include:

- `VoiceItem.CreateVoiceFileAsync()`;
- `VoiceItem.Pronounce`;
- `VoiceItem.VoiceParameter`;
- `VoiceItem.VoiceCache`;
- `VoiceItem.ClearVoiceCache()`;
- `Character.Voice`;
- `Character.VoiceParameter`;
- `VoiceDescription(IVoiceSpeaker)`;
- `VoiceDescription.SetSpeaker(IVoiceSpeaker)`.

## Safe use

Prefer the public VoiceItem / IVoiceSpeaker route for local pronunciation regeneration when its exact provider/data requirements are satisfied.

## Do not infer

- This PR does not prove Undo/Redo for a correction unit.
- It does not prove save/reload persistence.
- It does not prove interactive preview/audio refresh.
- It does not prove Assist Effect disable/remove lifecycle or batch semantics.

## Failure behavior

If the required public Pronounce/provider surface is unavailable, leave the item/audio unchanged rather than reaching immediately for the internal VOICEVOX engine.

## Evidence

- Lab PR: [#128](https://github.com/ziro-lab/chat-native-work-lab-001/pull/128)
- Host ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Workflow run: `35883723858`
- Native job: `107258405742`
- Artifact: `10762610287`
- Artifact SHA256: `e896cc7819ac347f5d7407925a79144c404fd1f32a693c1aa3d946be36a9cea8`
- Marker: `PASS_VOICEITEM_REGENERATION_E2E`
