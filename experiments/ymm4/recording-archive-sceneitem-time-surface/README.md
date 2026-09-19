# Recording Archive SceneItem child-time surface

## Question

Which SceneItem timing members and native host methods determine what portion of a referenced child Timeline is consumed by a parent scene?

This is relevant to archive-size optimization: the current product conservatively retains every VideoItem use in a dependency scene.

## Method

Inside real YMM4 v4.56.1.0 the probe records SceneItem/BaseItem timing-related members and scans native IL for methods referencing SceneItem.SceneId together with timing/content/frame members.

## PASS boundary

PASS identifies candidate native child-time mapping surfaces. It does not authorize dependency-range minimization until a behavioral roundtrip proves exact parent-to-child time mapping.

## Result

Native `SceneSource.Update` reads:

- `PlaybackRateMap.GetSourceTime`
- `ContentOffset`
- `ContentLength`
- `SceneItem.IsLooped`
- referenced child-scene duration

and forwards the mapped time to child `ITimelineSource.Update`.

This establishes the native timing spine needed for dependency-scene interval reduction. Behavioral values are recorded separately by `recording-archive-sceneitem-time-behavior`.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
