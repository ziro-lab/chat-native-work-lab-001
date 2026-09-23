# VoiceItem audio / preview refresh surface — YMM4 4.56.1.0

## Goal

Close the remaining supported host-refresh boundary after a pronunciation correction overwrites the WAV owned by a real `VoiceItem`.

The experiment validates what product code can guarantee without assuming an undocumented preview-redraw API:

1. corrected synthesis replaces the real `VoiceItem.FilePath` with distinct WAV bytes;
2. public `VoiceItem.ClearVoiceCache()` clears the item cache state without changing the corrected WAV;
3. assigning regenerated `Pronounce` is observable on the real item;
4. public `Timeline.CurrentFrame` changes emit the normal `PropertyChanged("CurrentFrame")` refresh signal on YMM4 4.56.1.0;
5. the Plugin-facing Timeline surface is inventoried for any dedicated preview refresh/redraw method.

## Boundary

This is **host state/cache evidence**, not a perceptual audio-device test.

A GREEN run does not claim that GitHub-hosted Windows speakers physically played the corrected waveform. Audible/interactive playback remains a hands-on acceptance check if needed.

The product must not invent or call unrelated internal redraw methods when the public host surface does not provide one.
