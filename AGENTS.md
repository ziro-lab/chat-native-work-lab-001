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

## Evidence minimum

Record enough information to connect:

```text
source commit
→ workflow run
→ exact host/version
→ assertions
→ artifact
→ digest/hash
```

When a result is only partially automated, document the remaining human verification explicitly.
