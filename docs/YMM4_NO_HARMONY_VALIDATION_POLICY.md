# YMM4 no-Harmony Full — Validation Policy v0.1

Status: **proposed policy for P2-P7**.

P0 Core Spine / P1 Structural Tracking are already frozen green.
From P2 onward, keep their proof assets but stop using research-grade validation as the default development loop.

Core rule:

> Use the smallest validation level that can falsify the assumption changed by the work.

Development checks, canonical YMM4 evidence, and release regression are different jobs.

---

## 1. Validation levels

| Level | Purpose | Default scope |
| --- | --- | --- |
| **V0 Pure/Fast** | known host-independent logic | unit/invariant/model/build/serialization tests; no YMM4 |
| **V1 Native Smoke** | ordinary host-sensitive development | one primary pinned host, narrow representative scenario |
| **V2 Phase Evidence** | freeze a new host-dependent product contract | focused integrated matrix, both pinned hosts when part of support claim, full Evidence identity |
| **V3 Golden Regression** | protect frozen cross-cutting behavior / RC | affected frozen P0/P1 or later product golden suites |

### V0

Use whenever possible.

Typical targets:

- `FolderRangeTracker`;
- `StructuralDeltaDetector`;
- `FoldMap`;
- persistence schema/migration;
- pure hidden-destination or compatibility policy;
- ordinary compile/build checks.

V0 does not establish undocumented YMM4 behavior.

### V1

Use when real YMM4 behavior matters.

Default development host: **YMM4 4.56.1.0 Lite**.

Examples:

- one placement/navigation route;
- one Undo transaction;
- one gesture;
- one save/reopen path;
- one `YmmHostAccess` capability.

V1 answers “does the changed integration still work?”.
It does not automatically become a permanent Lab host claim.

### V2

Use at phase freeze, or when a new undocumented YMM4 behavior becomes a product dependency.

For canonical host evidence record:

```text
source / tested tree
 -> workflow run
 -> exact host/version
 -> assertions
 -> artifact
 -> digest
```

Keep PASS and NOT PROVEN separate.

### V3

Run only when already-frozen shared behavior can have changed, or at release-candidate boundaries.

Typical triggers:

- `FoldMap` semantics;
- folded display/virtualization;
- shared `YmmHostAccess`;
- Undo ownership;
- gesture lifecycle;
- shared placement/navigation routing;
- major runtime/dependency change;
- release candidate.

Do not run V3 for an isolated label, styling change, or leaf route that does not alter a shared contract.

---

## 2. Manual hands-on is a separate track

Use manual live observation for:

- discoverability;
- interaction comfort;
- animation/visual continuity;
- keyboard ergonomics;
- recovery/error wording;
- perceptual behavior that automation would test poorly.

Record exact host, task, expected result, observed result and uncertainty.

Do not convert manual observations into automated PASS claims.
Avoid pixel-perfect automation unless exact pixels are the actual requirement.

---

## 3. Change -> validation routing

| Change | Default |
| --- | --- |
| Pure model/policy/schema/migration | V0 |
| Docs/comments/wording | V0 or no runtime test |
| New route using an existing proven mechanism | V0 + focused V1 |
| New undocumented YMM4 behavior | isolated V1 probe -> V2 only if adopted |
| `YmmHostAccess` shared contract | V0 + V1; V2/V3 before freeze |
| `FoldMap` / folded geometry / virtualization | V0 + V1 + affected V3 |
| Undo ownership | V0 + V1 + affected V3 |
| Gesture/drag lifecycle | V0 + V1 + affected V3 |
| Leaf UI styling | manual; no full native regression |
| UX path that changes host interaction | manual + focused V1; V2 at phase exit |
| YMM4 version bump | affected subsystem only first |
| Package/runtime/dependency change | build/package smoke + V1; V3 for RC |
| Release candidate | V0 + required V2 + affected V3 + package/recovery checks |

If ownership of the changed assumption is unclear, identify it before adding more tests.

---

## 4. Host and artifact policy

### Host matrix

Default:

- normal development: newest pinned supported host only;
- phase freeze: both pinned hosts for host-dependent acceptance;
- RC: both pinned hosts;
- version-sensitive discrepancy: both hosts immediately;
- pure logic: no YMM4.

Current pinned hosts:

- YMM4 4.55.1.1 Lite;
- YMM4 4.56.1.0 Lite.

A future YMM4 update does not automatically require replaying every historical probe.
Revalidate the subsystem materially touched by the host change.

### Artifacts

Ordinary V0/V1 checks do not need to become permanent canonical Evidence.

Keep full artifact/digest records for:

- phase freeze;
- architecture freeze;
- difficult/expensive host proof;
- release candidate.

---

## 5. Frozen P0/P1 treatment

P0/P1 are **golden assets**, not the normal inner loop.

Keep:

- P0 Track C/native interaction evidence;
- P1 pure tracker/detector suites;
- P1 final 353/353 integrated structural acceptance;
- existing Evidence identities.

From P2 onward:

- run cheap pure tests whenever relevant;
- do not rerun full native P0/P1 for unrelated work;
- rerun only the affected frozen suite when a shared invariant changes;
- run relevant golden suites again at RC.

New evidence should reopen only the affected assumption, not broadly restart P0/P1.

---

## 6. Lean phase gates

### P2 — Host Interaction Coverage

Classify each remaining route as:

1. native-safe;
2. mapped through existing `FoldMap` / interaction routing;
3. intentionally unsupported with graceful behavior.

Mechanism groups:

- placement/add;
- automatic navigation / ScrollToItem-like behavior;
- host-driven selection/seek;
- keyboard layer navigation;
- hidden destination;
- group/multi-layer movement;
- context-menu layer resolution.

Use one or two representative cases per mechanism, plus a nested/hidden boundary only when it can change the result.

**Exit:** focused matrix on both pinned hosts. Do not replay all P1 assertions unless P2 changes a P1 mechanism.

### P3 — Persistence

Keep most coverage at V0:

- schema round-trip;
- hierarchy/range invariants;
- collapse/name metadata;
- malformed/stale state;
- migrations/version skew;
- deterministic recovery.

Use native YMM4 only for:

- save -> close -> reopen;
- project switch/new project;
- recoverable unavailable/stale state where practical;
- one structural edit after reload.

**Exit:** canonical lifecycle on both pinned hosts. Do not launch YMM4 for every malformed-schema case.

### P4 — UX

Primary validation:

- hands-on task script;
- state/invariant automation behind the UI;
- focused V1 only where an operation changes host behavior.

Core tasks:

- create;
- collapse/expand;
- remove folder metadata without deleting items;
- rename;
- understand hidden ownership;
- nested selection/navigation;
- keyboard actions where implemented;
- recover from unsupported/stale state.

No broad screenshot/pixel suite.
Two-host UX testing is needed only for host-dependent interaction differences, not styling.

### P5 — Compatibility

Do not test the full Cartesian product of:

```text
item types x operations x folder states x host versions
```

Use equivalence classes:

- voice;
- text;
- image;
- video;
- audio;
- shape;
- structurally distinct effect/transition;
- group/multi-layer;
- representative special/third-party item.

For each class, test only operations where its host behavior can differ.

Output a matrix of:

- supported;
- supported through common route;
- graceful fallback;
- known unsupported;
- version-specific.

### P6 — Hardening / Performance

Stress is a phase/RC tool, not an inner-loop tool.

Use defined scenarios:

- many layers/items/folders;
- deep valid nesting;
- repeated collapse/expand and edits;
- scroll/zoom/resize;
- project switching;
- detach/reload;
- idle/soak;
- failure injection;
- subscription/memory cleanup.

Long soak: primary host by default.
Secondary host: shorter compatibility smoke unless a version-specific risk exists.

Diagnostic counters are not performance claims without a benchmark contract.

### P7 — Release

Required:

- V0 clean;
- relevant V2 gates green;
- affected V3 golden suites green;
- both supported hosts;
- exact tested package/source identity;
- install/startup/basic operation;
- persistence recovery;
- disable/uninstall behavior where relevant;
- no Harmony reference/assembly;
- unsupported cases fail safely.

Do not replay every historical discovery probe. Verify current product contracts.

---

## 7. Default loop

Ordinary development:

```text
edit
 -> V0
 -> host-sensitive? focused V1 on primary host
 -> continue
```

Phase boundary:

```text
focused integrated matrix
 -> V2 on required pinned hosts
 -> manual hands-on where relevant
 -> freeze
```

Shared frozen mechanism change / RC:

```text
V0
 -> V1
 -> affected V2
 -> affected V3
 -> freeze/release
```

---

## 8. Anti-overengineering rules

- Do not add assertions just to increase the count.
- Do not duplicate one behavioral claim at multiple levels unless each catches a distinct failure mode.
- Do not run YMM4 for host-independent logic.
- Do not run both hosts by default during iteration.
- Do not make every green development run permanent Evidence.
- Do not build a new Lab abstraction until the need repeats.
- Do not create a product adapter just because an isolated Lab probe has one.
- Do not cross every compatibility dimension when equivalence classes establish the same mechanism.
- Do not use full golden regression as a substitute for knowing what changed.
- Do not remove frozen safeguards merely to make the test suite smaller.
- Prefer fewer assertions with clear failure meaning over a large assertion total.

Target balance:

> research-grade evidence at uncertain boundaries, ordinary engineering tests for known logic, release-grade regression at release-relevant boundaries.
