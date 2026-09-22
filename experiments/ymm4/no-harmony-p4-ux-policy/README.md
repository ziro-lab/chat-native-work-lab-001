# P4.2 — host-independent folder UX commands

Status: candidate; pure CI pending.

This layer keeps product semantics out of WPF controls.

Initial creation candidate:

- minimum two selected layers;
- selected layers must be contiguous;
- exact Start/End comes from that selection;
- existing FolderDocument range validation decides nested/disjoint validity;
- crossing or duplicate-head creation is rejected;
- no automatic empty-layer insertion for a one-row selection.

Commands are immutable FolderDocument transforms:

- CreateFolder;
- RenameFolder;
- ToggleCollapsed;
- Ungroup.

Ungroup removes folder metadata only. It does not represent deletion of YMM4 layers/items.

This policy is intentionally easy to revise after P4 hands-on; it is not a claim that the first creation UX is final.
