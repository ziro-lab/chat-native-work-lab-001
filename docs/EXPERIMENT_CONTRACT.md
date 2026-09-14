# Experiment Contract

Each experiment must be small enough that its result can be interpreted without guessing.

## Required sections

### Goal
One falsifiable claim.

### Environment
Record runner OS/image, host version, runtime/toolchain versions, and exact downloads/hashes when practical.

### Assertions
List machine-checkable conditions that must pass.

### PASS means
State exactly what the experiment proves.

### NOT PROVEN
State nearby claims that are intentionally outside the experiment.

### Reproduction
Document the workflow/manual-dispatch path and any required inputs.

### Evidence
Record source commit, workflow run, artifact identity, digest/hash, assertion output, and relevant screenshots/logs. For pull-request runs, distinguish the source HEAD from a synthetic merge checkout when they differ; see `EVIDENCE_POLICY.md`.

## Optional downstream outcome

When a result later informs a real product or another experiment, record that separately without changing the original PASS boundary.

Useful fields are:

```text
Downstream outcome
- adopted / rejected / superseded
- product or experiment decision informed
- whether later integration contradicted the original result
```

Downstream adoption is feedback, not substitute evidence for product acceptance.

## Preferred proof ladder

For native host integrations, grow evidence in this order when useful:

```text
P0 host launches / plugin loads
P1 host state can be read
P2 target object/state can be identified
P3 smallest deterministic mutation succeeds
P4 real product UI path reaches the same behavior
P5 failure is atomic / safe
P6 native undo/recovery works
P7 distributable package is produced
P8 release artifact identity is verified
```

Do not claim later stages from earlier-stage evidence.

For P5 mutation-safety checks, prefer an explicit before/after invariant when practical:

```text
capture semantic state/signature
→ intentionally trigger rejection/failure
→ assert rejection occurred
→ assert semantic state/signature is unchanged
```

This is stronger than checking only an error message because it detects partial native-host mutation.

A lab experiment proves only its stated boundary. Full product regression, lifecycle behavior, packaging, real user assets and physical input/installer paths still require their own acceptance when relevant.
