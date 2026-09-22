# YMM4 no-Harmony Full — Completion Roadmap v0.1.1

Status: **planning freeze for the remaining technical and product gates**.

This roadmap begins after feasibility stopped being the main unknown. It is intentionally a completion plan, not a frozen product specification.

## 1. Goal

Deliver a practical YMM4 timeline folder / layer-collapse plugin whose Full feature set works without Harmony while preserving YMM4-native behavior wherever possible.

The roadmap must answer three questions for every phase:

1. What is being proved?
2. What must be green before the next phase starts?
3. What is explicitly deferred?

Unknown product-policy choices are not guessed early. They are frozen only after the relevant YMM4 host behavior is measured.

## 2. Non-negotiable engineering rules

- **No Harmony** reference and no Harmony assembly loaded.
- Preserve YMM4 ownership of native behavior when a stable host route exists:
  - Frame movement;
  - snapping;
  - standard commands;
  - native Undo/Redo;
  - file/item creation.
- Bounded reflection / nonpublic setter access is allowed only where already justified by exact-host evidence and should stay narrowly scoped.
- Prefer public host surfaces when they are adequate.
- Do not weaken assertions to make a gate green.
- Do not reinterpret synthetic harness behavior as native-host proof.
- Keep exact pinned-host verification on **YMM4 4.55.1.1 Lite** and **4.56.1.0 Lite** until the compatibility phase deliberately expands that matrix.
- Frozen evidence from earlier phases remains unchanged. Follow-on work is stacked rather than rewriting historical proof.
- `main` is not the exploration branch. Lab gates stay isolated until their acceptance conditions are met.

### CWT operation routing

This project uses Chat Work Tools only as a **common operation layer**. CWT does not own folder semantics, YMM4-specific architecture, acceptance policy, or product UX.

Default routing for this roadmap:

- **Direct Work** — small source/docs/test changes that can be completed and verified in one pass.
- **Managed Work** — iterative implementation where regression, rollback or revision history matters.
- **Protected Execution** — only when a local task is genuinely long-running, expensive to recompute, or materially benefits from resumable checkpoints.
- **Native Validation** — exact YMM4/Windows behavior is part of acceptance. Local or pure tests do not substitute for this.
- **Public Lab** — the default place for redistribution-safe, shareable YMM4 host probes and reproducible CI evidence.
- **Project / Capability / Playbook** — folder semantics, YMM4-specific implementation choices, UX and compatibility decisions remain here rather than moving into CWT Core.

Operational precedence for this project:

```text
Safety / platform policy
  ↓
this Lab's Evidence / YMM4 host-observation rules
  ↓
this Completion Roadmap and phase acceptance
  ↓
CWT Default Work Policy
```

Practical consequences:

- do not force every small edit through workers, ledgers or checkpoints;
- do not stop useful independent work merely because CI is queued/in-progress;
- before retrying an uncertain operation, observe branch/commit/run/artifact/checkpoint state first;
- runner-start/quota/provider failures are not product-code failures;
- use pure Linux tests for host-independent logic such as FolderRangeTracker / StructuralDeltaDetector;
- spend native Windows/YMM4 runs only where host behavior is actually part of the claim;
- record expensive or difficult-to-reproduce progress at durable phase/freeze boundaries;
- CWT policy must not introduce YMM4-specific branches into its Core, and this project must not duplicate CWT's generic execution policy locally beyond the needed deltas.


### Product architecture convergence rules

The Lab architecture is intentionally more fragmented than the intended product architecture.

**Lab separation is evidence structure, not product structure.** Operation-specific probes/adapters may exist to isolate failures, but the Full product must not inherit a permanent one-adapter-per-probe design.

The product should converge toward this dependency direction:

```text
FolderDocument
    │
    ├── FolderRangeTracker
    │
    ▼
FoldMap
logical Layer <-> display Row
    │
    ├──────────────────────┐
    ▼                      ▼
FoldDisplay          InteractionRouter
                         ├─ placement
                         ├─ navigation
                         └─ native gesture lease
    │                      │
    └──────────┬───────────┘
               ▼
          YmmHostAccess
               │
               ▼
              YMM4

Persistence <-> FolderDocument
```

This diagram is a **target boundary**, not a requirement to refactor green Lab probes immediately.

Required convergence principles:

- **One mapping authority.** Display-row <-> logical-layer conversion belongs to FoldMap/FolderLayout semantics and must not be reimplemented independently by FileDrop, right-click, navigation or future add routes.
- **No adapter proliferation in the product.** Similar placement actions should flow through a shared placement route rather than permanent FileDrop/AddTemplate/AddCharacter-specific mapping implementations.
- **YMM4-private knowledge is centralized.** Reflection, nonpublic setters, exact view-model access, viewport mutation and other version-sensitive host access should converge behind `YmmHostAccess`.
- **Necessary drag complexity is encapsulated, not deleted.** Direct-geometry freeze, temporary viewport lease, RenderTransform compensation and deterministic restore remain because P0 proved they are behaviorally necessary. Product code should hide them behind a gesture-lifetime abstraction such as `GestureLease`.
- **Lab instrumentation does not become product architecture.** Mutation counters, render audits, assertion budgets and evidence logging remain in test/probe layers unless a minimal diagnostic is independently justified.
- **Prefer shared mechanisms over operation-specific exceptions.** A newly discovered YMM4 route first asks whether it fits placement, navigation, gesture or host-access boundaries before adding a new product abstraction.

Do **not** optimize `FolderLayout` representation yet merely for code-size reasons. Immutable collections can remain while correctness work is active. Array-backed `logicalToRow / rowToLogical / owner` tables are a later implementation option if they materially simplify or speed the product without weakening invariants.

Do **not** freeze the original LayerPatan `TimelineTracker / ShiftDetector` approach as the product mechanism yet. Its Harmony-free observation pattern is a strong candidate to compare during P1.3/P1.4, alongside routed-command observation and public UndoRedoManager signals.

## 3. Common phase workflow

Every technical phase follows the same sequence unless there is a documented reason not to:

**Discovery -> isolated proof -> integrated proof -> freeze**

### Discovery

Measure the real YMM4 behavior and identify the smallest usable host surface.

### Isolated proof

Prove one behavior without contaminating already-green architecture.

### Integrated proof

Run the behavior inside the current Full architecture and verify native semantics, geometry, Undo/Redo, liveness and cleanup together.

### Freeze

Record:
- source commit;
- workflow run;
- exact host versions;
- assertion count;
- artifact IDs / SHA256;
- proven behavior;
- explicit non-proven boundaries.

A phase is not complete merely because code exists.

---

# 4. Phase roadmap

## P0 — Core Spine

**Purpose:** prove that a Harmony-free Full architecture is technically viable.

### Scope

- generalized folder layout mapping;
- folded display geometry / extent;
- native click and right-click semantics;
- Shift marquee;
- single and multi-item native drag;
- native Frame / snap ownership;
- native Undo/Redo;
- virtualization;
- nested collapse/reopen;
- drag-time visual continuity;
- detach / restore;
- real FileDrop integration.

### Current status

**COMPLETE / FROZEN**

Evidence chain:

- Track A #70 — **48/48** strict assertions on both pinned hosts.
- Track B #71 — **120/120** strict assertions on both pinned hosts.
- Track C core #72 — **107/107** strict assertions on both pinned hosts.
- Track C + real FileDrop #72:
  - source `c463699d084d6bc09a901676af656292386d45e1`;
  - run `35673103213`;
  - **134/134** strict assertions on both pinned hosts;
  - 4.55.1.1 artifact `10671434182`, SHA256 `9867ae36971e201c732a2287689da73bd0ef99a395119e8485bfa82e1bc075d0`;
  - 4.56.1.0 artifact `10671564054`, SHA256 `e2ade8857bf30f134819ac5c98071eedd9467a5ece724ebcfca92d141112de80`.

### Exit condition

Already satisfied.

### Deferred

All structural folder-range tracking, persistence, product UX and release work.

---

## P1 — Structural Tracking

**Purpose:** keep folder ranges correct when YMM4's real layer structure changes, with native Undo/Redo semantics preserved.

### P1.1 Host structural semantics

Measure standard YMM4 commands for:

- Add Layer;
- Delete Layer;
- Move Up / Move Down;
- selection changes;
- native Undo / Redo boundaries.

**Current status: first gate COMPLETE.**

PR #75 source `fb9df40ab7fd3e3ce99bae9ec21384833db1f90f`, run `35673866306`:

- **23/23** strict assertions on 4.55.1.1;
- **23/23** strict assertions on 4.56.1.0;
- standard routed commands form one correct native Undo/Redo operation;
- observed semantics:
  - Add L3 -> old L3+ shift by +1;
  - Delete L3 -> L3 is removed and old L4+ shift by -1;
  - Move Down L3 -> L3 and L4 swap.

Direct `Timeline.AddLayer/DeleteLayer/MoveLayer` calls are **not** treated as equivalent user-operation proof because direct invocation did not by itself produce the desired user-level history boundary.

### P1.2 FolderRangeTracker model

**Current status: COMPLETE / FROZEN.**

PR #76 source `0124fdaf86862927608121b296c3520f66ca0efe`, run `35681179713`:

- pure Linux runner;
- no YMM4 / WPF / Windows dependency;
- `PASS_FOLDER_RANGE_TRACKER`;
- **41/41** policy and boundary assertions;
- artifact `10674367679`;
- ZIP SHA256 `99f66fb0b839ab59d402a50aa906f4170ac21119a1d12735e6e658a08c0038eb`.

The frozen tracker is separate from WPF display code **and separate from YMM4 host types**.

P1.2 product boundary:

- `FolderRangeTracker` depends only on logical folder/span state and structural edit values;
- it does not depend on `TimelineView`, `TimelineViewModel`, RoutedCommand, WPF coordinates, reflection or DirectDisplay;
- host observation must be translated into small structural operations before the tracker sees it;
- the same tracker is executable without launching YMM4.

Frozen positional policy:

- folders are contiguous logical-position ranges rather than ownership attached to a particular YMM4 row object;
- Insert before or at a folder head shifts the folder;
- Insert with `Start < P <= End` joins the inserted row to the folder and expands End;
- Delete before shifts the folder upward;
- Delete inside shrinks it;
- Delete at the head promotes the next surviving row at the same numeric head;
- deleting the whole interval removes that folder metadata;
- a one-row folder may survive deletion; minimum folder size is a creation-UX concern;
- if delete makes a nested folder share its parent's head, the nested head is normalized downward and is pruned if it becomes empty;
- standard MoveUp/MoveDown is modeled as an adjacent row swap while folder numeric ranges remain fixed, so a row crossing a boundary changes membership;
- moving a whole folder is a separate explicit product operation and is not inferred from a standard single-layer reorder.

Frozen invariants:

- folder IDs are unique and non-empty;
- `Start >= 0` and `End >= Start`;
- nested or disjoint spans are valid;
- crossing spans are invalid;
- two folders may not share the same head layer.

The policy was compared with the public MIT-licensed `bluemistel/YMM4-LayerPatan` structural core. Its positional insert/delete ideas are compatible with this boundary, while its YMM4 integration, persistence, group-range correction, UI and Harmony patch architecture are not imported.

P1.5 still has to prove these semantics under the Track C folded display, especially standard layer reorder across folder boundaries.

### P1.3 Native command observation boundary

Detect the actual standard structural operation without replacing YMM4's implementation.

Preferred order:

1. standard WPF / routed-command observation;
2. public YMM4 events / state deltas;
3. bounded fallback only if the first two cannot distinguish required operations reliably.

Do not reimplement standard Add/Delete/Move merely to make the tracker easier.

P1.3 must also compare **general structural-delta observation** against command-specific observation. The existing Harmony-free LayerPatan-style approach—watching Timeline / LayerSettings plus UndoRedoManager Recorded/Undoed/Redoed and comparing before/after Layer/LayerSetting state—is a candidate because it could cover several structural operations without adding one adapter per command.

Selection rule:

- prefer the smallest observation mechanism that reliably identifies the structural delta needed by FolderRangeTracker;
- command-specific hooks are acceptable as Lab evidence, but should not multiply product adapters when a shared delta observer is equally reliable;
- if routed-command observation and state-delta observation are combined, each must have a clear responsibility rather than duplicate folder logic.

### P1.4 Undo/Redo synchronization

Folder-range state must move with the same user-visible history step as the YMM4 structural operation.

Investigate and use the smallest appropriate public surface from `UndoRedoManager`, including as relevant:

- `Subscribe(IUndoRedoable)`;
- `UnSubscribe`;
- `AddCommand(IUndoRedoCommandBase)`;
- `Record()`;
- `Recorded`;
- `Undoed`;
- `Redoed`;
- `HistoryChanged`.

Acceptance requires **one standard layer action -> one Ctrl+Z -> both YMM4 layer state and folder state return together**.

### P1.5 Track C integration

Connect the tracker to the P0 display/input architecture.

Verify after Add/Delete/Move and Undo/Redo:

- folded geometry;
- row chrome;
- extent;
- virtualization;
- right-click/add-position mapping;
- click;
- drag;
- FileDrop;
- no stale hidden rows;
- detach / restore.

### P1.6 Architecture Convergence Gate

After the integrated P1 behavior is green, but **before P1 is frozen**, review the product-facing structure.

This gate does not require rewriting every Lab probe. It requires proving that the intended product implementation can converge without losing the verified behavior.

Checklist:

- FolderRangeTracker is host/WPF-independent.
- FoldMap/FolderLayout remains the single coordinate-mapping authority.
- right-click, FileDrop and future placement paths can share one placement boundary instead of permanent operation-specific mapping code.
- YMM4 version-sensitive reflection/nonpublic access has a defined `YmmHostAccess` boundary.
- native drag complexity can be encapsulated behind one gesture-lifetime boundary without removing the P0 protections.
- Lab-only counters/audits/logging are clearly separable from runtime product code.
- no new P1 component duplicates mapping, Undo or host-access logic already owned elsewhere.
- a convergence refactor, if needed, reruns the integrated P1 suite before freeze.

Only after this gate is green should P1 be frozen and P2 add more host-interaction routes.

### P1 exit gate

Both pinned hosts must pass an integrated structural suite covering:

- Add;
- Delete;
- Move Up;
- Move Down;
- nested folders;
- boundary cases;
- repeated edit sequences;
- Ctrl+Z / Ctrl+Y;
- post-history native interaction;
- no Harmony.

Only then is P1 frozen.

### Explicitly deferred from P1

- project-file persistence format;
- polished folder UI;
- broad third-party item compatibility;
- performance claims.

---

## P2 — Host Interaction Coverage

**Purpose:** eliminate remaining places where folded display coordinates can diverge from YMM4's logical coordinates.

### Scope

- other native add routes beyond proven right-click and FileDrop;
- automatic navigation / ScrollToItem-style routes;
- host-driven selection / seek routes;
- keyboard layer navigation;
- hidden-destination policy;
- group / multi-layer movement routes that exercise different native handlers;
- context-menu actions that derive a layer from cursor position.

### Exit gate

A documented interaction matrix is green on both pinned hosts, with every supported route classified as:

- native-safe without adaptation;
- mapped through FolderLayout;
- intentionally unsupported with graceful behavior.

No harness-only `Reveal` call may be cited as proof of automatic host navigation.

### Deferred

Persistence schema and final visual design.

---

## P3 — Folder Data Model & Persistence

**Purpose:** make folder state survive normal project lifecycle safely.

### Scope

- folder identity;
- range / hierarchy representation;
- collapse state;
- names / minimal metadata needed by the Full product;
- save / reopen;
- project switch;
- new project;
- malformed / stale state recovery;
- schema versioning and migration;
- safe behavior when the plugin is unavailable or an older version is loaded.

### Design rule

Do not make display geometry the persisted source of truth. Persist logical folder state and rebuild display state.

### Exit gate

Round-trip tests must prove:

- save -> close -> reopen preserves valid structure;
- invalid persisted state fails safely;
- migration is deterministic;
- no project corruption if the plugin cannot restore a folder;
- structural edits after reload continue to pass P1 semantics.

---

## P4 — Product UX

**Purpose:** turn the proven engine into a practical editor feature.

### Minimum Full UX candidates

- create folder / define range;
- collapse / expand;
- delete folder metadata without deleting user items;
- rename;
- clear visual ownership of hidden layers;
- selection feedback;
- nested-folder discoverability;
- keyboard-accessible core actions where reasonable.

### UX constraints

- do not require selecting the plugin itself before normal timeline work;
- do not steal standard YMM4 gestures when native semantics can be preserved;
- destructive actions require clear outcome;
- failure or unsupported state must degrade to a usable timeline rather than trap the project.

### Exit gate

Hands-on tasks must be possible without lab-only controls or manual state injection.

Fine styling, animation and optional convenience features can remain deferred.

---

## P5 — Compatibility Coverage

**Purpose:** expand beyond the minimal marker / voice-item evidence.

### Scope

Representative coverage for:

- voice;
- text;
- image;
- video;
- audio;
- shape;
- effect / transition where relevant;
- grouped items;
- multi-layer / special host items;
- reasonable third-party item implementations;
- host version differences.

### Exit gate

A compatibility matrix documents:

- confirmed support;
- graceful fallback;
- known unsupported behavior.

Version differences must not silently corrupt folder state.

---

## P6 — Hardening & Performance

**Purpose:** prove the feature remains stable in normal long sessions and larger projects.

### Scope

- large layer counts;
- many timeline items;
- many folders;
- deep but valid nesting;
- repeated collapse / expand;
- repeated structural edits;
- long idle;
- scroll / zoom / resize;
- project switching;
- plugin detach / reload where applicable;
- failure injection;
- memory / subscription cleanup;
- mutation / refresh budget observation.

### Rule

Stress counters are diagnostics, not performance claims unless measured under a defined benchmark.

### Exit gate

No runaway refresh loop, persistent stale geometry, leaked subscriptions, or history corruption in the defined soak scenarios.

---

## P7 — Release Gate

**Purpose:** produce a distributable Full candidate.

### Scope

- package layout;
- plugin metadata;
- README / install / uninstall;
- compatibility statement;
- version / schema policy;
- known limitations;
- recovery instructions;
- release artifact verification.

### Full candidate acceptance

A release candidate is acceptable only when:

- P0-P6 are frozen green;
- no Harmony dependency is present;
- both pinned hosts pass the final acceptance suite;
- persistence has a recovery path;
- unsupported cases fail safely;
- packaged bits match the tested bits.

---

# 5. Current critical path

The active path is:

```text
P0 Core Spine                 DONE
  -> P1.1 host semantics      DONE
  -> P1.2 FolderRangeTracker  DONE
  -> P1.3 operation observe    NEXT
  -> P1.4 Undo synchronization
  -> P1.5 Track C integration
  -> P1.6 architecture convergence
  -> P1 freeze
  -> P2 interaction coverage
  -> P3 persistence
  -> P4 UX
  -> P5 compatibility
  -> P6 hardening
  -> P7 release
```

P1 may loop between P1.2-P1.4 if host evidence exposes a missing boundary case. That is expected and does not reopen P0.

# 6. Scope-control rules

To avoid turning the Full exploration into an unbounded rewrite:

- New functionality enters the roadmap only if it is required for correct folder semantics, normal Full UX, compatibility, or release safety.
- Nice-to-have features discovered during P1-P3 are recorded under the later appropriate phase instead of implemented immediately.
- A phase can add a new sub-gate, but must not silently expand an already-frozen earlier gate.
- If a host behavior cannot be supported safely without Harmony, document the exact missing surface before considering architectural escalation.
- Simpler architecture wins when it preserves the same verified behavior.
- Lab class/file count is not a product-design target. Simplification should remove duplicated entry paths and centralize ownership, not erase proven behavioral safeguards.
- Do not perform a broad P0 rewrite merely to match the target diagram. Converge incrementally at phase boundaries and rerun the relevant integrated gate.

# 7. Definition of “Full is feasible” vs “Full is complete”

**Feasible:** already demonstrated by the P0 spine plus real FileDrop and the P1.1 standard structural-command proof.

**Complete:** not yet. Completion requires the remaining P1-P7 gates, especially structural folder tracking, persistence, product UX and hardening.

This distinction should remain explicit in PRs and release notes.
