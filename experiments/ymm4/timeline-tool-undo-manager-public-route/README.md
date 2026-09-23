# TimelineToolInfo UndoRedoManager public route — YMM4 4.56.1.0

## Question

Does real YMM4 deliver the active host `UndoRedoManager` to a normal timeline tool through the public Plugin API:

```text
ITimelineToolViewModel.SetTimelineToolInfo(TimelineToolInfo info)
                                    ↓
                         info.UndoRedoManager
```

## Why

PR #130 proved the correction Undo/Redo semantics, but its native probe acquired the manager through bounded reflection. Product code should prefer a public host-provided route.

## Native gate

- exact YMM4 Lite 4.56.1.0;
- real `IToolPlugin` + `ITimelineToolViewModel`;
- host invokes `SetTimelineToolInfo`;
- `TimelineToolInfo.Timeline` is non-null;
- `TimelineToolInfo.UndoRedoManager` is non-null;
- received manager exposes public `AddCommand`, `Record`, `UndoAsync`, and `RedoAsync`.

The bootstrap may programmatically invoke the tool menu entry only to cause the real host to instantiate/open the tool. It must not construct `TimelineToolInfo` itself.

## Not proven

This experiment does not exercise correction Undo/Redo again; PR #130 owns that behavior proof.
