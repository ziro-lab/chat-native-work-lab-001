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

Run a synthetic probe inside the pinned YMM4 host. The probe creates a dummy Character/TachieFaceItem, calls `GetClone()`, records relevant public/non-public members, and searches loaded YMM4 assemblies for narrowly related method candidates. A built-in/community effect with a non-default enum value is also cloned to test whether effect identity and state are copied independently.

No user template, PSD, project data or third-party effect plugin is included in this public experiment.

## Proven observations

Against exact **YMM4 4.55.1.1 Lite**:

- `TachieFaceItem.GetClone()` returns an independent `TachieFaceItem` object.
- The clone keeps the **same `Character` object reference** as the source item; Character identity is not deep-cloned.
- `Character`, `CharacterName`, `TachieFaceParameter`, and `TachieFaceEffects` are publicly writable on the pinned host.
- `ItemTemplate.CreateItemsAsync(int targetFps)` is public.
- YMM4 exposes public `Json.GetClone<T>`.
- YMM4's internal `Project.MainModel` exposes public `AddTemplateItemAsync(int frame, int layer, ItemTemplate template)`; the containing type itself is not public, so this is an observed host surface rather than a recommended product dependency.
- A non-empty built-in/community `AfterImageEffect` is cloned into a different effect-list object and a different effect object.
- A non-default effect property (`Mode=Back`) remains `Back` on the clone.

These observations separate two concerns that can otherwise look like one failure:

```text
Template clone content fidelity
!=
canonical Character rebinding for the destination context
```

The pinned host's clone path preserved the tested built-in effect state, while Character object identity deliberately remained shared with the source.

## Evidence

Successful native discovery run after startup-dialog handling:

- workflow run: `35126073223`
- job: `104895301895`
- artifact: `10458804093`
- artifact SHA256: `f12be43d7ec338903b08209b4cd47adb2e486040a05702121337ec09c4f798f8`
- host ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

Earlier runs that failed before these observations were complete were probe/infrastructure development failures, not evidence that the downstream product behavior failed.

## PASS boundary

PASS proves that the probe loaded in the pinned host and recorded the host observations above. It proves built-in/community effect clone independence/state for the specific synthetic fixture used.

## NOT PROVEN

- PsdTachiePlugin behavior;
- CharactorMotion behavior or percentage-parameter fidelity;
- arbitrary third-party effect serialization/clone behavior;
- imported `.ymmt` Character rebinding semantics;
- visual rendering fidelity;
- downstream Template Placer placement correctness.

Those remain downstream integration or hands-on acceptance questions.
