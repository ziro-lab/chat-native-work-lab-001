# Template clone fidelity and Character rebinding are separate concerns

- Status: evidence-qualified
- Repository state: main
- Knowledge class: LAB-NATIVE
- Surface: S1
- YMM4 version: 4.55.1.1 Lite
- Revalidation trigger: YMM4 changes ItemTemplate/TachieFaceItem clone or Character binding behavior

## Claim

On the tested YMM4 4.55.1.1 Lite host:

- `TachieFaceItem.GetClone()` produced an independent item but retained the same source `Character` object reference;
- `ItemTemplate.CreateItemsAsync(int targetFps)` rebound a same-name detached Character to the registered canonical Character;
- rebinding a cloned Face item to the canonical Character while preserving the already-cloned face parameter/effect objects preserved the tested non-default effect state.

Therefore:

```text
template clone content fidelity
!=
destination Character canonicalization
```

## Safe use

A plugin that places/clones Tachie Face items should treat content cloning and destination-Character rebinding as two explicit steps when the destination context requires canonical Character identity.

Preserve already-cloned face/effect state across the rebind rather than reconstructing it unnecessarily.

## Do not infer

- This does not prove arbitrary third-party effect fidelity.
- It does not prove visual/rendering fidelity for arbitrary PSD assets.
- Exact PsdTachiePlugin compatibility remains outside this experiment.
- A clone retaining the source Character reference does not make that reference a durable identity across restart/project reload.

## Failure behavior

If a destination Character cannot be resolved unambiguously, do not guess and silently rebind. Preserve the clone/source state or require an explicit relink policy.

## Evidence

- Lab experiment: [template-clone-fidelity](../../experiments/ymm4/template-clone-fidelity/)
- Exact CharactorMotion proof: version 1.1.1, package SHA256 `e8c123410be76b3df87ea7fa3bac0354e75788f295ada3a3f2102433f28110f2`
- Workflow run: `35137826895`
- Artifact: `10463638116`
- Artifact SHA256: `79c0f60d10755e111205de738437fa4fc951b30dc56cb84b05e804292ded4ecc`
