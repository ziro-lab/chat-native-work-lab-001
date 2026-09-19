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

## Result

On a real Main → Child fixture where the child duration is three seconds and the parent SceneItem has `ContentOffset=0.5 s`:

- at 100%, parent 0 s → child 0.5 s
- at 100%, parent 0.5 s → child 1.0 s
- 100% consumed relative range: `[0, 1]` s
- at 200%, parent 0 s → child 0.5 s
- at 200%, parent 0.5 s → child 1.5 s
- 200% consumed relative range: `[0, 2]` s
- the mapping starts from the child front for these positive-rate cases

Together with the static `SceneSource.Update` evidence, this proves a usable non-loop parent-to-child interval mapping baseline. A future archive optimizer can map the parent SceneItem use into child-scene time rather than automatically retaining every child VideoItem, while still keeping loop/cycle cases conservative.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- SceneItem behavior job: `105865515231`
- SceneItem behavior artifact: `10580058903`
- Artifact SHA256: `cf5155427488abe569167b56e246716415fad88f2629feac29c9db14a118f444`
