# YMM4 experiments

YukkuriMovieMaker4 (YMM4) native-host experiments and version-pinned host observations.

These experiments do not redistribute YMM4. Workflows fetch an exact public release from the official distribution source, verify its hash, use it only on the temporary runner, and upload only original probe/evidence files.

YMM4 host-behavior observations used by downstream plugins should be recorded here rather than living only in product repositories. See [`../../docs/YMM4_OBSERVATION_POLICY.md`](../../docs/YMM4_OBSERVATION_POLICY.md) and [`../../docs/YMM4_OBSERVATION_TEMPLATE.md`](../../docs/YMM4_OBSERVATION_TEMPLATE.md).

## Current experiments

- [`plugin-host-validation/`](plugin-host-validation/) — minimal real-host plugin-load proof.
- [`timeline-selection-context/`](timeline-selection-context/) — public Timeline selection state, Character context and selection-change observation.
- [`playhead-quick-drop/`](playhead-quick-drop/) — public playhead access, independent Template clone and intrinsic-length placement.
- [`character-layer-placement/`](character-layer-placement/) — same-Character Front/Back baselines and full-span Layer collision behavior.
- [`template-identity/`](template-identity/) — restart-persistent ambiguity showing that `SceneId` is not a unique ItemTemplate identity.
- [`preview-playback-rate/`](preview-playback-rate/) — records the v4.56.1.0 baseline showing that the inspected preview PlaybackRate path is not statically capped at 8x, plus the remaining public-Lab reproduction boundary.

## Downstream feedback

These experiments were later used to guide larger YMM4 implementations. Product-side integration should re-test adopted behavior in its own acceptance when failure would affect the product, rather than treating a narrow Lab PASS as universal product acceptance.

See [`../../docs/DOWNSTREAM_FEEDBACK.md`](../../docs/DOWNSTREAM_FEEDBACK.md) for lessons fed back into this lab.

Small plugin prototypes currently consume YMM4 host observations through [`ziro-lab/ymm4-plugin-garage`](https://github.com/ziro-lab/ymm4-plugin-garage).

## Scope boundary

Each experiment remains intentionally narrow. A PASS here does not claim complete plugin lifecycle, every WPF interaction, every PSD/third-party asset combination, physical installer behavior or future YMM4 compatibility unless that experiment explicitly tests it.
