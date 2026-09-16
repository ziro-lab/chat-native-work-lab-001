# YMM4 Template clone fidelity discovery

Purpose: inspect the exact YMM4 4.55.1.1 Lite host surface relevant to copying a `TachieFaceItem` from an `ItemTemplate` without leaking product-specific assumptions into the lab.

## Question

When a downstream plugin wants to reproduce a YMM4 Item Template, what does the host actually expose for:

- `TachieFaceItem.GetClone()` and Character identity;
- `Character` / `CharacterName` setters;
- Tachie face parameter/effect containers;
- ItemTemplate-related create/add/place/copy/clone methods;
- YMM4 JSON helper copy/serialization candidates?

## Method

Run a synthetic probe inside the pinned YMM4 host. The probe creates a dummy Character/TachieFaceItem, calls `GetClone()`, records relevant public/non-public members, and searches loaded YMM4 assemblies for narrowly related method candidates.

No user template, PSD, project data or third-party effect plugin is included in this public experiment.

## PASS boundary

PASS proves only that the probe loaded and recorded the stated host observations against the pinned host. It does not prove third-party plugin parameter fidelity or product correctness.

## NOT PROVEN

- PsdTachiePlugin or CharactorMotion behavior;
- imported `.ymmt` Character rebinding semantics;
- visual rendering fidelity;
- downstream Template Placer placement correctness.
