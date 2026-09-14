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
Record source commit, workflow run, artifact identity, digest/hash, assertion output, and relevant screenshots/logs.

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
