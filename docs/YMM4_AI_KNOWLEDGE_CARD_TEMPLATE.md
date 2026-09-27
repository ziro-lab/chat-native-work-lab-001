# YMM4 AI Knowledge Card Template

Use this template for a reusable fact that should be easy for a coding agent to consume without reading an entire experiment.

A knowledge card is an index/summary layer. The original official source, reference implementation or Lab experiment remains canonical evidence.

```md
# <short claim name>

- Status: evidence-qualified / candidate / deprecated
- Repository state: main / closed-unmerged PR / open PR / Draft PR / stacked branch
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

### Treat merge state as provenance, not an evidence gate

A Draft/open experiment may be written as **evidence-qualified** when its exact tested source, host, assertions, PASS boundary and evidence identity are strong enough for reuse.

Always record the PR/branch state and exact tested source SHA.

Keep it as **candidate** when the result is still exploratory, lacks a final PASS boundary/evidence identity, has unresolved contradictory runs, or is explicitly awaiting a later gate that changes the same claim.
