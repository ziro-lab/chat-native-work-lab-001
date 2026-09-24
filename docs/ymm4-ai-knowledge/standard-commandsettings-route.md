# Standard YMM4 commands can be routed through CommandSettings

- Status: evidence-qualified
- Repository state: Draft PR #139
- Knowledge class: LAB-NATIVE
- Surface: S1 / public command surface
- YMM4 version: 4.55.1.1 Lite and 4.56.1.0 Lite
- Tested source: `d4ebae8d9ff01f0ae4a8a5616d7198dbc504998a`
- Revalidation trigger: YMM4 changes CommandSettings / CommandType / RoutedUICommandEx behavior

## Claim

On both tested YMM4 hosts, selected standard commands resolved through:

```text
CommandSettings.Default[CommandType]
  -> YukkuriMovieMaker.Settings.RoutedUICommandEx
```

and were successfully executed through the native routed-command path.

The tested commands included:

- Undo;
- Redo;
- SeekToNextFrame;
- SeekToPreviousFrame;
- SplitCurrentPositionItems;
- AddKeyFrameAtCurrentFrame.

Observed behavior included actual Undo/Redo state mutation, frame seek 200 -> 201 -> 200, selected-item split 1 -> 2, Undo of that split 2 -> 1, and keyframe command dispatch making Undo available.

## Safe use

For a finite, explicitly validated set of YMM4 standard actions, prefer the native CommandSettings / CommandType route over synthetic keyboard input.

Check `CanExecute` in the live routed-command context before executing when the command semantics require it.

## Do not infer

- This does not approve exposing the entire CommandType catalog.
- It does not prove every command has stable semantics across versions.
- It does not prove automatic WPF Button `IsEnabled` refresh in every focus/modal state.
- It does not prove commands that were not included in the native assertion set.

## Failure behavior

If a required CommandType does not resolve to the expected routed-command surface or cannot execute in the current context, disable that action rather than falling back to synthetic key input without a separate proof.

## Evidence

- Lab PR: [#139](https://github.com/ziro-lab/chat-native-work-lab-001/pull/139)
- Workflow run: `35948972486`
- 4.55.1.1 artifact: `10787657001`
- 4.55.1.1 artifact SHA256: `ae67cbc4530fc2d3740795c0a704be869a54eb854739acecf404068871a5e759`
- 4.56.1.0 artifact: `10787653170`
- 4.56.1.0 artifact SHA256: `4994e426f8a0421cdfe008b6afc9a3d2913d77df61509f63fa9983e9c7efbac5`
- Marker: `PASS_STANDARD_COMMAND_TILE_ROUTE`
