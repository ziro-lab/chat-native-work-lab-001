# YMM4 no-Harmony Full — P6 Hardening & Performance Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

P6の目的は、P4/P5で凍結した no-Harmony 製品経路を、大きめのTimeline、反復Undo/Redo、viewport変化、Project切替、失敗/回復条件へ置いたときに、状態ドリフト・stale geometry・runaway refresh・subscription leak・history corruptionが発生しないことを確認することだった。

P6はベンチマークではない。ここで記録するApplications / Mutations / CanvasRefreshes等はrunaway検出用の診断値であり、速度・性能保証値ではない。

## 1. Accepted sources

### Product runtime source

P6で見つかった唯一の製品runtime修正:

`4d9fae9a8b96d069d6d2b6923bc33f00035967eb`

PR:

- #124 `p6: prune stale virtualized display subscriptions`

P6 test-only harness boundary:

`b454df1cc8e6dbc93a8baaafbbbaa2beb37961af`

PR:

- #122 `p6: add conditional hardening harness hook`

Normal candidate/package builds do **not** compile the P6 stress sources. They are linked only when `EnableP6Hardening=true`.

### Final P6 evidence source

`ea0feed7b06c7b9fa172c62163fe41486654e54b`

Branch:

`work/ymm4-no-harmony-p6-hardening`

PR:

- #121

Pinned exact hosts:

- YMM4 4.55.1.1 Lite
- YMM4 4.56.1.0 Lite

No Harmony reference or assembly was introduced.

## 2. P6 finding — stale virtualized VM subscriptions

Initial density/viewport stress exposed deterministic subscription growth rather than a one-time host realization effect.

Observed repeated identical viewport sweeps grew the DirectDisplay subscription set:

```text
679
 -> 703
 -> 727
```

That is **+24 per repeated identical sweep**.

Root cause:

- `DirectDisplay.RefreshSlots()` subscribed to each item/row VM observed in the current YMM4 collections;
- YMM4 virtualization can replace those VMs as the viewport moves;
- stale VMs were removed from host collections but remained in `slots` / `watched` until controller Dispose.

Accepted correction in PR #124:

- keep root `TimelineViewModel`, `Viewport`, and YMM Settings subscriptions permanent;
- build the current item/label/line VM identity set on each `RefreshSlots()`;
- for stale slots:
  - restore native logical Top/Height before release;
  - remove the slot;
  - detach `PropertyChanged` when it is not one of the permanent root subscriptions;
- keep the existing FoldMap, host mutation, Undo, input, gesture, and Dispose ownership unchanged.

No second observer, state model, timer, Undo path, or Harmony mechanism was added.

### Regression after the runtime fix

PR #124 exact-host regressions:

- integrated Track C run `35857603479` — PASS on both hosts;
- Track C structural run `35857603476` — PASS on both hosts;
- S5 integration run `35857603573` — PASS on both hosts.

Representative detach evidence still ends with:

`subscriptions=0`

on both hosts.

## 3. P6.1 / P6.3 — density, nesting, viewport and idle soak

P6.3 viewport/visual soak is intentionally integrated into P6.1 rather than duplicated as another equivalent workflow.

Canonical final run:

`35861609755`

Both hosts passed the same final profile:

- logical layers = **128**;
- items = **384**;
- folders = **24**;
- maximum nesting depth = **8**;
- collapse/expand cycles = **12**;
- collapsed hidden layers = **95**;
- collapsed timing bands = **285**;
- three identical viewport passes:
  - warmup 1;
  - warmup 2;
  - measured;
- each viewport pass exercises **12** zoom/scroll/resize cycles;
- final geometry exact;
- FolderDocument validation PASS;
- display reentries = **0**;
- display failure = **none**.

After the subscription correction:

```text
subscriptions_after_warmup1 = 655
subscriptions_settled       = 655
subscriptions_final         = 655
measured growth             = 0
```

### Bounded idle soak

After all density/fold/viewport work settles, the same runtime remains untouched for **8 seconds**.

Both hosts:

- idle Applications delta = **0**;
- idle Mutations delta = **0**;
- idle CanvasRefreshes delta = **0**;
- idle subscription growth = **0**;
- geometry remains valid.

This specifically checks that the regular HandsOnRuntime polling and normal host idle activity do not create a continuous DirectDisplay refresh/subscription loop.

## 4. P6.2 — repeated structural/history soak

Canonical final run:

`35861609755`

Both hosts passed:

- cycles = **4**;
- product/host operations = **24**;
- Undo/Redo transitions = **72**.

Repeated operation families:

- standard Add Layer;
- standard Delete Layer;
- standard Move Down;
- plugin-owned add-inside;
- folder Hidden;
- explicit panel block move.

For every operation:

```text
baseline
 -> operation
 -> exact post-state
 -> Undo  -> exact baseline
 -> Redo  -> exact post-state
 -> Undo  -> exact baseline
```

Snapshot coverage includes:

- canonical FolderProductState serialization;
- test item Type / Layer / Frame / Length;
- GroupRange;
- Timeline / LayerSettings bounds;
- representative layer visibility.

Final result:

- exact snapshot restore = true;
- FolderProductState validation = true;
- display reentries = 0;
- display failure = none.

## 5. P6.4 — project/session lifecycle soak

Canonical final run:

`35861609755`

Both hosts passed:

- Project transitions = **4**;
- unique Timeline IDs = **5**;
- disposed old controllers = **4/4**;
- old DirectDisplay subscription count after Dispose = **0/4 retained, 4/4 zero**;
- new unsaved projects with inherited folder state = **0**;
- stale visibility callbacks mutating the new project = **0**;
- old controller lookup after replacement = cleared.

The gate also mutates the old Timeline after its controller has been disposed and verifies that current shared FolderProductState remains unchanged.

This confirms the accepted lifecycle boundary:

> old session/controller callbacks cannot write into a newly active project after detach.

## 6. P6.5 — bounded failure and recovery

Canonical final run:

`35861609755`

Both hosts passed the same recovery contract:

- unsupported/newer raw saved state locks editing;
- exact unreadable raw payload is preserved;
- structural edit while locked is rejected;
- rejected locked edit does not mutate native Timeline structure;
- explicit reset recovers editing;
- invalid plugin-operation precondition is rejected;
- rejected precondition leaves product/native state unchanged;
- rejected precondition creates no extra history boundary;
- the preceding valid rename still Undo/Redo's correctly;
- display reentries = 0;
- display failure = none.

P6 does not convert recoverable invalid input into silent state replacement.

## 7. P6.6 — final dual-host hardening + normal package

Final workflow:

`35861609755`

Final evidence source:

`ea0feed7b06c7b9fa172c62163fe41486654e54b`

Sequence on **each** pinned host:

1. build instrumented candidate with `EnableP6Hardening=true`;
2. P6.1 density/viewport/idle;
3. P6.2 history;
4. P6.4 lifecycle;
5. P6.5 failure/recovery;
6. validate all final contract markers;
7. remove the instrumented candidate;
8. build the normal candidate with no P6 stress sources;
9. create real `.ymme`;
10. install the real `.ymme` through YMM4;
11. verify installed DLL identity;
12. startup/attach;
13. verify no Harmony files.

Both jobs:

- `PASS_P6_FINAL_HARDENING`;
- `PASS_P6_FINAL_PACKAGE`;
- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`.

### YMM4 4.56.1.0 Lite

Hardening DLL SHA256:

`4a0e65dfd6a5b89b696772866eaf19e66ba702381bec95a78f05096e681a9ded`

Normal release-form DLL SHA256:

`21e5ecdf37e4dad20b4675cd9b1f603d0ef8712da94d0140dac7ba3155738643`

Installed DLL SHA256:

`21e5ecdf37e4dad20b4675cd9b1f603d0ef8712da94d0140dac7ba3155738643`

Real .ymme SHA256:

`b34f3430a0f76f3f23fab4e3680cb4fbd69fd7be339934507bd038ce54dd8ad6`

Installed .ymme SHA256:

`b34f3430a0f76f3f23fab4e3680cb4fbd69fd7be339934507bd038ce54dd8ad6`

Artifacts:

- package: `10751185477`
  - `sha256:347a175fe3ec012a985909ad467e9b443fa13ec14432dddd037d209482739244`;
- evidence: `10751180483`
  - `sha256:747333bc2cc6fe561fa509848d6829db896dc14b719f6306943d48790a5a94cd`.

### YMM4 4.55.1.1 Lite

Hardening DLL SHA256:

`7d73a26a66190c1f2c0821cdf2d0f53e5068b9aa7ea128c8c7941665711b2fd6`

Normal release-form DLL SHA256:

`1bff233fc278396681f7862c6c2385e9b7f69133f769c42c235f8912ae445d39`

Installed DLL SHA256:

`1bff233fc278396681f7862c6c2385e9b7f69133f769c42c235f8912ae445d39`

Real .ymme SHA256:

`6f765594e573cb42cc7868681993cee76558b890e17c323281c71da18f5759a6`

Installed .ymme SHA256:

`6f765594e573cb42cc7868681993cee76558b890e17c323281c71da18f5759a6`

Artifacts:

- package: `10750632626`
  - `sha256:0fd69b557b362c7f7d738af8bf92169fe235573ffc5c15c6d3fe115a5bc20889`;
- evidence: `10750227697`
  - `sha256:93279ccf75dff23fbf56972f1d464f06f05473988d2652853e58b33328d7fa8a`.

## 8. Architecture result

P6 did **not** require:

- another FolderDocument;
- another FoldMap;
- another persistence path;
- a custom Undo stack;
- another structural observer;
- a resident stress worker;
- a periodic display refresh timer;
- Harmony.

The only product hardening change was stale virtualized VM cleanup inside the already-authoritative DirectDisplay observer.

P6 stress code remains conditional test instrumentation and is not compiled into normal release-form builds.

## 9. Explicit P7 boundary

P6 validates normal package install/startup on both pinned hosts, but each matrix job built its own release-form DLL/.ymme against that host.

Therefore P6 does **not** yet make the P7 release-artifact identity claim:

> one single canonical .ymme binary, with one SHA256, is installed and tested unchanged on both supported YMM4 hosts.

P7 must create one canonical release artifact and use that exact package/dll identity for the final both-host acceptance.

P7 also owns:

- final version/name metadata;
- release README;
- install/uninstall instructions;
- compatibility statement;
- persistence/schema statement;
- unavailable-plugin recovery warning;
- known limitations;
- final package contents;
- disable/uninstall behavior where practical;
- canonical artifact identity.

## 10. Exit decision

P6 Hardening & Performance is **COMPLETE / FROZEN**.

P6 exit conditions are satisfied for the defined bounded soak scenarios:

- no folder-state drift;
- no history corruption;
- no stale folded geometry;
- no runaway refresh/reentry;
- no steady-state subscription growth;
- disposed controllers retain zero DirectDisplay subscriptions;
- no old-session state writes;
- failure/recovery remains safe;
- normal .ymme install/startup remains green on both pinned hosts;
- no Harmony.

The active completion path advances to:

**P7 — Release Gate.**
