# YMM4 no-Harmony Full — Validation Policy v0.1

Status: **proposed policy for P2-P7**.

This policy starts after P0 Core Spine and P1 Structural Tracking were frozen green.
Its purpose is to keep the remaining work rigorous without repeating research-grade validation on every change.

The central rule is:

> Development checks, canonical host evidence, and release regression are different jobs. Do not run all three at the same density by default.

The frozen P0/P1 suites remain valuable as golden regression assets. They are not the default inner-loop test suite.

---

## 1. Validation levels

### V0 — Pure / fast verification

Use for host-independent logic and ordinary source changes.

Typical contents:

- unit tests;
- invariant/property tests;
- deterministic model tests;
- compile/build;
- serialization/schema tests that do not require YMM4;
- static fixture tests.

Expected use: **every relevant change**.

Examples:

- `FolderRangeTracker`;
- `StructuralDeltaDetector`;
- `FoldMap` / logical-display mapping;
- persistence schema/migration logic;
- hidden-destination policy when expressed as pure policy.

V0 does not establish undocumented YMM4 host behavior.

### V1 — Focused native development smoke

Use when a change touches real YMM4 interaction or a version-sensitive host surface.

Default:

- one primary pinned host;
- one narrow scenario or a small representative set;
- machine-checkable assertions;
- no requirement to reproduce the full canonical Evidence chain unless the result will be cited as a host claim.

Current primary development host: **YMM4 4.56.1.0 Lite**.

Typical V1 targets:

- one placement route;
- one navigation route;
- one Undo transaction;
- one drag gesture;
- one save/reopen route;
- one host adapter capability.

V1 answers “did this integration still work?” rather than “have we re-proved the whole architecture?”.

### V2 — Phase acceptance / canonical host evidence

Use at a phase freeze, or when a new undocumented host behavior becomes a product dependency.

Requirements:

- both pinned hosts when the behavior is part of the supported compatibility claim:
  - YMM4 4.55.1.1 Lite;
  - YMM4 4.56.1.0 Lite;
- focused integrated matrix for the mechanisms changed by the phase;
- exact PASS / NOT PROVEN boundary;
- canonical Evidence identity according to Lab policy:
  - source HEAD;
  - actual tested checkout when different;
  - workflow run;
  - exact host/version;
  - assertions;
  - artifact;
  - digest/hash.

V2 is where exploratory or V1 findings become durable evidence.

### V3 — Golden regression / release confidence

Use only when a change can invalidate already-frozen cross-cutting behavior, or at release-candidate boundaries.

Candidates include:

- frozen P0 Track C acceptance;
- frozen P1 final integrated structural suite;
- equivalent product-extraction golden suites once they exist.

Trigger examples:

- `FoldMap` semantics change;
- folded display geometry changes;
- `YmmHostAccess` changes a shared host surface;
- Undo ownership changes;
- `GestureLease` internals change;
- placement/navigation routing changes in a way shared by multiple operations;
- major dependency/runtime change;
- release candidate.

Do **not** run V3 merely because a leaf UI command, label, or isolated route changed.

---

## 2. Manual hands-on validation is a separate track

Manual checks are not a weaker form of V2; they answer different questions.

Use manual live observation for:

- visual discoverability;
- interaction comfort;
- animation quality;
- perceived drag continuity;
- keyboard ergonomics;
- error/recovery wording;
- workflows that are difficult to automate without testing the harness rather than the product.

Record:

- exact host version;
- task performed;
- expected result;
- observed result;
- remaining uncertainty.

Do not turn visual/perceptual observations into automated PASS claims without a corresponding machine test.

Avoid pixel-perfect automation unless pixel identity itself is the product requirement.

---

## 3. Change-to-validation routing

| Change | Default validation |
| --- | --- |
| Pure folder/model/policy logic | V0 |
| Pure persistence/schema/migration logic | V0 |
| Docs / wording / test comments | V0 or no runtime test |
| New route using an already-proven mapping mechanism | V0 + focused V1 |
| New undocumented YMM4 behavior | isolated host probe first; V1 discovery, then V2 if adopted |
| `YmmHostAccess` change | V0 + V1; V2/V3 if shared contract changes |
| `FoldMap` / display-row mapping semantics | V0 + V1 + affected V3 before freeze |
| Folded display geometry / virtualization | V0 + V1 + affected V3 before freeze |
| Undo transaction ownership | V0 + V1 + affected V3 before freeze |
| Gesture / drag lifecycle | V0 + V1 + affected V3 before freeze |
| Leaf UI styling | manual hands-on; no native full regression by default |
| UX interaction path that changes host behavior | manual + focused V1; V2 at phase exit |
| Host version bump | revalidate affected subsystem; do not blindly replay all historical probes |
| Package/runtime/dependency change | build/package smoke + focused V1; V3 for RC |
| Release candidate | V0 + required V2 phase gates + V3 + package/install/recovery checks |

If uncertain, choose the smallest level that can falsify the changed assumption.

---

## 4. Two-host policy

Running both pinned YMM4 versions is valuable, but not necessary for every edit.

Default:

- **development / iteration:** newest pinned supported host only;
- **phase freeze:** both pinned hosts for host-dependent acceptance;
- **release candidate:** both pinned hosts;
- **version-sensitive discrepancy:** both hosts immediately;
- **pure logic:** no YMM4 host.

When support moves to a newer YMM4 version, re-run the subsystem that materially changed first.
Do not treat an unrelated YMM4 update as a reason to replay every historical experiment.

---

## 5. Evidence and artifact policy

### Development checks

A V0/V1 development check may remain ordinary CI evidence.

It does not need to be promoted into a permanent host-observation record unless it is relied on as a product claim.

### Canonical evidence

V2/V3 evidence keeps the full Lab chain:

```text
source / tested tree
  -> workflow run
  -> exact host
  -> assertions
  -> artifact
  -> digest
```

Independent artifact SHA256 checking is most useful at:

- phase freeze;
- architecture freeze;
- release candidate;
- difficult-to-reproduce or expensive host proof.

Do not require a new permanent artifact/digest record for every inner-loop smoke run.

---

## 6. Frozen P0/P1 treatment

P0 and P1 are **golden evidence**, not ordinary development loops.

Keep:

- P0 frozen host-interaction/Track C evidence;
- P1 `FolderRangeTracker` and `StructuralDeltaDetector` pure suites;
- P1 final integrated structural acceptance;
- the exact evidence records already produced.

Default behavior from P2 onward:

- run cheap pure P0/P1-derived logic tests whenever relevant;
- do not rerun the full native P0/P1 suites for unrelated work;
- run the affected frozen native suite when a shared invariant is changed;
- run the final golden set again at the release-candidate boundary.

A failure in new work should reopen only the affected frozen assumption, not automatically restart P0/P1.

---

## 7. P2 — Host Interaction Coverage

Goal: cover remaining routes where folded display coordinates may diverge from logical YMM4 coordinates.

### Discovery

Inventory routes and classify each as:

1. native-safe without adaptation;
2. mapped through existing `FoldMap` / interaction routing;
3. unsupported with graceful behavior.

For a genuinely new host route:

- use one narrow Lab question;
- prove it first on the primary host;
- avoid creating a permanent product adapter until the mechanism is understood.

### Representative mechanism groups

Prefer one or two representative cases per mechanism rather than a Cartesian matrix:

- placement/add routes;
- automatic navigation / ScrollToItem-like behavior;
- host-driven selection / seek;
- keyboard layer navigation;
- hidden-destination policy;
- group / multi-layer movement;
- context-menu layer resolution.

Include nested/hidden-boundary coverage only where that boundary can change the result.

### P2 exit

- V0 for policy/mapping logic;
- focused native integration for each supported mechanism group;
- both pinned hosts for the final supported-route matrix;
- no need to replay all 353 P1 assertions unless P2 changes a P1 shared mechanism.

---

## 8. P3 — Folder Data Model & Persistence

Persistence should be mostly cheap and deterministic.

### V0-heavy coverage

Test without YMM4:

- schema round-trip;
- hierarchy/range invariants;
- collapse state;
- naming/minimal metadata;
- malformed data;
- stale references;
- migrations;
- version skew;
- deterministic recovery policy.

### Native coverage

Use YMM4 only for lifecycle behavior that requires the host:

- save -> close -> reopen;
- project switch;
- new project;
- plugin unavailable / recoverable fallback when practical;
- one structural edit after reload to prove P1 semantics still attach correctly.

Do not execute every malformed-schema case through a full YMM4 launch if the same parser/recovery logic is already covered at V0.

P3 freeze uses both pinned hosts for the canonical lifecycle path.

---

## 9. P4 — Product UX

P4 should not become a pixel-test project.

Primary verification:

- hands-on task scripts;
- state/invariant automation behind the UI;
- focused native smoke only when a UX operation changes host interaction.

Core tasks:

- create folder;
- collapse / expand;
- remove folder metadata without deleting items;
- rename;
- identify hidden ownership;
- select/navigate nested folders;
- keyboard-accessible core actions where implemented;
- recover from unsupported/stale state.

One good end-to-end hands-on task is more valuable than many brittle screenshot assertions.

Run both hosts only when the UX relies on a host surface known to differ; styling alone does not require a two-host gate.

---

## 10. P5 — Compatibility Coverage

Avoid the full product of:

```text
item types x operations x folder states x host versions
```

Use equivalence classes.

Representative classes:

- voice;
- text;
- image;
- video;
- audio;
- shape;
- effect/transition where structurally different;
- grouped/multi-layer item;
- representative third-party/special item when available.

For each class, test only operations where its host behavior can differ.

A single common behavior can cover multiple classes if they enter the same verified host mechanism.

The output is a compatibility matrix:

- supported;
- supported via common route;
- graceful fallback;
- known unsupported;
- host-version-specific.

---

## 11. P6 — Hardening & Performance

Stress tests are phase/RC tools, not inner-loop tests.

Use defined scenarios such as:

- many layers/items/folders;
- deep valid nesting;
- repeated collapse/expand;
- repeated structural edits;
- scroll/zoom/resize cycles;
- project switching;
- detach/reload;
- long idle/soak;
- failure injection;
- subscription/memory cleanup.

Policy:

- long soak on the primary host unless evidence shows a version-specific reason for duplicate long runs;
- shorter compatibility smoke on the secondary host;
- measure only defined metrics;
- do not turn diagnostic counters into performance claims without a benchmark contract.

---

## 12. P7 — Release Gate

Release validation should be strict but finite.

Required:

- V0 clean;
- relevant phase-acceptance gates green;
- affected golden V3 suites green;
- both supported pinned hosts;
- package built from the same tested source;
- install/startup/basic operation;
- persistence recovery path;
- uninstall/disable behavior where relevant;
- no Harmony reference/assembly;
- known unsupported cases fail safely;
- artifact identity recorded.

Do not rerun every historical discovery probe. The release suite should verify the current product contracts, not reproduce the entire research history.

---

## 13. Practical default loop from P2 onward

For ordinary development:

```text
edit
 -> V0
 -> if host-sensitive: focused V1 on primary host
 -> continue
```

At a phase boundary:

```text
focused integrated suite
 -> both pinned hosts (V2)
 -> manual hands-on where relevant
 -> freeze evidence
```

When a shared frozen mechanism changes or at RC:

```text
V0
 -> focused V1
 -> affected V2
 -> affected golden V3
 -> freeze/release evidence
```

This is the default. Escalate only when the changed assumption requires it.

---

## 14. Anti-overengineering rules

- Do not add assertions merely to increase assertion count.
- Do not duplicate the same behavioral claim at pure, synthetic, and native levels unless each level catches a distinct failure mode.
- Do not run two YMM4 versions when the test contains no YMM4 behavior.
- Do not make every successful development run permanent Evidence.
- Do not build new Lab abstractions before the same need appears more than once.
- Do not create a new product adapter just because a Lab probe has its own adapter.
- Do not cross every dimension of the compatibility matrix when equivalence classes establish the same mechanism.
- Do not use full golden regression to compensate for unclear change ownership; first identify which contract changed.
- Do not delete frozen safeguards merely to reduce test count.
- Prefer fewer assertions with clear failure meaning over a large assertion total.

The desired end state is **research-grade evidence at uncertain boundaries, ordinary engineering tests for known logic, and release-grade regression only at release-relevant boundaries**.
