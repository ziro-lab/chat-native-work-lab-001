# VoiceItem correction save/reload — YMM4 4.56.1.0

## Question

Which parts of a pronunciation-assist correction survive a real YMM4 project save/reopen?

The durable source of truth is expected to be declarative:

- the Assist Effect and its settings;
- official control tags such as `<w0>`;
- ordinary VoiceItem text/settings.

A synthesized `VoiceItem.Pronounce` may or may not be persisted by YMM4. This probe observes that outcome instead of assuming it.

## Native sequence

1. Create a real Timeline VoiceItem with:
   - Serif containing `<w0>`;
   - Hatsuon;
   - enabled Assist Effect + probe settings;
   - VOICEVOX Pronounce with pause = 0.
2. Save project A.
3. Mutate the in-memory item to deliberately different values.
4. Save project B, so A is no longer the current project.
5. Open project A.
6. Verify the original marker/effect/settings return.
7. Observe whether Pronounce is restored; if present it must still carry pause = 0.

This avoids a false positive from simply retaining the same in-memory object.

## Not proven here

- regeneration triggered automatically after reload;
- Undo/Redo;
- plugin-unavailable project behavior;
- batch apply.
