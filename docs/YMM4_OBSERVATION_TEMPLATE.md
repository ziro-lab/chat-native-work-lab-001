# YMM4 Observation Template

Copy this into a new `experiments/ymm4/<slug>/README.md` and remove sections that do not apply.

```md
# <experiment name>

## Question

<one narrow YMM4 behavior to verify>

## Status

- Date:
- YMM4 version:
- Evidence type: static / native automated / manual live

## Host identity

- Official source:
- Asset:
- SHA256:

## Observation / assertions

- [ ] ...

## PASS boundary

A PASS proves only that:

- ...

## NOT PROVEN

This experiment does not prove:

- ...

## Reproduction

1. ...

## Evidence

- Source commit:
- Workflow run:
- Artifact:
- Digest/hash:
- Manual environment, if applicable:

## Downstream impact

- Consumer repository/plugin:
- Decision supported by this result:

## Revalidation notes

- Re-run when:
```

For mixed evidence, keep the boundaries separate. For example, IL may prove that a setter has no upper clamp while manual playback proves that a specific rate is perceptually usable; neither claim should be silently substituted for the other.
