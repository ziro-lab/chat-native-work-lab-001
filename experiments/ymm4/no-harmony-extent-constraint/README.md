# No-Harmony Scroll Extent Constraint

The native TimelineViewModel CanvasHeight is a ReadOnlyReactivePropertySlim and cannot be assigned directly. This experiment asks whether ordinary WPF measurement constraints can shrink the native Timeline ScrollViewer extent anyway.

It tests content MaxHeight/Height and FastCanvas/CanvasHeight-bound element MaxHeight while keeping the existing no-Harmony direct item/row layout corrections.
