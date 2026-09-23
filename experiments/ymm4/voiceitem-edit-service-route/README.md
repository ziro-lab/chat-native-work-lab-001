# VoiceItem edit service route — YMM4 4.56.1.0

## Goal

Inventory the real implementation behind public `IVoiceItemEditService.CreateVoiceFileAsync()`.

Questions:

1. Which real host type implements `IVoiceItemEditService`?
2. Is that implementation constructible/obtainable independently of an item property editor?
3. What VoiceItem/cache/speaker surfaces does `CreateVoiceFileAsync` call?

This is an inventory slice only. No production dependency on internal types is implied.
