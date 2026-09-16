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

## YMM4 host-observation rules

- This repository is the canonical `ziro-lab` location for undocumented/version-sensitive YMM4 host behavior used by downstream plugins.
- Keep YMM4 behavior discovery here rather than in product/Garage repositories; product-side tests may verify integration but should not become the only source of the host claim.
- Label evidence as **static inspection**, **native automated observation**, or **manual live observation**. Do not substitute one for another.
- Static IL/reflection evidence can establish inspected code structure but does not prove interactive rendering/audio stability.
- Manual observations may establish perceptual/UI behavior but do not silently become automated PASS assertions.
- State the exact YMM4 version for every undocumented host behavior. Revalidate when a newer version materially touches the subsystem.
- Follow [`docs/YMM4_OBSERVATION_POLICY.md`](docs/YMM4_OBSERVATION_POLICY.md) for YMM4 records.

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
