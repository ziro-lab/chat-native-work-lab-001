# Evidence Policy

This lab treats evidence as a chain, not a screenshot or a green badge.

## Minimum evidence chain

```text
source commit SHA
→ workflow run ID
→ runner / exact host identity
→ assertion output
→ artifact ID
→ artifact digest/hash
```

When a compiled artifact is part of the claim, preserve its SHA256. When the host loads a specific binary, record or assert the loaded binary identity when practical.

## Artifact rules

- Do not upload downloaded third-party host distributions as experiment artifacts by default.
- Artifacts should contain only original probe binaries/scripts and evidence needed to reproduce or audit the result.
- Keep evidence compact and text-first where possible.

## Result language

Use:

- `PASS` — all stated assertions passed.
- `FAIL` — an assertion or required setup step failed.
- `BLOCKED` — the experiment could not reach the assertion boundary.

A workflow job that merely uploaded diagnostics successfully is not a product/experiment PASS.

## Human verification

If a behavior was inferred through internal API state rather than a literal mouse/keyboard action, state that boundary. Do not turn a programmatic Undo call into a claim that physical Ctrl+Z key delivery was tested, for example.

## Public safety

Evidence must not contain credentials, private project paths, user data, paid assets, or redistributable copies of third-party applications unless intentionally allowed and documented.
