# Recording Archive SceneItem time behavior

## Question

For YMM4 4.56.1.0, can a parent `SceneItem` be mapped into the referenced child scene's time domain with the same `PlaybackRateMap` source-time primitives used by native `SceneSource.Update`?

## Method

A real YMM4 process creates a disposable Main scene and a child scene containing a real synthetic VideoItem. A `SceneItem` in Main references that child. The probe observes:

- child Timeline duration through `SceneItem.ContentLength`;
- `PlaybackRateMap.GetSourceTime(..., out startsFromContentEnd)`;
- `GetConsumedContentRange(itemLength,fps)`;
- constant 100% and 200% parent-to-child mappings with nonzero ContentOffset.

The static companion experiment `recording-archive-sceneitem-time-surface` proves that native `SceneSource.Update` consumes these same values and forwards the mapped time into the child `ITimelineSource.Update`.

## PASS boundary

A PASS establishes a non-loop SceneItem mapping baseline suitable for planning a future dependency-scene interval reduction. It does not authorize pruning looped SceneItems or arbitrary nested cycles.
