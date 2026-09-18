# YMM4 Timeline Input Intent — Round 2 / L3-L5

## Goal

Observe real YMM4 input-route ordering well enough to distinguish:

- first click on a Timeline Item
- re-click of an already-selected Item
- blank Timeline click
- ruler click
- Ctrl+Item
- ruler drag / playhead-style pointer movement
- keyboard frame movement
- playback-style Space-key movement when available

## Method

The probe creates a synthetic VoiceItem, finds the real rendered Timeline controls, subscribes to WPF `InputManager`, routed `PreviewMouseDown/MouseDown`, and public Timeline change notifications, then injects OS mouse/keyboard input through user32.

This is **real input routing in the real host**, but it is automated input rather than a human hand.

## PASS boundary

PASS requires the actual Item, blank Timeline and ruler targets to receive injected mouse input and produce an ordered event trace. Playback/keyboard/drag variants are recorded separately and may remain observational if the runner focus/key binding does not activate them.
