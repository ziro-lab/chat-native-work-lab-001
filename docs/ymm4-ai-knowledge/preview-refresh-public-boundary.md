# Timeline CurrentFrame change is not proof of visible preview repaint

- Status: evidence-qualified
- Repository state: main
- Knowledge class: LAB-NATIVE + NEGATIVE-FINDING
- Surface: S1 / S2 observation boundary
- YMM4 version: 4.55.1.1 Lite
- Revalidation trigger: YMM4 adds or changes a public Timeline/Preview refresh API

## Claim

On the tested YMM4 4.55.1.1 Lite host:

- direct writes to `Timeline.CurrentFrame` emitted `Timeline.PropertyChanged("CurrentFrame")`;
- `TimelineViewModel.ScrollFrame(int)` did not change `CurrentFrame`;
- the experiment found no dedicated public Plugin-facing instance route meaning "force the current preview to redraw now."

## Safe use

Use `Timeline.CurrentFrame` for the supported public Timeline state change proven here.

Do not use `ScrollFrame` as a seek/refresh substitute.

## Do not infer

- `PropertyChanged("CurrentFrame")` does **not** prove pixel-level preview repaint.
- The absence of a dedicated public refresh route in this experiment is not proof that no future YMM4 version can expose one.
- MainViewModel traversal used by a probe is not automatically a supported Plugin API.

## Failure behavior

If product acceptance shows the visible preview can remain stale after a public CurrentFrame write, do not append an unrelated rendering method or speculative reflection call. Treat the refresh need as a new version-pinned host question.

## Evidence

- Lab report: [Round 2 host behavior findings](../../experiments/ymm4/ROUND2_HOST_BEHAVIOR_FINDINGS.md#l2--preview-refresh-surface)
- Workflow run: `35367666289`
- Artifact: `10556713160`
- Artifact SHA256: `efa97e31639c66886a449e653e8759cc662cab1c08f8989afb74331f16ce356a`
