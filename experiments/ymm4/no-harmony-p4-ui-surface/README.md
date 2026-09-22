# P4.1 — Timeline layer-label UI surface

Status: implementation candidate; V1 primary-host result pending.

Narrow question:

Can the no-Harmony folded display expose normal folder UX directly in YMM4's layer-label column using only WPF/public host surfaces?

The probe uses the already-green DirectDisplay/FoldMap and adds only:

- one small owner-row Adorner button;
- one handled-events-too ContextMenuOpening observer on the real LayerLabels control.

It requires:

- native mouse click on the overlay toggles collapse/expand;
- a click outside the small overlay still performs native layer-label selection;
- native right-click opens the existing menu and receives one tagged test item without replacing host items;
- native click on the injected item reaches its handler;
- tagged menu items and Adorner detach cleanly;
- no Harmony.

External reference, not evidence:

- `bluemistel/YMM4-LayerPatan` commit `aa58e7e772deb829853742afc65e42036cf09d47` (MIT);
- its TimelineOverlay / LayerMenu show the same general WPF surfaces, while its original fold implementation uses Harmony.

This Lab independently validates the required surfaces on the pinned host.
