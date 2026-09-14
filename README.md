# Chat Native Work Lab

Public experiment lab for testing how far chat-driven development can go when GitHub Actions is used as a target-native validation environment.

The main question is not merely whether code builds. The lab asks whether a chat-driven workflow can:

```text
Chat / agent
→ repository change
→ GitHub Actions
→ exact native environment
→ real host application
→ observable assertion
→ evidence + artifact
→ reproducible result
```

## What this repository is for

Use this repository for small, isolated experiments that answer concrete questions about native execution and host integration.

Examples:

- Can a plugin be loaded by the real host application on a hosted Windows runner?
- Can the host expose the state needed for deterministic assertions?
- Can a UI path be exercised without relying only on screenshots?
- Can a distributable artifact be proven to be the same binary that was loaded by the host?
- Can a workflow remain reproducible after host/application updates?

This repository is an **experiment lab**, not a general workflow framework, CI marketplace, or product monorepo.

## Experiment contract

Every experiment should define:

1. **Goal** — one concrete claim being tested.
2. **Environment** — exact OS/runner, host version, dependencies and pinned hashes when practical.
3. **Assertions** — machine-checkable conditions for PASS.
4. **PASS boundary** — what success proves.
5. **NOT PROVEN** — nearby claims that the experiment does not prove.
6. **Reproduction** — how to run it again.
7. **Evidence** — source identity, actual tested checkout when relevant, workflow run, artifact identity and hashes.

See [`docs/EXPERIMENT_CONTRACT.md`](docs/EXPERIMENT_CONTRACT.md) and [`docs/EVIDENCE_POLICY.md`](docs/EVIDENCE_POLICY.md).

## YMM4 experiment set

The first seed was a minimal YukkuriMovieMaker4 (YMM4) host-validation proof:

```text
GitHub Actions Windows runner
→ download exact YMM4 4.55.1.1 Lite from the official release
→ verify SHA256
→ build a minimal .NET 10 plugin
→ install it into the temporary YMM4 instance
→ launch real YMM4
→ require a real plugin callback marker
→ preserve evidence without redistributing YMM4 itself
```

The lab has since grown into several independent YMM4 experiments covering plugin load, Timeline selection context, playhead placement, Character-relative Layer behavior and ItemTemplate identity/restart ambiguity.

See [`experiments/ymm4/`](experiments/ymm4/) for the current list.

These experiments later informed a larger downstream YMM4 Template Placer implementation. The product re-ran integrated native acceptance rather than treating lab PASS results as product acceptance. The lessons fed back into this repository are recorded in [`docs/DOWNSTREAM_FEEDBACK.md`](docs/DOWNSTREAM_FEEDBACK.md).

## Repository layout

```text
.github/workflows/        experiment runners
docs/                     lab-wide contracts and evidence rules
experiments/               isolated target-specific experiments
  ymm4/                    YMM4 experiments
```

Shared helpers should only be introduced after the same mechanism has been repeated and proven across multiple experiments. The current YMM4 set is large enough to consider a small shared helper for repeated host download/hash/launch mechanics when a future experiment needs it, but experiment assertions should remain local.

## Public-repository rules

- Prefer synthetic or redistribution-safe fixtures.
- Do not commit credentials, personal projects, private assets, paid assets, or large third-party binaries.
- Fetch third-party host applications from their official distribution source during the workflow when appropriate.
- Pin exact versions/hashes when practical.
- Do not interpret a green workflow as sufficient evidence by itself; assertions and artifacts must support the stated claim.
- Record failure boundaries and unproven claims explicitly.
- Treat infrastructure failures before the assertion boundary as BLOCKED rather than silently turning them into product failures.

## License

Unless otherwise noted, original code, workflows, scripts, documentation, and synthetic fixtures in this repository are licensed under the Apache License 2.0. See [`LICENSE`](LICENSE).

Third-party applications, binaries, trademarks, screenshots, names and other third-party materials retain their original rights and are **not** relicensed under Apache-2.0 by this repository. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## Status

**v0.2 / downstream-validated lab process** — experiment contract plus five independent YMM4 native-host experiments, with product-integration feedback incorporated into the evidence and experiment policies.
