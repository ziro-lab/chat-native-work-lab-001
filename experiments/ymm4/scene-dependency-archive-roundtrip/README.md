# Scene dependency archive roundtrip

## Question

Can YMM4 v4.56.1.0 represent a selected-scene archive as `selected timeline + SceneItem dependency closure`, remove an unrelated scratch scene, save it, and reload it without breaking the SceneItem reference?

## Status

- Date: 2026-09-16
- YMM4 version: v4.56.1.0 Lite
- Evidence type: native automated behavior

## Assertions

A disposable native-host fixture creates three timelines:

- `Main`
- `UsedSub`
- `Scratch`

`Main` contains a `SceneItem` referring to `UsedSub`. The probe computes dependency closure from `SceneItem.SceneId`, removes `Scratch`, saves a disposable project, reloads it, and verifies that only `Main` and `UsedSub` remain and the SceneItem still targets `UsedSub`.

## PASS boundary

A PASS proves that selected-scene dependency closure can be modeled using the observed YMM4 scene surfaces for this host version.

## NOT PROVEN

This does not prove product-side offline `.ymmp` transformation, arbitrary nested plugin references, or every future YMM4 version.
