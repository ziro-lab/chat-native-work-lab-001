# Evidence Policy

This lab treats evidence as a chain, not a screenshot or a green badge.

## Minimum evidence chain

For a direct branch/push run:

```text
source commit SHA
→ workflow run ID
→ runner / exact host identity
→ assertion output
→ artifact ID
→ artifact digest/hash
```

For pull-request workflows, record the source branch identity separately from the commit/tree actually tested when GitHub checks out a synthetic merge commit:

```text
source HEAD
→ checkout commit / checkout tree
→ workflow run ID
→ runner / exact host identity
→ assertion output
→ artifact ID
→ artifact digest/hash
```

Do not describe a GitHub-generated PR merge commit as though it were the source branch HEAD.

When a compiled artifact is part of the claim, preserve its SHA256. When the host loads a specific binary, record or assert the loaded binary identity when practical.

## Artifact rules

- Do not upload downloaded third-party host distributions as experiment artifacts by default.
- Artifacts should contain only original probe binaries/scripts and evidence needed to reproduce or audit the result.
- Keep evidence compact and text-first where possible.

## Result language

Use:

- `PASS` — all stated assertions passed.
- `FAIL` — an assertion or required experiment condition failed after reaching the relevant assertion boundary.
- `BLOCKED` — the experiment could not reach the assertion boundary, including runner, host-download or external distribution failures.

A workflow job that merely uploaded diagnostics successfully is not a product/experiment PASS.

If a run is BLOCKED by infrastructure before any relevant source behavior is exercised, a same-source retry is valid. Record that the successful retry used the same source identity; do not silently reinterpret the blocked run as a failed product assertion.

## Human verification

If a behavior was inferred through internal API state rather than a literal mouse/keyboard action, state that boundary. Do not turn a programmatic Undo call into a claim that physical Ctrl+Z key delivery was tested, for example.

## Downstream product evidence

A product may later adopt an experiment result and pass a larger integrated acceptance suite. That is useful downstream feedback, but it does not expand the original experiment's PASS boundary unless the additional evidence is itself brought into this lab under an auditable experiment contract.

## Public safety

Evidence must not contain credentials, private project paths, user data, paid assets, or redistributable copies of third-party applications unless intentionally allowed and documented.
