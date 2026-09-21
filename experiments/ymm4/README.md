# YMM4 experiments

YukkuriMovieMaker4 (YMM4) native-host experiments and version-pinned host observations.

These experiments do not redistribute YMM4. Workflows fetch an exact public release from the official distribution source, verify its hash, use it only on the temporary runner, and upload only original probe/evidence files.

YMM4 host-behavior observations used by downstream plugins should be recorded here rather than living only in product repositories. See [`../../docs/YMM4_OBSERVATION_POLICY.md`](../../docs/YMM4_OBSERVATION_POLICY.md) and [`../../docs/YMM4_OBSERVATION_TEMPLATE.md`](../../docs/YMM4_OBSERVATION_TEMPLATE.md).

Before creating a new probe, check [`../../docs/YMM4_REFERENCE_SOURCES.md`](../../docs/YMM4_REFERENCE_SOURCES.md) first, then [`../../docs/YMM4_PLUGIN_SURFACE_GUIDE.md`](../../docs/YMM4_PLUGIN_SURFACE_GUIDE.md). The registry covers official documentation/samples, shipped community source, API indexes, community notes and existing implementations; the surface guide records the S1-S4 dependency escalation ladder.

## Current experiments

- [`plugin-host-validation/`](plugin-host-validation/) — minimal real-host plugin-load proof.
- [`timeline-selection-context/`](timeline-selection-context/) — public Timeline selection state, Character context and selection-change observation.
- [`playhead-quick-drop/`](playhead-quick-drop/) — public playhead access, independent Template clone and intrinsic-length placement.
- [`character-layer-placement/`](character-layer-placement/) — same-Character Front/Back baselines and full-span Layer collision behavior.
- [`template-identity/`](template-identity/) — restart-persistent ambiguity showing that `SceneId` is not a unique ItemTemplate identity.
- [`preview-playback-rate/`](preview-playback-rate/) — records the v4.56.1.0 baseline showing that the inspected preview PlaybackRate path is not statically capped at 8x, plus the remaining public-Lab reproduction boundary.
- [`tool-group-resolution/`](tool-group-resolution/) — YMM4 4.55.1.1 Tool Plugin group-key / existing Utilities-group observation.
- [`preview-refresh-surface/`](preview-refresh-surface/) — YMM4 4.55.1.1 Plugin-facing preview-refresh surface and CurrentFrame/ScrollFrame event observation.
- [`timeline-input-intent/`](timeline-input-intent/) — YMM4 4.55.1.1 real input-route ordering for Item/re-click/blank/ruler/drag/keyboard/playback-style actions.

### Recording archive investigation — YMM4 v4.56.1.0

The recording-archive candidate has a dedicated evidence chain:

- [`videoitem-archive-surface/`](videoitem-archive-surface/) — VideoItem archive/timing surfaces.
- [`scene-archive-surface/`](scene-archive-surface/) — Scenes / Timeline / SceneItem dependency surfaces.
- [`project-save-archive-surface/`](project-save-archive-surface/) — project save/path discovery.
- [`project-save-copy-roundtrip/`](project-save-copy-roundtrip/) — demonstrates why live Save-As + path restore is not the product route.
- [`videoitem-playbackrate-surface/`](videoitem-playbackrate-surface/) — `PlaybackRate2` is the supported speed surface; legacy `PlaybackRate` is obsolete.
- [`videoitem-constant-playbackrate-roundtrip/`](videoitem-constant-playbackrate-roundtrip/) — 50 / 100 / 200% `PlaybackRate2` plus ContentOffset survive project serialization.
- [`videoitem-media-length-rate-mapping/`](videoitem-media-length-rate-mapping/) — proves `ContentLength` must not be used to infer source usage.
- [`videoitem-source-time-il-surface/`](videoitem-source-time-il-surface/) — native video playback routes source-time calculation through `PlaybackRateMap`.
- [`playbackratemap-source-time/`](playbackratemap-source-time/) — host `PlaybackRateMap` forward/inverse source-time behavior for constant 50 / 100 / 200%.
- [`scene-dependency-archive-roundtrip/`](scene-dependency-archive-roundtrip/) — selected scene + SceneItem dependency closure survives save/reload while unrelated Scratch is removed.
- [`detached-project-serialization-surface/`](detached-project-serialization-surface/) — native YMM4 JSON serializer surface discovery.
- [`detached-project-json-save-roundtrip/`](detached-project-json-save-roundtrip/) — detached Project can be saved without mutating live state or the source `.ymmp`.
- [`recording-archive-integrated-spine/`](recording-archive-integrated-spine/) — combines scene pruning, VideoItem relinking, PlaybackRate preservation, detached serialization and reload validation in one real-host proof.

Adopted downstream facts for the recording-archive plugin:

1. The live project should be **read-only during archive generation**. `SaveProject(archive)` switches the live path; restoring the path marks the live model unsaved.
2. Archive creation should use `LoadProjectFile(source)` -> detached `Project` -> detached mutation -> `YukkuriMovieMaker.Json.Json.Save()` -> `LoadProjectFile(archive)` validation.
3. Scene selection is modeled as the requested Timeline IDs plus transitive `SceneItem.SceneId` dependencies.
4. Detached `Project.Timelines` has no setter. The proven non-private-field route is YMM4 `GetJsonText()` -> filter only the top-level `Timelines` array by stable Timeline ID -> `LoadFromText<Project>()` -> `Json.Save()`.
5. `PlaybackRate2` is authoritative; legacy `BaseItem.PlaybackRate` is obsolete.
6. The native video playback path uses `PlaybackRateMap`. For constant positive rates, `sourceTime = ContentOffset + itemTime * rate / 100` and inverse lookup is valid on the half-open item interval.
7. `ContentLength` remains the media duration in the tested 50 / 100 / 200% + ContentOffset cases and is not a source-range-planning value.
8. Final integrated native proof at source head `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`: workflow run `35113196291`, artifact `10453342033`, artifact SHA256 `e8c038738faeddf6c2bae17f6eaaebae56f528a3fd2af0a3fd1603e94bcfe585`.

## Downstream feedback

These experiments were later used to guide larger YMM4 implementations. Product-side integration should re-test adopted behavior in its own acceptance when failure would affect the product, rather than treating a narrow Lab PASS as universal product acceptance.

See [`../../docs/DOWNSTREAM_FEEDBACK.md`](../../docs/DOWNSTREAM_FEEDBACK.md) for lessons fed back into this lab.

Small plugin prototypes currently consume YMM4 host observations through [`ziro-lab/ymm4-plugin-garage`](https://github.com/ziro-lab/ymm4-plugin-garage).

## Candidate host questions

These are intentionally **not** experiments yet. Promote one into an isolated experiment only when a downstream plugin needs the answer.

- **ToolState lifecycle** — exact `SaveState` / `LoadState` invocation and persistence boundaries across panel/lifecycle transitions.
- **SettingsBase persistence** — minimal persistence lifecycle for `SettingsBase<T>` on an exact YMM4 build.
- **Specific Harmony target compatibility** — only when a real feature cannot be implemented through lower-risk public surfaces; test the exact target/semantic claim rather than Harmony generically.

## Scope boundary

Each experiment remains intentionally narrow. A PASS here does not claim complete plugin lifecycle, every WPF interaction, every PSD/third-party asset combination, physical installer behavior or future YMM4 compatibility unless that experiment explicitly tests it.


## Round 2 host behavior findings

- [YMM4 4.55.1.1 Host Behavior — Round 2 Findings](ROUND2_HOST_BEHAVIOR_FINDINGS.md)
