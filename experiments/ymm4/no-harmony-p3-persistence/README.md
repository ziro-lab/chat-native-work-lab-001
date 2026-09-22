# P3.1 — host-independent folder persistence model

Status: **implementation candidate; V0 run pending**.

P2 Host Interaction Coverage is frozen at PR #84. P3 starts with the persistence model only; no YMM4 lifecycle claim is made by this slice.

## Goal

Define the smallest project-state document that can survive save/reload without persisting display geometry or depending on YMM4 host types.

Persisted data:

- schema version;
- opaque per-Timeline/Scene key supplied later by the host adapter;
- folder identity;
- logical Start / End;
- name;
- collapsed state.

Not persisted:

- visual row;
- item/view geometry;
- viewport;
- reflected/private YMM4 state;
- derived parent/child pointers.

Hierarchy remains derived from the already-frozen logical ranges.

## Safety behavior

The codec distinguishes:

- empty state;
- malformed JSON;
- unsupported schema version;
- structurally invalid state;
- valid loaded state.

Malformed/unsupported/invalid data does not silently become an empty document. That lets the product degrade safely without overwriting recoverable data on the next save.

Serialization is canonicalized by Timeline key and folder range/identity so equivalent state produces deterministic JSON.

## Host-independent boundary

The persistence core targets plain `net10.0` and links only the frozen P1 `FolderRangeTracker` validation rules.

It contains no:

- YMM4 type;
- WPF type;
- TimelineViewModel;
- reflection;
- filesystem path policy.

The host-facing identity and storage mechanism are intentionally deferred to P3.2.

## Persistence reference material

Two public implementations make `IToolViewModel.SaveState/LoadState + ToolState.SavedState` the leading host candidate:

- official `manju-summoner/YukkuriMovieMaker.Plugin.Community` Notepad at commit `ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa`;
- MIT `routersys/YMM4-Timeline` at commit `755a664bee9313a6efad2a592771a6ebfffd6974`.

Both serialize plugin-owned state into `ToolState.SavedState`. YMM4-Timeline additionally uses the tool-state route to restore its panel identity across project save/reopen.

These are **reference implementations, not Lab evidence** for the no-Harmony folder product.

## P3.2 host questions

Before freezing the storage mechanism, exact-host observation must answer:

1. Which public Scene/Timeline identity is stable across save/reopen?
2. Is `ToolState.SavedState` restored with the project on both pinned hosts for this tool shape?
3. Does switching projects/scenes keep documents isolated correctly?
4. What happens when the plugin is unavailable and the project is opened/saved?
5. Can malformed/stale plugin state fail without project corruption?

Do not add an external sidecar or plugin-directory data file unless the project-owned ToolState route fails one of these requirements.
