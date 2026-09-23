# YMM4 no-Harmony Full — P6 Hardening & Performance Plan

Status: **ACTIVE / P6.1 NEXT**

P5 compatibility is frozen at:

- `docs/YMM4_NO_HARMONY_P5_COMPATIBILITY_FREEZE.md`
- P5 freeze branch HEAD `43886abe2699adb56495498495b3335d7a0aa882`

P6 does not add product features. It attempts to break the frozen P4/P5 architecture under larger, repeated, long-lived, and lifecycle-sensitive workloads.

## 1. P6 rules

- No Harmony.
- Do not introduce a stress-specific product model, state store, Undo stack, FoldMap, or resident worker.
- Stress counters are diagnostics, not benchmark claims.
- Do not fail a gate merely because one run is slower than another unless a concrete timeout/runaway condition is defined.
- Prefer bounded representative stress over enormous assertion counts.
- Use one pinned host during inner-loop diagnosis when the host surface is known to be identical; use both pinned hosts for a freeze gate.
- Fixture creation may use direct public host setup routes when the behavior under test is not the user-visible creation command itself.
- A hardening failure must be classified as:
  - product correctness;
  - lifecycle/cleanup;
  - performance/runaway refresh;
  - host/runner/resource;
  before any architecture change.

## 2. P6 gates

### P6.1 — bounded density / nesting / fold stress

Target:

- ~128 explicit layers;
- ~384 resource-free built-in items;
- ~24 folders;
- nested depth >= 6;
- repeated collapse/expand cycles;
- item-area visual summary active;
- repeated scroll/zoom/resize;
- no display failure/reentry;
- stable FolderProductState invariants;
- no runaway application/refresh budget;
- subscription count does not grow across steady-state cycles.

This is a hardening gate, not a throughput benchmark.

### P6.2 — repeated structural/history cycles

Representative repeated operations:

- standard Add/Delete/Move;
- plugin-owned add-inside/end;
- explicit folder block move;
- visibility hide/show;
- GroupRange correction;
- Undo/Redo.

Acceptance focuses on exact final state, bounded history ownership, and no drift after repeated cycles.

### P6.3 — viewport / visual soak

Repeated:

- horizontal and vertical scroll;
- TimelineZoom changes;
- window resize;
- collapsed timing summary;
- Group compatibility overlay.

Acceptance:

- geometry remains aligned;
- no stale hidden views;
- no continuously increasing refresh/subscription counts after settling.

### P6.4 — project/session lifecycle

Representative:

- unsaved project -> scene/timeline change;
- saved project sync;
- controller detach/reconnect;
- active Timeline replacement;
- pending Dispatcher work during detach;
- old-session callbacks cannot mutate the new session.

### P6.5 — failure / recovery boundaries

Inject bounded failures around:

- unsupported/missing media;
- invalid persisted raw state;
- overlay attachment unavailable;
- ambiguous structural evidence;
- plugin-owned operation precondition rejection.

Acceptance:

- fail/skip safely;
- no silent folder corruption;
- no unrelated Undo rollback;
- recovery/raw preservation remains intact.

### P6.6 — final dual-host soak / freeze

Run the representative P6 profile on both pinned hosts and record:

- source commit;
- exact host versions;
- final state;
- diagnostic counters;
- artifact IDs/digests;
- real .ymme install/startup regression.

## 3. Initial bounded budgets

These are runaway guards, not performance promises.

P6.1 starts below existing implementation limits:

- logical layer count <= 256;
- item count <= 768;
- folder count <= 64;
- nesting depth <= 12;
- repeated fold cycles <= 40;
- viewport/zoom cycles <= 40.

If a lower workload already exposes a correctness or refresh problem, fix/classify that problem before increasing load.

## 4. P6.1 required observations

Record at least:

- `DirectDisplay.Applications`;
- `Mutations`;
- `CanvasRefreshes`;
- `Reentries`;
- `Failure`;
- `SubscriptionCount`;
- visual timing-band count when collapsed;
- final visible-row count;
- FolderDocument validation;
- exact item/folder counts;
- cycle count;
- no Harmony loaded.

Subscription stability is measured after setup and after repeated steady-state cycles. One-time growth caused by newly created host objects is distinguished from per-cycle leakage.

## 5. P6 exit

P6 is complete when the frozen architecture survives the defined density, repetition, viewport, lifecycle and failure scenarios without:

- folder-state drift;
- history corruption;
- stale folded geometry;
- runaway refresh/reentry;
- leaked subscriptions tied to disposed controllers;
- old-session writes into a new project;
- unsafe failure recovery.

The next gate after P6 is P7 Release.
