# VoiceItem pronunciation observer — YMM4 4.56.1.0

## Goal

Validate the runtime half of the pronunciation-assist architecture:

> A VoiceItem carrying the pronunciation-assist subtitle effect can be observed continuously without requiring the effect settings UI to be open.

This slice checks whether the real YMM4 VoiceItem exposes ordinary property-change notifications for the fields a pronunciation controller cares about.

## Questions

1. Does a real VoiceItem implement INotifyPropertyChanged?
2. Do Serif changes raise PropertyChanged("Serif")?
3. Do Hatsuon changes raise PropertyChanged("Hatsuon")?
4. Does replacing JimakuVideoEffects raise PropertyChanged("JimakuVideoEffects")?
5. Does the custom key effect itself expose PropertyChanged for IsEnabled?
6. Can all of this happen while the VoiceItem is present in the real Timeline?

## PASS boundary

PASS proves that a separate pronunciation controller can watch VoiceItem/effect state directly and does not need the nested effect editor UI to remain materialized.

It does not yet prove VOICEVOX regeneration, control-marker parsing, save/reload, or Undo/Redo behavior.
