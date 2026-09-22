# P1.3a — StructuralDeltaDetector

Host-independent detector that converts before/after logical layer observations into the frozen P1.2 structural edit values.

## Why this exists

P1.1 proved the standard YMM4 commands.

P1.2 froze the folder-range transform.

P1.3 must avoid turning each YMM4 command into a permanent product adapter. The intended product boundary is:

```text
YMM4 state/event snapshot
        │
        ▼
StructuralDeltaDetector
        │
        ├─ InsertLayers
        ├─ DeleteLayers
        └─ SwapAdjacentLayers
        │
        ▼
FolderRangeTracker
```

The detector therefore contains no YMM4 or WPF types.

## Conservative ambiguity rule

The public MIT-licensed `bluemistel/YMM4-LayerPatan` ShiftDetector is the main behavioral reference for insert/delete inference.

This detector intentionally differs in one important way:

**when sparse observations leave the exact insert/delete position underdetermined, it reports `Ambiguous` instead of choosing a guessed boundary.**

Optional inserted/vanished hints can make a sparse case exact.

This makes false folder-range edits less likely.

## Swap extension

The LayerPatan ShiftDetector deliberately rejects reorder because its folder intervals stay positional.

The current Full roadmap still benefits from classifying an exact adjacent swap so that the host observer can distinguish:

- real structural reorder;
- individual item drag;
- insert/delete.

An exact reciprocal mapping `L -> L+1` and `L+1 -> L` becomes `SwapAdjacentLayers(L)`. Applying it to P1.2 leaves folder numeric ranges unchanged.

## P1.3a gate

The pure Linux suite covers:

- dense insert;
- sparse insert ambiguity;
- inserted-hint disambiguation;
- multi-row insert;
- dense delete;
- sparse delete ambiguity;
- vanished-layer disambiguation;
- multi-row delete + merge;
- adjacent swap;
- multiple items per swapped row;
- inconsistent same-row movement;
- arbitrary individual item move;
- identity/no-change;
- direct application of detected edits to the frozen FolderRangeTracker.

P1.3b follows only after this pure detector is green. P1.3b will prove that exact YMM4 4.55.1.1 / 4.56.1.0 snapshots can feed this detector without command-specific folder logic.
