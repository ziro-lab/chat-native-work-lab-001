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
- host invokes `SetTimelineToolInfo` on the real tool view model;
- `TimelineToolInfo.Timeline` is non-null;
- `TimelineToolInfo.UndoRedoManager` is non-null;
- received manager exposes public `AddCommand`, `Record`, `UndoAsync`, and `RedoAsync`.

The bootstrap may programmatically invoke the tool menu entry only to cause the real host to instantiate/open the tool. It must not construct `TimelineToolInfo` itself.

## Not proven

This experiment does not exercise correction Undo/Redo again; PR #130 owns that behavior proof.


## Host activation observation

On YMM4 4.56.1.0 the host may instantiate/initialize the timeline tool and call
`SetTimelineToolInfo` before the Lab bootstrap explicitly invokes the Tool-menu item.
Tool-menu discovery/invocation is therefore diagnostic/fallback behavior, not part of
the acceptance gate. The product-relevant proof is that YMM4 itself supplies the
real `TimelineToolInfo` and its non-null `UndoRedoManager`.
