# No-Harmony FolderLayout generalization

Goal: replace the earlier hard-coded "[1..2] collapsed" mapping with one reusable display-row ↔ logical-layer model that supports multiple disjoint collapsed folders and nested collapsed folders.

Scenarios exercised in real YMM4:

- A: collapsed [2..3] and [6..8] -> visible logical rows 0,1,2,4,5,6,9,10.
- B: additionally collapse parent [1..5] while [2..3] remains nested -> visible rows 0,1,6,9,10.

The probe validates algorithmic owner-row mapping, live WPF item geometry, custom Shift-marquee selection, post-corrected native YMM4 drag with non-uniform logical jumps, Undo/Redo, right-click/add-position correction, and dynamic parent-collapse re-layout.

No Harmony reference/package/runtime patch is used.
