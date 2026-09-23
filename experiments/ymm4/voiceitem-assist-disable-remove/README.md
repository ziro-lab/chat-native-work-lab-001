# VoiceItem Assist disable/remove lifecycle — YMM4 4.56.1.0

## Question

After a real VoiceItem has been pronunciation-corrected, can disabling or removing the Assist Effect deterministically restore the uncorrected baseline Pronounce + WAV through the same public synthesis route?

## Intended product semantics

The Assist Effect is an opt-in/settings container.

- Effect present + enabled -> correction active.
- Effect disabled -> correction inactive; rebuild baseline audio.
- Effect removed -> correction inactive; rebuild baseline audio.
- Re-enable -> re-resolve and reapply correction.

## Native gate

One real Timeline VoiceItem is driven through:

```text
enabled
  -> corrected pause 0.0 + corrected WAV hash

disable Effect
  -> baseline pause > 0 + baseline WAV hash

re-enable Effect
  -> corrected pause 0.0 + original corrected WAV hash

remove Effect
  -> baseline pause > 0 + original baseline WAV hash
```

The test also requires the already-proven host notifications:

- Effect `IsEnabled` change notification;
- VoiceItem `JimakuVideoEffects` change notification on removal.

## Boundary

This proves state transition mechanics. Product debounce/coalescing and batch scheduling remain controller implementation details.
