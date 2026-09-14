# AGENTS.md

Keep this repository a small public experiment lab.

## Rules

- One experiment should answer one concrete native-host question.
- Prefer exact host versions and hashes.
- Prefer machine-checkable assertions over screenshots.
- Always state PASS and NOT PROVEN separately.
- Do not commit third-party binaries unless redistribution is explicitly intended and permitted.
- Use synthetic or redistribution-safe fixtures.
- Never commit credentials, private project files, personal paths or secrets.
- Do not build a shared framework until the same helper has proved useful in multiple independent experiments.
- Experiment-specific code stays under `experiments/`.
- Shared lab policy belongs in `docs/`.
- A green GitHub Actions run is evidence only when the workflow actually executes the stated host assertions.
- For pull-request runs, distinguish the source HEAD from the actual checkout commit/tree when GitHub tests a synthetic merge commit.
- Treat runner/download/external-host failures before the assertion boundary as BLOCKED; a same-source retry is allowed when no source change is needed.
- Downstream product adoption may be recorded as feedback, but it never widens an experiment's original PASS boundary by itself.

## Evidence minimum

Record enough information to connect:

```text
source HEAD / source commit
→ actual tested checkout commit/tree when different
→ workflow run
→ exact host/version
→ assertions
→ artifact
→ digest/hash
```

When a result is only partially automated, document the remaining human verification explicitly.

For mutation rejection/safety proofs, prefer checking that a semantic state/signature is unchanged after the expected failure rather than checking only the error text.
