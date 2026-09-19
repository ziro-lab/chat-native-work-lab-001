# Preview PlaybackRate overflow boundary — result

## Status

**PASS — YMM4 v4.56.1.0 accepts preview PlaybackRate values beyond the downstream candidate's x32 / index 127 boundary, and an out-of-range value can persist across a normal restart.**

- Observation date: 2026-09-19
- Host: YMM4 Lite v4.56.1.0
- Host ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Native automated run: `35429732680`
- Artifact: `ymm4-preview-playback-rate-overflow`
- Artifact ID: `10580690870`
- Artifact SHA256: `ba7f1dd3e97dec14a40b515a3263b13ca1997dfdb96c35853f8da5f08fd61a1b`
- Tested source HEAD: `5f58cf3e7def79f9ddffc491f328dc7e658242cd`

## Question

If a downstream plugin exposes x32 as `PlaybackRate=127`, does the real YMM4 host clamp the normal preview-speed-up command at that value, or can the setting advance past x32? If an out-of-range value such as 128 exists at shutdown, is it rejected on the next launch?

## Native automated observation

The probe did **not** install the downstream Preview Speed Extension plugin. It used the real YMM4 v4.56.1.0 native PreviewViewModel / PlaybackRate path and the native 32-item selector.

To prove that the synthetic Windows shortcut route was actually reaching YMM4, the probe first started at `PlaybackRate=126` and required one real `Ctrl+.` shortcut to produce 127 before testing the boundary.

Observed setting sequence:

```text
126
→ Ctrl+.
127
→ Ctrl+.
128
→ Ctrl+.
129
→ Ctrl+.
130
```

Machine evidence:

```text
input_route_validated=true
shortcut_observed_settings=126,127,128,129,130
shortcut_final_setting=130
```

Therefore, on the inspected build, the normal preview-speed-up input route does **not** clamp at `PlaybackRate=127`.

## Direct out-of-range assignment

The live host also accepted an explicit assignment of `PlaybackRate=128`:

```text
direct_set_128_accepted=true
```

The native selector still had exactly 32 items.

During the already-running first process, assigning an invalid SelectedIndex source value did not immediately replace the existing selected display; the selector remained showing its previous x1 entry:

```text
host_item_count=32
direct_set_128_selected_index=3
direct_set_128_text=text=x 1.0;selected_item=x 1.0
```

This is an important lifecycle distinction: an invalid source value can exist while the currently materialized selector retains an earlier valid visual selection.

## Normal restart with PlaybackRate=128

The probe then left `PlaybackRate=128` in place and closed YMM4 through the normal main-window close path.

After relaunching the same YMM4 instance:

```text
loaded_setting=128
overflow_128_persisted=true
restart_selected_index=-1
restart_text=text=;selected_item=<null>
host_item_count=32
```

So the out-of-range value **was persisted** and reloaded. On fresh binding initialization, the native 32-item selector had no valid item for index 128 and rendered with no selected item / blank text.

The probe restored the original setting before completion.

## PASS boundary

This experiment proves, for exact YMM4 Lite v4.56.1.0:

- the tested Windows `Ctrl+.` input route reaches the real YMM4 preview speed command;
- the route advances `PlaybackRate` from 127 to 128 and continues to at least 130;
- the live `YMMSettings.PlaybackRate` setter accepts 128;
- 128 can survive a normal YMM4 shutdown and restart;
- a freshly initialized native 32-item PlaybackRate selector cannot represent 128 and becomes unselected/blank.

## NOT PROVEN

This result does not prove:

- video/audio quality or stability at x32.25 and above;
- that future YMM4 versions retain the same command/settings behavior;
- the exact visual result inside any specific downstream extended selector implementation;
- that 127 is the only sensible downstream clamp point;
- behavior of a physical human keyboard versus the validated synthetic Windows shortcut route.

## Downstream implication

A downstream plugin that intentionally exposes only indices 0–127 should **not rely on YMM4 to enforce that ceiling**.

If it presents index 127 as the terminal `MAX / x32` state, it should explicitly guard both:

1. **runtime speed-up beyond 127**, and
2. **startup/recovery when persisted PlaybackRate is already greater than 127**.

Otherwise, a user can cross the intended ceiling through the normal YMM4 speed-up command and the invalid value can persist into the next launch.

## Evidence files

The workflow artifact contains:

- `seed-result.txt`
- `restart-result.txt`
- aggregate `result.txt`
- input-route markers
- build output
- .NET SDK identity
