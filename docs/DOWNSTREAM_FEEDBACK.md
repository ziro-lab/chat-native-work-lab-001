# Downstream product feedback

This document records lessons learned after the public native-host experiments were used to guide a larger downstream YMM4 plugin implementation.

It is **not** additional experiment evidence. Product-side integration results live outside this public lab and do not expand the PASS boundary of any experiment here.

## 2026-09 YMM4 Template Placer feedback

The following public experiments informed concrete downstream implementation choices:

- [`timeline-selection-context`](../experiments/ymm4/timeline-selection-context/) — used to drive temporary Character-palette context from public Timeline selection state rather than polling or product-side private Timeline ViewModel reflection.
- [`playhead-quick-drop`](../experiments/ymm4/playhead-quick-drop/) — used for playhead Quick Drop through public `Timeline.CurrentFrame`, independent Template cloning and intrinsic Template length.
- [`character-layer-placement`](../experiments/ymm4/character-layer-placement/) — used for deterministic Front/Back planning, same-Character ordering baselines and full-span collision checks.
- [`template-identity`](../experiments/ymm4/template-identity/) — prevented `SceneId` from being treated as a unique Template identity and led to strict source-locator resolution with explicit missing/ambiguous states.

The downstream product later re-tested these choices inside its own integrated native acceptance. That product acceptance is separate from this lab: a small experiment can reduce uncertainty, but it does not substitute for full product regression, UI lifecycle, packaging or user acceptance.

## Lessons that should feed back into the lab

### 1. Record downstream outcome without changing the original claim

When an experiment materially informs a product design, it is useful to record that outcome. The outcome should be described as downstream adoption or feedback, not as stronger proof of the original experiment.

A useful optional section is:

```text
Downstream outcome
- adopted / rejected / superseded
- what product decision it informed
- whether later product testing contradicted the experiment
```

### 2. Distinguish source HEAD from the actual PR test checkout

Pull-request workflows may test a synthetic merge commit rather than the branch HEAD itself. Evidence should distinguish at least:

```text
source_head
→ checkout_commit / checkout_tree
→ workflow run
→ host / assertions
→ artifact / digest
```

This avoids treating a GitHub-generated PR merge commit as though it were the source branch commit.

### 3. Pre-assertion infrastructure failures are BLOCKED, not product FAIL

A host download, runner or external release endpoint may fail before the native assertion boundary is reached. Such a run should be recorded as `BLOCKED` (or otherwise explicitly infrastructure-only), and a same-source retry is valid when no source change is required.

A diagnostics upload succeeding after an infrastructure failure is not a PASS.

### 4. P5 failure-safety proofs benefit from state signatures

For mutation experiments, a practical atomicity pattern is:

```text
capture semantic state/signature
→ intentionally trigger rejection/failure
→ assert rejection occurred
→ assert state/signature is unchanged
```

This is especially useful for proving that invalid input, collision or stale-plan rejection does not leave partial native-host mutations behind.

### 5. Lab proof and product acceptance remain different layers

A narrowly scoped lab experiment can be completely correct while a product still contains an unrelated lifecycle or integration defect. The experiment's `NOT PROVEN` section is therefore a feature, not a disclaimer to remove.

The downstream YMM4 work reinforced the preferred workflow:

```text
small public experiment
→ product design decision
→ product implementation
→ integrated native product acceptance
→ human/user acceptance where needed
```

### 6. Functional acceptance and UI/UX acceptance should be separate claims

The downstream v0.4.2 work reached a point where the intended features existed and were functionally correct, but the UI still exposed too much of the implementation taxonomy and relation-engine parameter model.

That made one lesson explicit:

> a feature being present and executable is not proof that the user-facing workflow matches the user's editing model.

For UI-heavy experiments or product proofs, define a separate UI/UX acceptance layer when appropriate. Examples of claims worth checking independently are:

- selecting an Item reveals only meaningful actions for that context;
- intent, reusable Set and concrete action are distinct interaction levels;
- a high-frequency action is one direct action after setup, without asking again for lower-level engine choices;
- navigation and Set switching are zero-mutation operations;
- an empty state leads to the correct recovery action instead of only explaining that nothing is available;
- settings use progressive disclosure so irrelevant parameters are actually hidden for the current choice.

Do not infer those properties merely because the underlying commands and settings model exist.

### 7. `IsVisible` is weaker evidence than physical layout containment

The downstream UI proof initially checked WPF visibility state and captured screenshots. Manual review of the resulting evidence showed that a control can be logically visible while a test-sized host surface still makes the lower edge look clipped or ambiguous.

For narrow-window acceptance, a stronger pattern is:

```text
assert semantic visibility/state
→ assert ActualWidth/ActualHeight are non-zero
→ translate the important control bounds into the tested surface
→ assert the complete bounds remain inside that surface
→ capture a screenshot for human audit
```

A screenshot is useful corroborating evidence, but it should not be the only machine-checkable UI claim. Likewise, `IsVisible == true` alone should not be treated as proof that the action is physically discoverable in the tested viewport.

### 8. Packaging / installation behavior is a separate acceptance boundary

The downstream plugin exposed an installation failure mode that a normal DLL load test would not catch.

A versioned outer package filename does not by itself define a safe upgrade path. For the tested YMM4 `.ymme` behavior, the archive's top-level plugin directory affected the installation directory. A package layout that effectively created version-named plugin directories could leave old and new Candidate versions installed side by side instead of upgrading one stable plugin location.

The downstream fix used a stable internal install root independent of the outer package version, for example:

```text
PluginName-v0.4.2.ymme
└─ PluginName/
   ├─ PluginName.dll
   └─ ...
```

The package proof then checked the archive paths themselves, not only file existence:

- every allowlisted payload file is below the one stable internal plugin root;
- no plugin DLL exists at a flat archive root;
- no version-named internal plugin root is accepted;
- the DLL archived in the installer package has the same hash as the distribution DLL that passed the native host smoke test;
- upgrade cleanup instructions are explicit if an earlier Candidate used a bad install layout.

The general lesson is:

> "the plugin binary loads" and "the package installs/upgrades into the intended location" are different claims and need different evidence.

### 9. Acceptance evidence should not certify itself

The downstream release lane generated acceptance manifests, but final packaging did not trust the producer's own `PASS` flag alone. A separate consumer validated the manifest identity, version, required stage set, unique requirement IDs and failure markers.

Negative fixtures deliberately removed or corrupted required evidence and had to be rejected. Useful corruption cases include:

- missing required stage;
- duplicate stage or requirement ID;
- old manifest version;
- explicit failed requirement;
- missing requirement;
- weakened required-stage list;
- wrong host identity;
- failure text present in the native log.

For high-value release evidence, prefer:

```text
producer creates evidence
→ independent consumer validates structure + semantics
→ negative tests prove the consumer rejects weakened evidence
```

rather than allowing the producer to define and approve its own success contract.

### 10. Keep claim layers explicit

The downstream work is easier to reason about when these claims stay separate:

```text
native host/API behavior
→ product functional integration
→ product UI/UX behavior
→ package/install/upgrade behavior
→ real-user acceptance
```

Passing one layer should never silently imply the later layers. This separation also makes regressions easier to localize: a package-layout defect does not invalidate a proven placement algorithm, and a UI mental-model defect does not mean the native host API experiment was wrong.

## Shared-helper threshold

The YMM4 lab now has several independent workflows that repeat host download, hash verification and launch setup. This is enough evidence to consider a **small** shared helper for those repeated mechanics when the next experiment needs it.

Do not turn the lab into a general framework. Only extract mechanics that are already repeated and stable; keep experiment assertions and product-specific behavior local to each experiment.
