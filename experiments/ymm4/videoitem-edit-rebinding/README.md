# VideoItem edit rebinding — discovery

Goal: verify the YMM4 host behaviors that matter when Highlight Navigator reviews a long recording while the user edits it:

- trim;
- move after split;
- Undo / Redo;
- duplicate occurrences of the same source range.

This experiment is intentionally separate from `videoitem-split-lifecycle`. The first native pass discovers the exact 4.56.1.0 host surfaces. Behavioral claims are added only after real-host mutation assertions pass.
