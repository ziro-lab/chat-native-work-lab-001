# ItemTemplate SceneId is not a durable unique identity

- Status: evidence-qualified
- Repository state: main
- Knowledge class: NEGATIVE-FINDING + LAB-NATIVE
- Surface: S1
- YMM4 version: 4.55.1.1 Lite
- Observed / inspected: canonical Lab experiment
- Revalidation trigger: a YMM4 update changes ItemTemplate identity/public Guid surfaces

## Claim

On the tested YMM4 4.55.1.1 Lite host, `ItemTemplate.SceneId` is not a unique restart-persistent template identity.

The native experiment persisted two distinct templates with the same Name, Path and SceneId. Both survived restart as separate records.

## Safe use

A plugin that needs its own durable library identity should keep a plugin-owned stable ID and treat YMM4 template metadata as a locator.

When an exact locator resolves to more than one live template, treat the result as ambiguous rather than selecting one.

## Do not infer

- This does not prove how future YMM4 versions identify templates.
- This does not prove rename/move behavior.
- It does not mean YMM4 templates lack all possible identity semantics; it proves the tested public locator fields are not API-enforced unique keys.

## Failure behavior

If the saved locator resolves to zero or multiple candidates, fail closed and require relink/remove rather than fuzzy-guessing a template.

## Evidence

- Lab experiment: [template-identity](../../experiments/ymm4/template-identity/)
- YMM4 ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- Source head: `934148d8ab612b0b2e6794f51768107b4153f058`
- Workflow run: `34834213261`
- Artifact: `10343732270`
- Artifact SHA256: `e225884aa2a56e8de64bda5d8cef25f6c7e4b20fc00d25f94bcc628dbf8015d1`
