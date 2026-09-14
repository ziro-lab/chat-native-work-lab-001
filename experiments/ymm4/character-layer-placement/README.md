# YMM4 Character Front / Back Layer Placement — Experiment 004

## Goal

Prove the v0.4 Character Palette layer rule on a real YMM4 Timeline:

```text
Front = max Layer among overlapping same-Character items, then search upward
Back  = min Layer among overlapping same-Character items, then search downward
```

Candidate-layer collisions are checked against **all** Timeline items for the entire requested Quick Drop interval. Other Characters do not change the same-Character front/back baseline, but they can block a candidate layer.

## Synthetic case

At requested interval `Frame=130, Length=30`:

- Character A Voice: Layer 10
- Character A Face: Layer 18
- Character A Face: Layer 22
- other Character: Layer 40 (must not change Character A baseline)
- blocker at Layer 23 overlaps only the later part of the requested interval
- blocker at Layer 9 overlaps only the later part of the requested interval

Expected:

```text
Front baseline = 22 -> 23 blocked -> place at 24
Back baseline  = 10 -> 9 blocked  -> place at 8
```

## PASS

`PASS_CHARACTER_LAYER_PLACEMENT` requires both generated Face items to exist in the real Timeline at Layers 24 and 8 with the requested full interval unchanged.

## NOT PROVEN

- pixel/render z-order correctness
- TachieItem-specific Character extraction
- Palette UI
- other YMM4 versions
