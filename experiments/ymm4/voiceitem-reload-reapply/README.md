# VoiceItem reload reapply — YMM4 4.56.1.0

## Question

After native project reload removes transient `VoiceItem.Pronounce`, can the persisted pronunciation-assist source be resolved again and regenerated through the already-proven public VOICEVOX route?

## Previous evidence

- PR #128: real VoiceItem generate -> patch -> regenerate works.
- PR #131: `<w0>`, Hatsuon and Assist Effect/settings survive save/reopen, but `VoiceItem.Pronounce` becomes null.

## Native gate

1. Configure a fake VOICEVOX speaker/engine through the same supported public route used by PR #128.
2. Create a real VoiceItem with persisted `<w0>` marker, enabled Assist Effect, real VoiceDescription / VoiceParameter, and generated audio.
3. Save project A.
4. Save deliberately different project B.
5. Open project A.
6. Require the reloaded VoiceItem to have the durable marker/effect source and no stale Pronounce.
7. Obtain a fresh Pronounce through public `IVoiceSpeaker.CreateVoiceAsync(..., pronounce:null,...)`.
8. Resolve the persisted correction, set target pause to 0, and synthesize directly into the reloaded VoiceItem file.
9. Clear Voice cache and attach the regenerated Pronounce.
10. Require the final synthesis body to preserve pause=0 without a new `/audio_query` after the corrected Pronounce is supplied.

## Boundary

This proves the **reload -> resolve -> regenerate** operation itself. The exact production trigger/subscription used to decide *when* to run this operation is a separate controller-lifecycle concern.
