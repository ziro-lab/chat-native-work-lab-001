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

Validation depth for P2-P7 is governed by [`YMM4_NO_HARMONY_VALIDATION_POLICY.md`](YMM4_NO_HARMONY_VALIDATION_POLICY.md). Frozen P0/P1 native suites are golden regression assets, not the default inner-loop test set.

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

**Status: COMPLETE / FROZEN.**

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

**Current status: COMPLETE / FROZEN.**

The product-facing observation boundary is:

```text
UndoRedoManager transaction trigger
        +
before/after Timeline item-layer snapshot
        +
native empty-LayerSetting insert hint when available
        +
one generic WPF RoutedCommand position hint when state is underdetermined
        ↓
StructuralDeltaDetector
        ↓
InsertLayers / DeleteLayers / SwapAdjacentLayers
        ↓
FolderRangeTracker
```

No Add/Delete/Move-specific folder adapters are required.

#### P1.3a — pure StructuralDeltaDetector

Initial pure source `e0b7e6ffb8c264ff026a68839aac156326096837`, run `35681491437`:

- host-independent detector;
- conservative Ambiguous result instead of guessed sparse boundaries;
- Insert/Delete/adjacent Swap classification;
- individual item movement is not accepted as structural mutation.

P1.3c later extended the same detector with an optional generic operation-position hint.

#### P1.3b — exact-host shared observer

Source `55ef1c12b379b7a31253e457ff0ea7440819ced0`, run `35689361090`:

- **62/62** on YMM4 4.55.1.1;
- **62/62** on YMM4 4.56.1.0;
- public `UndoRedoManager.Recorded / Undoed / Redoed` events are usable common transaction triggers;
- dense Add/Delete/MoveDown and Undo/Redo/reset classify exactly through one shared state observer;
- sparse Add L24 is exact through YMM4's empty-LayerSetting insertion hint;
- sparse Delete of a completely empty L24 between observed L20/L30 is genuinely underdetermined from state alone on both hosts.

P1.3b artifacts:

- 4.55.1.1: `10677727515`, SHA256 `404b359dbdcc646b2e1c7f6c1931756d653605139d05ebf375714ac020d67192`;
- 4.56.1.0: `10678401894`, SHA256 `6f95107d738a5f87dc3c2d7f9043d6a7a39704bee67c172345edf4a0341aa781`.

#### P1.3c — generic routed structural position hint

Frozen source `22bbe06494729f2c89b72d733070d3962a840943`.

Pure run `35689743836`:

- `PASS_STRUCTURAL_DELTA_DETECTOR`;
- **37/37** assertions;
- unique operation-position hint may resolve an already-observed sparse structural gap;
- multiple/conflicting operation hints remain Ambiguous;
- detector remains host-independent.

Native run `35689743833`:

- `PASS_STRUCTURAL_OBSERVER_NATIVE`;
- **73/73** on YMM4 4.55.1.1;
- **73/73** on YMM4 4.56.1.0;
- one handled-events-too WPF RoutedCommand observer sees Add/Delete/MoveDown and their integer layer parameters;
- dense classification remains exact from state delta;
- sparse Add L24 raw = `Exact:I:24:1`, hinted = same;
- sparse Delete L24 raw = `Ambiguous`, command hint = `DeleteLayer:24`, hinted = `Exact:D:24:1`;
- no Harmony.

P1.3c artifacts, independently downloaded and SHA256-checked:

- pure: `10677622965`, SHA256 `b554c7e4098be1429d2ff4ec0fe2c7a5f908b6ba529cca53ecb4895af1c4c699`;
- 4.55.1.1: `10677318394`, SHA256 `4ea75e6b1fd3112640e3abb164b39c7acf14380959b073c15a1275015274f763`;
- 4.56.1.0: `10678207901`, SHA256 `7f8169b8bb5e669a80940e4a4dcd2498e64607e2d3ab7b69ec33b3008317035a`.

#### Frozen observation rule

1. State delta is authoritative when it is exact.
2. Native empty-LayerSetting changes are generic insert hints.
3. A single generic RoutedCommand position hint may resolve an otherwise-under-determined structural gap.
4. Command hints do not contain folder policy and do not bypass the pure detector.
5. Conflicting/insufficient evidence remains Ambiguous.
6. Standard YMM4 command implementation is never replaced.

### P1.4 Undo/Redo synchronization

**Current status: COMPLETE / FROZEN.**

PR #81 source `868004204778fbe45802013f8ba7de3e606e704f`, run `35692780255`:

- `PASS_UNDO_SYNC`;
- **74/74** strict assertions on YMM4 4.55.1.1;
- **74/74** strict assertions on YMM4 4.56.1.0;
- standard Add/Delete/MoveDown RoutedCommands remain host-owned;
- plugin folder state is computed by the frozen FolderRangeTracker during handled-events-too PreviewExecuted;
- the plugin calls public `UndoRedoManager.AddCommand(new UndoRedoActionCommand(...))`;
- the plugin does **not** call `Record()` during the structural command;
- YMM4's own standard structural `Record()` commits the pending folder command into the same user-visible history unit;
- one Ctrl+Z restores Timeline + Folder state together;
- one Ctrl+Y restores both together;
- positional MoveDown produces no folder-state change and therefore adds no plugin Undo command;
- no Harmony.

Artifacts, independently downloaded and SHA256-checked:

- 4.55.1.1: `10679068161`, SHA256 `ac9643650056063872fc7100ed8494229a7b58550dc69d493cd4854d0a2e640f`;
- 4.56.1.0: `10678638727`, SHA256 `7083b5e511aff163ee067db28101f6ba406707eeb841173f3fc5688eab61a682`.

Frozen transaction rule:

```text
Preview standard structural command
  -> FolderRangeTracker
  -> update FolderDocument state
  -> UndoRedoManager.AddCommand(folder undo/redo)
  -> no plugin Record()
  -> YMM4 standard command continues
  -> YMM4 Record() commits one combined user transaction
```

### P1.5 Track C integration

**Current status: COMPLETE / FROZEN.**

The P1 structural model, observer and same-transaction Undo mechanism were integrated into the frozen P0 Track C folded display/input architecture without rewriting the previously green Track C mechanisms.

#### P1.5a — structural display/history spine

Source `d2ca75c53c92feb8edf2e6b10e1897b9e6186d5f`, run `35693945595`:

- `PASS_TRACK_C_STRUCTURAL`;
- **108/108** on both pinned hosts;
- Add L3 / Delete L3 / MoveDown L3 prediction equals post-host StructuralDeltaDetector result;
- one native Record boundary per structural edit;
- one-step Timeline + Folder Undo/Redo;
- folded geometry / extent remain exact through forward / Undo / Redo / reset;
- no stale hidden item views;
- native click and right-click mapping remains correct after a structural mutation.

A host behavior discovered here is intentionally reflected in the test ordering: native click/right interaction can create another YMM4 history boundary, so post-mutation interaction is tested separately rather than placing that history entry above the structural Undo unit being measured.

Artifacts:

- 4.55.1.1: `10679103302`, SHA256 `5c9f500f150ee760f1f6dc02e350002e67d6ff3d0a91a724a0a37231c5fa06a2`;
- 4.56.1.0: `10679975336`, SHA256 `4b25463e31dc11564edeaeb82c618a676b724533804deb24689f935471a3e7a0`.

#### P1.5b — post-structural native drag + real FileDrop

Source `0cfc6026dd3011f35a1858ad40d1ca13d422adea`, run `35694253060`:

- `PASS_TRACK_C_STRUCTURAL`;
- **156/156** on both pinned hosts;
- after real standard Add L3, collapsed folder state is A1..6 / B2..5 / C7..9;
- native drag maps the visible C owner from logical L7 to logical L10;
- native Frame delta remains +35 on both hosts;
- drag Undo/Redo/reset is exact while Folder state remains unchanged;
- real PNG FileDrop on the folded visible route executes native AddFileItem;
- added item is post-corrected to logical L10;
- FileDrop Undo/Redo/reset is exact while Folder state remains unchanged;
- geometry / extent / no-reentry / no-hidden-view remain green.

Artifacts:

- 4.55.1.1: `10680220486`, SHA256 `f9436ddfd2f135b033d86cc57fc1bc2117725b6829b699bc9055265fbdfbc826`;
- 4.56.1.0: `10680200520`, SHA256 `574e2b7e497bd4b6b6d15808156953d28b49af7db41aa8efd2922042ea36a810`.

#### P1.5c — Full structural acceptance

Frozen source `05237928242f6a182a74b7e63090512dd4693212`, run `35695949808`:

- `PASS_TRACK_C_STRUCTURAL`;
- **353/353** on YMM4 4.55.1.1;
- **353/353** on YMM4 4.56.1.0;
- explicit MoveUp L4 -> exact Swap(3);
- Add exactly at outer folder owner L1 -> exact Insert(1,1) and folder shift;
- Delete outer owner L1 -> exact Delete(1,1), surviving owner promotion and nested-head normalization;
- repeated four-operation native sequence:
  - Add owner;
  - Delete inserted layer;
  - MoveUp;
  - inverse MoveDown;
- exactly four native history units are produced;
- four Ctrl+Z operations traverse every expected Timeline + Folder intermediate state;
- four Ctrl+Y operations traverse every expected state forward;
- geometry / extent / no-reentry / no-hidden-view remain green at every history state;
- post-history native click/right mapping remains correct;
- no Harmony.

Artifacts, independently downloaded and SHA256-checked:

- 4.55.1.1: `10679749216`, SHA256 `79f54961559feed7c8e0d6ee4041979f72faa32c29056fc6e80be472226f4f9f`;
- 4.56.1.0: `10679918889`, SHA256 `78c30ddf73019c497ab2dc03bf269307785192dd53f5db4701c3239f320ea57f`.

P1.5 therefore covers the P1 exit interaction matrix:

- Add;
- Delete;
- Move Up;
- Move Down;
- nested folders;
- owner/boundary cases;
- repeated edit sequences;
- Ctrl+Z / Ctrl+Y;
- folded geometry / extent / virtualization behavior inherited from Track C;
- click / right-click;
- native drag;
- real FileDrop;
- no stale hidden rows;
- no Harmony.

### P1.6 Architecture Convergence Gate

**Current status: COMPLETE / FROZEN.**

Review: `docs/YMM4_NO_HARMONY_P1_ARCHITECTURE_CONVERGENCE.md`.

The review compared the frozen P0/P1 implementation against the product convergence rules and found **no broad Lab refactor is required before P1 freeze**.

Checklist result:

- FolderRangeTracker is host/WPF-independent — **green**.
- StructuralDeltaDetector is host/WPF-independent — **green**.
- FolderLayout/FoldMap is the single display-row <-> logical-layer mapping authority — **green**.
- right-click and FileDrop already delegate coordinate conversion to the same FolderLayout — **green for convergence**; the final product should expose one shared PlacementRouter rather than preserving Lab adapter names.
- version-sensitive reflection/nonpublic YMM4 access has a defined YmmHostAccess extraction boundary — **green**.
- native drag complexity already has an explicit BeginGesture / UpdateGestureVisuals / EndGesture lifetime and can be hidden behind a product GestureLease without deleting the proven protections — **green**.
- Lab mutation counters, render audits, budgets and evidence logging are separable from runtime product behavior — **green**.
- P1 structural observation, range transformation and Undo ownership are separate and do not duplicate folder policy — **green**.
- no convergence refactor is required inside the frozen Lab probes; rewriting those probes now would reduce evidence stability without simplifying the eventual product — **green**.

The product extraction target remains:

```text
FolderDocument
    |
    +-- FolderRangeTracker
    |
    v
FoldMap
logical Layer <-> display Row
    |
    +----------------------+
    v                      v
FoldDisplay          InteractionRouter
                         +-- placement
                         +-- navigation
                         +-- GestureLease
    |                      |
    +----------+-----------+
               v
          YmmHostAccess
               |
               v
              YMM4

Persistence <-> FolderDocument
```

P1.6 deliberately freezes **ownership boundaries**, not concrete class names or a premature product rewrite.

### P1 exit gate

**SATISFIED / FROZEN.**

Both pinned hosts pass the integrated structural suite covering:

- Add — **green**;
- Delete — **green**;
- Move Up — **green**;
- Move Down — **green**;
- nested folders — **green**;
- boundary / owner cases — **green**;
- repeated edit sequences — **green**;
- Ctrl+Z / Ctrl+Y — **green**;
- post-history native interaction — **green**;
- native drag / real FileDrop after structural mutation — **green**;
- no Harmony — **green**.

The final integrated gate is P1.5c source `05237928242f6a182a74b7e63090512dd4693212`, run `35695949808`, **353/353 on both pinned hosts**.

P1 is therefore **COMPLETE / FROZEN**. P2 may add host interaction routes without reopening P1 unless new evidence invalidates a frozen assumption.

### Explicitly deferred from P1

- project-file persistence format;
- polished folder UI;
- broad third-party item compatibility;
- performance claims.

---

## P2 — Host Interaction Coverage

**Status: COMPLETE / FROZEN.**

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

**SATISFIED / FROZEN.** See `docs/YMM4_NO_HARMONY_P2_INTERACTION_MATRIX.md`.

A documented interaction matrix is green on both pinned hosts, with every supported route classified as:

- native-safe without adaptation;
- mapped through FolderLayout;
- intentionally unsupported with graceful behavior.

No harness-only `Reveal` call may be cited as proof of automatic host navigation.

### Deferred

Persistence schema and final visual design.

---

## P3 — Folder Data Model & Persistence

**Status: COMPLETE / FROZEN.**

Freeze record: `docs/YMM4_NO_HARMONY_P3_PERSISTENCE_FREEZE.md`.

**Purpose:** make folder state survive normal project lifecycle safely.

### Frozen storage boundary

- schema v1 persists only logical folder state: Timeline key, folder Id, Start/End, Name and collapsed state;
- display geometry / viewport / derived hierarchy are rebuilt, not persisted;
- project-owned `ToolState.SavedState` is the storage surface;
- public `Timeline.ID : Guid` is the per-Timeline key;
- public `ProjectFilePath` change notification is the accepted project-switch signal;
- new-project creation explicitly clears inherited folder state after the host produces a fresh Timeline.ID;
- malformed / unsupported / invalid SavedState is quarantined and preserved raw rather than silently overwritten;
- reloaded FolderDocument immediately re-enters the frozen P1 structural/Undo semantics.

### Exit gate

**SATISFIED / FROZEN.**

Evidence:

- P3.1 pure FolderDocument / codec — 29/29 PASS;
- P3.3 pure unreadable-state preservation — 40/40 PASS;
- P3.2 project A/B roundtrip — GREEN on 4.56.1.0 and 4.55.1.1;
- P3.4 new-project reset — GREEN on both pinned hosts;
- P3.6 reload -> structural Add/Undo/Redo — GREEN on both pinned hosts;
- consolidated secondary-host V2 — `PASS_P3_V2_SECONDARY`.

### Known graceful-degradation limitation

If the folder plugin is completely unavailable and the project is then re-saved, YMM4 does not preserve that absent plugin's ToolState entry. The YMM4 project remains usable, but folder metadata can be lost.

P3 deliberately does **not** add a sidecar / dual-store solely for this unavailable-plugin case. Recovery guidance belongs in P7 release documentation.

### Deferred

Product-facing folder controls, discoverability and interaction polish move to P4.

---

## P4 — Product UX & LayerPatan Convenience Parity

**Status: ACTIVE — minimum workflow / packaging GREEN; convenience Full reproduction is the active path.**

Work-policy authority:
- `docs/YMM4_NO_HARMONY_CONVENIENCE_DESIGN.md`

Reference feature baseline:
- LayerPatan commit `aa58e7e772deb829853742afc65e42036cf09d47`
- feature set is the reproduction target;
- LayerPatan internal topology / Harmony implementation is **not** the architecture target.

Current P4 evidence:

- P4.1 layer-label UI surface — 19/19 named assertions PASS on YMM4 4.56.1.0;
- P4.2 host-independent UX commands — 27/27 PASS;
- P4.3 minimum integrated workflow — 24/24 named assertions PASS on YMM4 4.56.1.0;
- P4.4 installable hands-on candidate — startup-smoked;
- P4.5 real `.ymme` install — GREEN on YMM4 4.56.1.0;
- S0 product integration reconciliation — dual-host GREEN on YMM4 4.56.1.0 / 4.55.1.1; freeze record: `docs/YMM4_NO_HARMONY_S0_INTEGRATION_RECONCILIATION.md`;
- S1 common commands / low-risk parity — pure 27/27 PASS + dual-host one-row composite GREEN; freeze record: `docs/YMM4_NO_HARMONY_S1_COMMON_COMMANDS_FREEZE.md`.

### Product direction

Reproduce LayerPatan's convenience feature set while preserving the frozen no-Harmony core.

Required principles:

- no Harmony;
- keep P0-P3 core semantics and ownership intact;
- prefer a small number of shared product boundaries over one service per feature;
- one runtime state authority;
- one edit/commit entry;
- one YMM4 host-access boundary;
- one FoldMap authority;
- no feature-specific Undo stack, persistence path, structural observer or reflection island;
- Tool panel is a projection, not a second folder model.

### Active implementation sequence

#### S0 — integration reconciliation — COMPLETE / FROZEN

Freeze record:
- `docs/YMM4_NO_HARMONY_S0_INTEGRATION_RECONCILIATION.md`

Completed:

- compared the installable candidate with the frozen P1/P2/P3 mechanisms;
- connected standard structural FolderRangeTracker + same-transaction Undo synchronization to the persisted FolderDocument;
- connected the proven folded input/native-drag and real FileDrop mapping routes;
- verified Add -> Undo -> Redo against the **product candidate**, not only the Lab probes;
- passed the S0 product-integration gate on both pinned hosts;
- recorded P2 automatic reveal as an intentional UX/history-policy boundary rather than silently persisting view-only reveal behavior;
- kept StructuralDeltaDetector as the classifier boundary for structural routes that actually require post-hoc delta classification, without creating a second structural owner beside the proven standard-command path.

S0 does not reopen the P0-P3 Core contracts.

#### S1 — common command surface + low-risk parity — COMPLETE / FROZEN

Freeze record:
- `docs/YMM4_NO_HARMONY_S1_COMMON_COMMANDS_FREEZE.md`

Completed:

- pinned LayerPatan creation-range behavior: selected-click -> selected min..max including gaps; outside-click -> clicked layer only;
- folder-head adjustment;
- one-layer create convenience with one native Add Layer + one user-visible Undo unit;
- double-click rename on the folder-name hit region;
- expand all / collapse all;
- select items in folder;
- shared typed `FolderCommands` ownership used by Timeline menu/tag and reserved for S4 Tool-panel/keyboard projection;
- standard command execution centralized through the product host-access boundary;
- pure policy 27/27 PASS;
- dual-host S1 composite integration GREEN;
- S0 / startup / real `.ymme` / P4 minimum workflow regressions GREEN.

S1 freezes command ownership and semantics. S4 still owns management-panel construction and panel-specific keyboard/discoverability UX.

#### S2 — extended project state + visual metadata — COMPLETE / FROZEN

Freeze record:
- `docs/YMM4_NO_HARMONY_S2_VISUAL_METADATA_FREEZE.md`

Completed:

- outer schema v2 over the frozen P3 FolderDocument v1 core;
- v1 migration and unknown/raw preservation;
- Folder Color / Hidden keyed by TimelineKey + FolderId without duplicated ranges;
- eight-color reference palette + default;
- explicit YMM4 layer-color application with native Undo/Redo;
- nested folder hide/show with exact original per-layer visibility restoration;
- external YMM4 eye overrides without immediate re-hide;
- native eye and folder visibility Undo/Redo convergence;
- structural remap of visibility-restore keys through the existing FolderRangeTracker mapping;
- real ToolArea SavedState v2 roundtrip;
- pure 29/29 PASS;
- dual-host S2 integration GREEN;
- S0 / S1 / startup / real .ymme regressions GREEN.

No S2-specific global preference was required, so no new SettingsBase state was introduced.

#### S3 — structural convenience + Group Control — COMPLETE / FROZEN

Freeze record:
- `docs/YMM4_NO_HARMONY_S3_STRUCTURAL_GROUP_FREEZE.md`

Completed:

- host-independent structural convenience / Group rules;
- add layer at folder end;
- add layer below a layer while keeping it inside the folder;
- target + ancestor expansion with unrelated ranges shifted through FolderRangeTracker;
- destructive folder+contained-layer/item delete;
- add Group Control at folder head;
- GroupRange issue detection / warning count;
- Fit GroupRange;
- standard Add/Delete GroupRange auto-correction in the existing P1 host-owned history boundary;
- plugin-owned structural composites through public Timeline.AddLayer/DeleteLayer and one final native Record();
- Hidden/visibility-restore composition for newly inserted rows;
- pure **33/33 PASS**;
- dual-host S3 integration GREEN on 4.56.1.0 / 4.55.1.1;
- S0 / S1 / S2 / startup / real .ymme regressions GREEN.

Host mutation remains owned by the no-Harmony structural/Undo boundary. No second structural observer, persistence store, Undo stack or FoldMap was introduced.

#### S4 — management panel parity

- folder/layer list projection;
- inline rename / F2 / Ctrl+G / Delete;
- expand/collapse all;
- Follow Timeline;
- current-item-at-playhead display;
- Group warning lanes;
- Before / Into / After block drag/drop.

The panel must not become a second structural engine or persistence model.

#### S5 — visual parity requiring redesign

- collapsed-owner-row inner timing summary;
- folded Group/background visual compatibility;
- final Timeline visual polish.

Do not reproduce LayerPatan's native item Top/Height Harmony patch. Use plugin-owned overlay/drawing where viable.

### Exit gate

P4 completes only when:

- the LayerPatan reference feature set is either reproduced or explicitly documented as intentionally different with an equivalent user outcome;
- the frozen no-Harmony core remains authoritative;
- Tool panel and Timeline UI call the same command/state boundaries;
- no Harmony dependency is introduced;
- host-sensitive new mechanisms have narrow Lab evidence;
- hands-on confirms the resulting workflow is practical.

Styling differences are acceptable only when they do not remove reference functionality.

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
  -> P1.3 operation observe   DONE
  -> P1.4 Undo synchronization DONE
  -> P1.5 Track C integration DONE
  -> P1.6 architecture convergence DONE
  -> P1 freeze                DONE
  -> P2 interaction coverage  DONE
  -> P3 persistence           DONE
  -> P4.1 UI surface           DONE (V1 primary)
  -> P4.2 UX command policy    DONE
  -> P4.3 minimum workflow     DONE (V1 primary)
  -> P4.4 installable candidate DONE
  -> P4.5 real .ymme install    DONE
  -> S0 integration reconcile  DONE
  -> S1 common commands / low-risk parity DONE
  -> S2 color / visibility / persistence extension DONE
  -> S3 structural convenience / Group Control DONE
  -> S4 management panel parity NEXT
  -> S5 visual parity redesign
  -> P4 freeze
  -> P5 compatibility
  -> P6 hardening
  -> P7 release
```

P0-P3 are frozen. If later phases expose evidence that contradicts a frozen assumption, reopen only the affected gate with a new isolated proof rather than broadly rewriting earlier phases.

# 6. Scope-control rules

To avoid turning the Full exploration into an unbounded rewrite:

- LayerPatan reference convenience features are now part of P4 Full scope; do not reclassify them as optional merely to shorten implementation.
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
