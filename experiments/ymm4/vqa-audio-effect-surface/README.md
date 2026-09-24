# VQA vNext Audio Effect Surface Gate

Status: **PREP / NOT YET PROVEN**

## Goal

Determine whether Voice Quality Assist can move its durable per-VoiceItem settings from the subtitle/video-effect collection to the normal **Audio Effects** area without relying on brittle private reflection or Harmony.

Preferred product shape:

`VoiceItem -> Audio Effects -> Pronunciation Assist`

## Environment

- YMM4 Lite 4.56.1.0
- .NET 10
- pinned YMM4 ZIP SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

## Probe requirements

Create the smallest possible custom audio effect using the normal YMM4 plugin effect surface.

The effect should contain harmless test settings representative of the product:

- enabled/disabled state;
- string setting;
- enum setting.

Its processor must be pass-through.

## PASS assertions

PASS requires all of the following.

1. The custom effect is discovered by the real host as an Audio Effect.
2. It can be attached to a VoiceItem through a product-acceptable host route.
3. It appears under the VoiceItem Audio Effects UI, not the subtitle/video-effects UI.
4. YMM4 renders normal property editors for the custom settings.
5. `IsEnabled` changes are observable through normal notification.
6. Custom property changes are observable through normal notification.
7. The effect and settings survive real project save/reload.
8. The VoiceItem audio-effect collection can be enumerated with a public/product-acceptable surface.
9. Add/remove can be implemented without private field traversal.
10. Existing YMM4 UndoRedoManager can record add/remove or a product journal can safely restore collection membership.
11. Pass-through output is byte/sample equivalent for the controlled fixture, or the host proves a no-op processor path that does not alter the audio.
12. Removing the effect leaves ordinary VoiceItem audio behavior intact.

## Migration observation

Also record whether a product can safely:

- read the existing legacy Pronunciation Assist stored under `JimakuVideoEffects`;
- create/configure the new audio Effect first;
- attach it;
- then remove the legacy Effect.

The experiment does not need to implement product migration, only prove the collection/property surfaces needed for it.

## Decision

### GREEN

If every required surface is available without brittle private reflection/Harmony:

- product vNext should introduce an Audio Effect storage adapter;
- dual-read legacy subtitle Effect + new audio Effect;
- new-write audio Effect;
- migrate candidate state safely.

### BLOCKED

If a required surface is missing:

- keep Pronunciation Assist in the existing subtitle/video-effect collection for this release;
- continue the forced-boundary and settings-UI work anyway;
- do not use Harmony merely to move the UI section.

## PASS means

A PASS proves that Audio Effects is a viable durable settings host for this plugin on the pinned YMM4 version.

It does not prove future-version compatibility or final UX quality.
