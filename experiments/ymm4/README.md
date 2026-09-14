# YMM4 experiments

YukkuriMovieMaker4 (YMM4) native-host experiments.

These experiments do not redistribute YMM4. Workflows fetch an exact public release from the official distribution source, verify its hash, use it only on the temporary runner, and upload only original probe/evidence files.

## Current experiments

- [`plugin-host-validation/`](plugin-host-validation/) — minimal real-host plugin-load proof.
- [`timeline-selection-context/`](timeline-selection-context/) — public Timeline selection state, Character context and selection-change observation.
- [`playhead-quick-drop/`](playhead-quick-drop/) — public playhead access, independent Template clone and intrinsic-length placement.
- [`character-layer-placement/`](character-layer-placement/) — same-Character Front/Back baselines and full-span Layer collision behavior.
- [`template-identity/`](template-identity/) — restart-persistent ambiguity showing that `SceneId` is not a unique ItemTemplate identity.

## Downstream feedback

These experiments were later used to guide a larger YMM4 Template Placer implementation. Product-side integration re-tested the adopted behavior in its own native acceptance rather than treating the lab results as product acceptance.

See [`../../docs/DOWNSTREAM_FEEDBACK.md`](../../docs/DOWNSTREAM_FEEDBACK.md) for the lessons fed back into this lab.

## Scope boundary

Each experiment remains intentionally narrow. A PASS here does not claim complete plugin lifecycle, every WPF interaction, every PSD/third-party asset combination, physical installer behavior or future YMM4 compatibility unless that experiment explicitly tests it.
