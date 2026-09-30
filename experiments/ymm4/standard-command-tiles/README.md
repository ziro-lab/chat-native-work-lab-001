# YMM4 Standard Command Tile Route

## Goal

Prove whether a normal YMM4 Tool Plugin can invoke YMM4's own standard commands through the same public WPF command route used by ToolBox:

```text
CommandSettings.Default[CommandType]
-> RoutedCommand
-> CanExecute(parameter, Application.Current.MainWindow)
-> Execute(parameter, Application.Current.MainWindow)
```

The target product questions are:

1. Can Template Placer expose persistent native Undo / Redo buttons without synthesizing keyboard input?
2. Can a tile store a finite YMM4 `CommandType` and invoke that standard command on click?
3. Does the same route work for a non-Undo action such as frame seek and current-position split?
4. Is the midpoint command at least routable / executable in an ordinary selected-item context?

## Environment

Native automated observation on GitHub Actions Windows runners against exact official YMM4 Lite archives:

- YMM4 4.55.1.1 Lite — SHA256 `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- YMM4 4.56.1.0 Lite — SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

## Assertions

The native probe opens as a real Timeline Tool and uses the real active Timeline.

Required for PASS:

- `Undo`, `Redo`, `SeekToNextFrame`, `SeekToPreviousFrame`, `SplitCurrentPositionItems` and `AddKeyFrameAtCurrentFrame` resolve from `CommandSettings.Default` as WPF `RoutedCommand` instances.
- A synthetic Timeline addition recorded through the host `UndoRedoManager` makes native Undo executable.
- Executing the standard Undo command against the YMM4 main window removes that addition.
- Native Redo then becomes executable and restores it.
- The standard next-frame / previous-frame commands change the real Timeline current frame and return it to the starting value.
- With one selected synthetic item spanning CurrentFrame, `SplitCurrentPositionItems` is executable and changes one marked item into two marked items.
- Standard Undo restores the pre-split one-item state.

The midpoint command is additionally recorded as an observation. Its actual keyframe content mutation is not required for this experiment's PASS because the synthetic item's animation surface is not the subject of this probe.

## PASS boundary

PASS proves that the tested YMM4 versions expose a usable public-host/WPF standard-command execution route suitable for:

- persistent native Undo / Redo controls in a Tool Plugin;
- finite Action Tiles backed by YMM4 `CommandType` values;
- at least frame-step and selected-item split commands through that same route.

## NOT PROVEN

- Every YMM4 `CommandType` is safe or useful as a Template Placer Action Tile.
- Command availability is identical in every focus/modal/editor state.
- `AddKeyFrameAtCurrentFrame` produces the desired midpoint on every Item/effect type.
- Template Placer should expose the complete YMM4 command catalog.
- Product UX, persistence and compatibility acceptance.

External ToolBox source is a design precedent only; this Lab run is the native host evidence.
