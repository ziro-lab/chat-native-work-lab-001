# YMM4 AI Knowledge Card Template

Use this template for a reusable fact that should be easy for a coding agent to consume without reading an entire experiment.

A knowledge card is an index/summary layer. The original official source, reference implementation or Lab experiment remains canonical evidence.

```md
# <short claim name>

- Status: canonical / candidate / deprecated
- Knowledge class: OFFICIAL-CONTRACT / OFFICIAL-SAMPLE / REFERENCE-IMPLEMENTATION / LAB-STATIC / LAB-NATIVE / LAB-MANUAL / NEGATIVE-FINDING / CANDIDATE
- Surface: S1 / S2 / S3 / S4 / n/a
- YMM4 version: <exact version, range, or "current public contract">
- Observed / inspected: YYYY-MM-DD
- Revalidation trigger: <when to re-check>

## Claim

<one narrow statement>

## Safe use

<what implementation decision this supports>

## Do not infer

- <nearby stronger claim that is not proven>
- <another boundary>

## Failure behavior

<what an implementation should do when the required condition is absent or ambiguous>

## Evidence

- Official/reference source:
- Lab experiment:
- Source commit:
- Workflow run:
- Artifact/digest:

## Notes

<optional implementation notes>
```

## Writing rules

### Keep one card narrow

Prefer:

> On YMM4 4.55.1.1, using the localized Utility group resource joins the existing Utilities group.

Avoid:

> Tool plugin groups and localization.

The narrow form can be versioned, disproved and revalidated.

### Preserve negative findings

A failed route deserves its own card when an agent is likely to retry it.

Example shape:

> **NEGATIVE-FINDING:** Literal Japanese `DefaultGroupName` is not a language-independent way to join the existing Utilities group on the tested en-US host.

### Separate mechanism from product policy

Mechanism:

> Public Timeline state exposes X.

Product policy:

> Template Placer chooses to use X only after Y.

Only the mechanism belongs in the generic YMM4 card unless the product policy itself teaches a reusable integration rule.

### Prefer fail-closed wording

For version-sensitive behavior, define what to do when the expected member/event/state is missing.

Examples:

- disable the feature and report unsupported host behavior;
- leave source data unchanged;
- skip an automatic correction;
- fall back to a lower-risk public route.

Do not ask an agent to guess a replacement private member.

### Keep open PRs non-canonical

A Draft/open experiment may be listed as a candidate, but do not write it as a canonical fact until its evidence is accepted into the Lab's canonical record.
