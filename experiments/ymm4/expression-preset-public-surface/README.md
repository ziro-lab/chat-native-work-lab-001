# YMM4 expression preset public-surface probe

## Question

On exact YMM4 4.55.1.1 Lite, is there a plugin-usable public route that can support Template Placer's proposed expression mode:

1. enumerate saved expression/item presets,
2. identify a preset stably enough to select it later,
3. create a bare TachieFaceItem for a Character,
4. apply the selected preset without private fields, fake input, or UI Automation?

This first phase is deliberately discovery only. It records the exported/public surface and exercises only public bare-face construction when possible. It does not infer an application route from names.

## Evidence type

- native automated observation on Windows GitHub Actions
- public reflection over loaded YMM4 assemblies
- exact host ZIP downloaded during the run and hash-checked

Reflection is used only by this Lab probe to discover the host surface. A downstream product must consume a concrete bounded public API established by a later exercise, not copy the broad discovery scan.

## PASS boundary

PASS_EXPRESSION_PRESET_PUBLIC_SURFACE_DISCOVERY means the exact host ran the probe and emitted a machine-readable inventory of public TachieFaceItem construction, exported Preset surfaces, public preset-like containers and public application candidates.

## NOT PROVEN

This discovery run alone does not prove that a real user-saved preset can be enumerated or applied. If a concrete route is discovered, a second native run must exercise that route before downstream use.

## Downstream impact

Used only to decide whether Template Placer should add a one-button Template mode / Expression preset mode switch to the expression batch screen.
