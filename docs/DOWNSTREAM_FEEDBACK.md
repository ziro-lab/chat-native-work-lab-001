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

## Shared-helper threshold

The YMM4 lab now has several independent workflows that repeat host download, hash verification and launch setup. This is enough evidence to consider a **small** shared helper for those repeated mechanics when the next experiment needs it.

Do not turn the lab into a general framework. Only extract mechanics that are already repeated and stable; keep experiment assertions and product-specific behavior local to each experiment.
