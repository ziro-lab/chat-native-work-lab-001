# YMM4 Preview Refresh Surface — Round 2 / L2

## Goal

Determine whether a Timeline Tool has a dedicated public Preview refresh / redraw route in YMM4 4.55.1.1, and observe what the real host exposes when `Timeline.CurrentFrame` is changed directly versus TimelineViewModel scrolling.

## Evidence classes

- **public Plugin-facing surface**: `TimelineToolInfo` and `Timeline`
- **real-host observation**: public members/event traffic on the live PreviewViewModel and TimelineViewModel, reached only by the validation probe
- this experiment does not treat MainViewModel traversal as a supported Plugin API

## PASS boundary

PASS means the exact host was inspected and both CurrentFrame and host ScrollFrame transitions were executed while event traffic was recorded. Visual pixel refresh is not inferred from PropertyChanged traffic alone.
