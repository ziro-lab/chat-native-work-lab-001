# VoiceItem correction Undo/Redo — YMM4 4.56.1.0

## Question

Can a pronunciation correction applied to a real YMM4 `VoiceItem` participate in the host's user-visible Undo/Redo history as one plugin operation, while restoring both:

- the `VoiceItem.Pronounce` state; and
- the generated audio file owned by that VoiceItem?

## Candidate

Prepare two complete snapshots before committing the edit:

- baseline: original VOICEVOX Pronounce + baseline WAV bytes;
- corrected: patched Pronounce + corrected WAV bytes.

Apply the corrected snapshot, enqueue one public `UndoRedoActionCommand`, then commit one `UndoRedoManager.Record()`.

The command callbacks restore the complete snapshot synchronously:

```text
Undo -> baseline Pronounce + baseline WAV + ClearVoiceCache()
Redo -> corrected Pronounce + corrected WAV + ClearVoiceCache()
```

The native test then drives normal Ctrl+Z / Ctrl+Y and checks both pronunciation state and file SHA256.

## Evidence boundary

This experiment is allowed to use bounded reflection to obtain the host `UndoRedoManager` for the native probe. It separately inventories whether a public acquisition route is visible.

A GREEN behavior result does **not** by itself approve a private manager-acquisition route for product code.

## Related evidence

- PR #116: mutable VOICEVOX pronunciation graph.
- PR #125: corrected Pronounce synthesizes through public `IVoiceSpeaker.CreateVoiceAsync`.
- PR #128: corrected Pronounce can regenerate the audio file of a real Timeline VoiceItem.

## Not proven here

- save/reload persistence;
- public product-grade acquisition of `UndoRedoManager` if none is found;
- batch/multi-item transaction semantics;
- interactive preview refresh.
