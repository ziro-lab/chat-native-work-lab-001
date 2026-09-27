# Tool Plugin existing Utilities group should use the localized resource

- Status: evidence-qualified
- Repository state: main
- Knowledge class: LAB-NATIVE + REFERENCE-IMPLEMENTATION
- Surface: S1
- YMM4 version: 4.55.1.1 Lite
- Revalidation trigger: YMM4 changes Tool grouping/localization behavior

## Claim

On the tested en-US YMM4 4.55.1.1 Lite host, returning:

```csharp
YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName
```

as the Tool Plugin group name joined the existing built-in `Utilities` group.

A literal Japanese `"ユーティリティ"` created a separate group on that host.

The inspected YMM4 Community tools also use the localized resource.

## Safe use

When a Tool Plugin intends to join YMM4's existing Utilities / ユーティリティ group, use the host localization resource rather than a hard-coded display-language string.

## Do not infer

- The literal English string `"Utilities"` is not a language-independent contract merely because it joined the group on the tested en-US host.
- This card does not prove other YMM4 versions or future localization resource names.

## Failure behavior

If the expected localization resource cannot be resolved on a future host, do not guess a translated literal and claim it is the built-in group. Fall back safely or mark the host unsupported for that integration.

## Evidence

- Lab report: [Round 2 host behavior findings](../../experiments/ymm4/ROUND2_HOST_BEHAVIOR_FINDINGS.md#l1--tool-group-resolution)
- YMM4 ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- Source head: `9eca8374be42e86b8d905a1999a70a0e345eb2d7`
- Workflow run: `35367666297`
- Artifact: `10557201726`
- Artifact SHA256: `32c19045b2f2aa63fb6ccc73aa418fc711087eba19771f04727df05d77fa5e71`
