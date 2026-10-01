# YMM4 PSD notation runtime probe

## Goal

On exact YukkuriMovieMaker4 Lite v4.56.1.0, run a synthetic PSD through the built-in PSD parser/file-source path and observe whether PSDTool-style layer-name markers change runtime state:

- leading `*`
- leading `!`
- `:flipx`
- `:flipy`
- `:flipxy`

This follows the static `psd-notation-surface` experiment, which found no obvious managed notation parser.

## Fixture

The probe creates a private synthetic PSD at runtime. It contains no user asset.

Control layers:

- `PlainVisible` — encoded visible
- `PlainHidden` — encoded hidden

Notation cases:

- `*StarA` — encoded visible
- `*StarB` — encoded visible
- `!BangHidden` — encoded hidden
- `Pose` — encoded visible
- `Pose:flipx` — encoded hidden
- `Pose:flipy` — encoded hidden
- `Pose:flipxy` — encoded hidden

All are root siblings so group construction cannot explain a difference.

## Assertions

The runtime callback executes inside real YMM4.

The first evidence boundary is the built-in `PsdParser.PsdFile -> YukkuriMovieMaker.Plugin.FileSource.Psd.PsdFolder.Parse` path.

Controls must prove that the fixture's visible/hidden flag is interpreted correctly:

- `PlainVisible == enabled`
- `PlainHidden == disabled`

Then the probe records whether:

- both `*StarA` and `*StarB` remain enabled or are automatically made exclusive;
- `!BangHidden` remains disabled or is automatically forced visible;
- flip suffix names remain literal in the built-in file-source model.

The probe also attempts to instantiate the built-in PSD layer editor ViewModel and records its constructor surface and resulting item state when possible.

## PASS means

PASS means the synthetic PSD was accepted by the exact host's built-in parser/file-source path and the control visibility states were interpreted correctly, so notation-case observations are meaningful at that boundary.

PASS does not mean a marker is supported. Marker support/non-support is an observation within the result.

## NOT PROVEN

Unless the editor ViewModel path is successfully loaded, the file-source result alone does not prove what the final interactive WPF control may do after a user gesture.

This experiment also does not prove final rendered flip pixels. If `:flip*` does not alter the model, rendering confirmation may still be added if a plausible downstream transform path is found.

## Environment

- YMM4 Lite v4.56.1.0
- official release archive SHA256:
  `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Windows GitHub Actions runner
- .NET 10

No YMM4 binary or user PSD is committed.
