# YMM4 Template Placer UX host-surface preflight

Purpose: remove host-API guesswork before the next Template Placer UX implementation pass.

Pinned host: exact YMM4 4.55.1.1 Lite / .NET 10 / Windows.

The probe is reflection-only and writes no Timeline/project/settings state. It records:

- the exact public surface of `IToolPlugin`, including any category/group/order metadata relevant to placing a plugin under `ツール > ユーティリティ`;
- native Undo/Redo/history/transaction types and methods, to determine whether high-frequency expression switching can be coalesced without a custom undo stack;
- public Timeline current-frame and selection APIs relevant to Voice-row double-click navigation;
- candidate host APIs for user-facing/localized Item type names, so internal CLR names such as `TransitionItem` and `FrameBufferItem` do not leak into normal Settings.

This experiment does not implement product behavior. It exists only to gate implementation choices on proved host capabilities.