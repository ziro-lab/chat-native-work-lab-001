# YMM4 Observation Policy

This lab is the canonical place in `ziro-lab` for recording **observed YukkuriMovieMaker4 (YMM4) host behavior** that downstream plugins may depend on.

The goal is to keep product repositories from accumulating ad-hoc reverse-engineering notes and to make version-sensitive behavior reproducible.

## Separation of responsibilities

```text
chat-native-work-lab-001
  observe / probe / reproduce YMM4 behavior
              ↓ evidence
product or plugin repository
  consume the smallest verified behavior needed for implementation
```

For small plugin prototypes, the current downstream Garage is:

- [`ziro-lab/ymm4-plugin-garage`](https://github.com/ziro-lab/ymm4-plugin-garage)

A downstream plugin may add its own regression test, but YMM4 host-behavior discovery belongs here first.

## Evidence types

A record must say which kind of evidence supports each claim.

### Static inspection

Examples:

- reflected members;
- IL/disassembly;
- command binding structure;
- property setter implementation.

Static inspection can prove that a code path or clamp is present/absent in the inspected build. It does **not** by itself prove that the live UI reaches that path or that real playback/rendering remains stable.

### Native automated observation

A GitHub Actions Windows runner (or equivalent reproducible native runner) launches or exercises the exact YMM4 build and produces machine-checkable assertions/artifacts.

This is preferred when the behavior can be automated safely.

### Manual live observation

A human verifies behavior in a real interactive YMM4 session.

Manual observations are useful for UI, perceptual smoothness, sound, timing and behaviors that are hard to automate, but they must be labeled as manual and should not silently expand an automated PASS boundary.

## Required record fields

Each YMM4 observation/experiment should contain:

1. **Question** — the narrow behavior being checked.
2. **Observed on** — exact YMM4 version and date.
3. **Host identity** — official asset/source and SHA256 when practical.
4. **Evidence type** — static / native automated / manual live, or a combination.
5. **Observation / assertions** — what was actually seen or measured.
6. **PASS boundary** — what the evidence proves.
7. **NOT PROVEN** — nearby claims that remain untested.
8. **Reproduction** — workflow, command, probe or manual steps.
9. **Evidence identity** — source commit, workflow run, artifact and digest when available.
10. **Downstream impact** — which plugin/product decision relies on the result.

## Version drift

Do not write undocumented YMM4 behavior as timeless fact.

Prefer wording such as:

> Observed on YMM4 v4.56.1.0.

When a newer YMM4 version materially touches the same subsystem, either re-run the experiment or mark the old result as not yet revalidated.

## Public repository boundary

- Do not commit YMM4 binaries unless redistribution is explicitly permitted.
- Fetch official public releases during workflows when needed.
- Preserve hashes and original probe output instead of redistributing the host.
- Do not publish private projects, user assets, credentials, local paths or unrelated application data.

## Downstream rule

A product README may summarize a Lab result, but the detailed host claim should point back to this repository.

If a plugin needs a new undocumented YMM4 behavior, add or extend a Lab experiment before introducing a broad patch based only on inference.
