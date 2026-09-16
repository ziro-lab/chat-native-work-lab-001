# YMM4 Template clone / Character rebind fidelity

Purpose: inspect the exact YMM4 4.55.1.1 Lite host behavior relevant to reproducing a `TachieFaceItem` from an `ItemTemplate` without copying private/user Template data into the public lab.

## Questions

When a downstream plugin reproduces a YMM4 Item Template:

- what does `TachieFaceItem.GetClone()` do with Character identity and effect state?
- does YMM4's own `ItemTemplate.CreateItemsAsync()` canonicalize a detached same-name Character?
- can a cloned Face be rebound to the destination Character without losing already-cloned effect state?
- does the exact third-party `CharactorMotion` plugin preserve percentage/enable state through clone and Character rebind?

## Proven observations

Against exact **YMM4 4.55.1.1 Lite**:

- `TachieFaceItem.GetClone()` returns an independent item but keeps the **same source `Character` object reference**.
- `Character`, `CharacterName`, `TachieFaceParameter`, and `TachieFaceEffects` are publicly writable.
- `ItemTemplate.CreateItemsAsync(int targetFps)` is public and, when a same-name Character is registered, produces a Face bound to that canonical Character rather than the detached source Character.
- YMM4 exposes public `Json.GetClone<T>`.
- YMM4's internal `Project.MainModel` exposes public `AddTemplateItemAsync(int frame, int layer, ItemTemplate template)`; the containing type itself is not public, so this is observation rather than a recommended dependency.
- A non-empty built-in/community effect is independently cloned with a non-default property intact.
- Rebinding a clone's `Character` to the canonical Character while preserving the clone's Face parameter/effect objects keeps the tested non-default effect state.

This establishes the important distinction:

```text
Template clone content fidelity
!=
canonical Character rebinding for the destination context
```

## Exact CharactorMotion compatibility proof

The lab additionally downloads **CharactorMotion ver1.1.1** at CI runtime from the author's official GitHub release. The package is pinned to SHA256:

`e8c123410be76b3df87ea7fa3bac0354e75788f295ada3a3f2102433f28110f2`

The third-party binary is **not committed to this repository and is not uploaded in the evidence artifact**.

Inside actual YMM4 4.55.1.1, `CharactorMotion.CharactorMotionEffect, CharactorMotion 1.1.1.0` is tested with:

- `ZoomCorrection = 5`
- `IsEnabled = false`

The native probe proves:

- the effect list and effect object are independently cloned;
- `ZoomCorrection=5` survives `TachieFaceItem.GetClone()`;
- `IsEnabled=false` survives `GetClone()`;
- after canonical Character rebind using the same save/restore pattern as the downstream product, the same cloned effect object remains attached;
- `ZoomCorrection=5` and `IsEnabled=false` remain unchanged after rebind;
- the source Template Character and source effect value remain unchanged.

Key marker: `PASS_CHARACTOR_MOTION_FIDELITY`.

Successful compatibility run:

- workflow run: `35137826895`
- job: `104934736290`
- evidence artifact: `10463638116`
- artifact SHA256: `79c0f60d10755e111205de738437fa4fc951b30dc56cb84b05e804292ded4ecc`

## PASS boundary

PASS proves the host/clone/rebind observations above and exact compatibility with the tested **CharactorMotion 1.1.1** numeric/enable-state fixture. It does not prove arbitrary third-party plugins or rendering output.

## NOT PROVEN

- exact `PsdTachiePlugin` runtime clone/rendering fidelity;
- arbitrary third-party effect serialization/clone behavior;
- visual rendering fidelity of a user's PSD assets;
- every downstream product workflow merely from this lab result.

`PsdTachiePlugin` is intentionally not bundled into this public experiment because its current FC2 distribution path has not yet been reduced to a direct immutable/hash-verified CI source. Downstream code should preserve the cloned `ITachieFaceParameter` around Character rebind and keep a hands-on gate for real PSD content until an exact native fixture is available.
