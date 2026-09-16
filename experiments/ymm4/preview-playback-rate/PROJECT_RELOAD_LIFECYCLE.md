# Preview playback-rate selector lifecycle across project load

## Observation

Date: 2026-09-16

YMM4 version in the current investigation: **v4.56.1.0 Lite**.

A hands-on user test of the downstream `YMM4 プレビュー再生速度拡張プラグイン` v0.1.0 found this lifecycle behavior:

```text
YMM4 starts
→ plugin extends preview speed selector to x32
→ user loads an existing YMM4 project
→ selector maximum visibly returns to x8
```

This is a **manual live observation**, not a public-Lab automated project-file load proof.

## Interpretation

The downstream v0.1.0 implementation extended the first matching Preview speed `ComboBox` once and then stopped discovery.

The observed return to the standard x8 UI is consistent with YMM4 rebuilding, replacing, rebinding or resetting the Preview selector during project load. The earlier public-Lab UI proof established that the native v4.56.1.0 selector contains exactly 32 inline items (`x0.25` through `x8.0`) with `SelectedIndex` bound to `PlaybackRate`.

Therefore UI extensions that modify this selector must not assume that the first visual instance or first item collection remains authoritative for the full application session.

## Downstream fix pattern

`ymm4-plugin-garage` v0.1.1 changed the plugin from one-shot extension to lifecycle-aware reapplication.

After the selector is found, the product watches only relevant local lifecycle signals:

- selector `Unloaded`;
- `DataContextChanged`;
- Items `CollectionChanged`.

If the matched selector is unloaded/replaced, discovery resumes. If its inline items are reset, the extended range is restored. The normal healthy state does not use permanent Visual Tree polling.

No Harmony patch, Reflection hook, project-load method hook, custom playback engine or audio replacement is required.

## Product-side real-host regression evidence

The downstream Garage native proof runs inside the real pinned YMM4 v4.56.1.0 host. It now intentionally reduces the live selector from 128 PlaybackRate indices back to the native 32-item/x8 state and requires automatic re-extension before PASS.

Garage PR #3 validation:

- workflow run: `35112517947`
- job: `104849608042`
- result: PASS
- distribution build: 0 warnings / 0 errors
- proof build: 0 warnings / 0 errors
- assertion: `host-like selector reset returns to native x8 item range` PASS
- assertion: `selector automatically re-extends after host-like reset` PASS
- x16/x32 source↔target PlaybackRate mapping re-verified after recovery

This proves recovery from the same **observable selector state** that caused the hands-on failure. It does **not** claim that Actions opened the user's actual project file or reproduced every internal event sequence used by YMM4 project loading.

## Reusable YMM4 plugin lesson

For plugins that augment YMM4 WPF controls discovered from the live visual tree:

```text
initial control found
≠ control remains the same for the entire YMM4 session
```

When the host may rebuild a view during project creation/load/switching, prefer narrow lifecycle-aware reattachment over one-shot mutation. Keep the trigger close to the modified control and fail closed when the expected binding/data-context contract no longer matches.

## Remaining hands-on gate

Install `Ymm4PreviewSpeedExtension-v0.1.1.ymme` in the user's normal YMM4 environment, confirm x32 is visible before project load, load the same real project that exposed the v0.1.0 failure, and confirm x32 remains/restores afterward.
