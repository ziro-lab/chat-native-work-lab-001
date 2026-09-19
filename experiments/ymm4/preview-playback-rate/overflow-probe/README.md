# Preview PlaybackRate overflow boundary

## Goal

On exact YMM4 Lite v4.56.1.0, observe what happens when the real preview-speed-up shortcut crosses the product candidate's intended x32 / `PlaybackRate=127` boundary, and whether an out-of-range `PlaybackRate=128` survives a normal YMM4 restart.

This is a host-behavior experiment. It does **not** install the downstream Preview Speed Extension plugin.

## Environment

- GitHub Actions: `windows-latest`
- YMM4 Lite: v4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- .NET 10

## Method

### Shortcut boundary

1. Launch real YMM4 with a probe plugin.
2. Locate the native PlaybackRate-bound Preview `ComboBox`.
3. Set `YMMSettings.Default.PlaybackRate=126`.
4. Send the real YMM4 shortcut `Ctrl+.` through the Windows input route.
5. Require `126 -> 127` before treating the input route as valid.
6. From 127, send `Ctrl+.` three more times.
7. Record the observed PlaybackRate values and native selector state.

The 126→127 validation prevents a failed synthetic-input delivery from being mistaken for an x32 host clamp.

### Direct + restart boundary

1. Assign `PlaybackRate=128` in the live host.
2. Record whether the setting accepts it and what the native 32-item selector shows.
3. Leave 128 in place and close YMM4 through the normal main-window close path.
4. Relaunch YMM4 and record the loaded PlaybackRate and selector state.
5. Restore the original setting before the experiment ends.

## PASS boundary

A green run means the native host was launched, the `Ctrl+.` input route was first validated at 126→127, the boundary observations were captured, and the restart observation completed.

PASS does **not** mean that overflow is safe. The recorded values determine whether the host clamps, overflows, blanks the selector, or persists an out-of-range value.

## NOT PROVEN

- Video/audio quality above x32.
- Behavior in YMM4 versions other than v4.56.1.0.
- Physical-human keyboard input versus the validated synthetic Windows shortcut route.
- Downstream plugin behavior; the Garage product must separately integrate any mitigation.

## Evidence

Workflow artifacts preserve `seed-result.txt`, `restart-result.txt`, aggregate `result.txt`, build output and SDK identity.
