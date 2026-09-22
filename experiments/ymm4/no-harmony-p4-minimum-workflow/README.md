# P4.3 — minimum integrated folder workflow

Status: implementation candidate; primary-host V1 pending.

This composes:

- frozen P4.1 layer-label UI surface;
- frozen P4.2 host-independent UX commands;
- frozen P0 FoldDisplay/FoldMap;
- P3 FolderDocument serialization.

Automated smoke:

1. fixture selects contiguous L2-L4 through the public LayerSelection state;
2. real OS right-click opens the mapped current YMM4 layer ContextMenu;
3. OS click on injected Create action creates Folder 1 for exactly L2-L4;
4. OS click on the visible folder overlay collapses and expands;
5. real OS right-click + OS menu click renames to Renamed;
6. real OS right-click + OS menu click ungroups;
7. Timeline items and logical layers remain unchanged;
8. FolderDocument remains serializable throughout.

The fixture-selected range and fixed test names are automation accommodations, not hands-on UX acceptance. P4 hands-on must still validate selection ergonomics, naming prompt, discoverability and wording.
