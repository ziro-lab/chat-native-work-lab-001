# Recording archive integrated native spine

## Question

Can YMM4 v4.56.1.0 execute the recording-archive product spine non-destructively when all proven pieces are combined?

## Fixture

A disposable live project contains:

- `Main`
- `UsedSub`
- `Scratch`
- `Main -> UsedSub` through `SceneItem.SceneId`
- one `VideoItem` in Main and one in UsedSub, both pointing at source-recording paths with non-zero `ContentOffset`

The native-host probe then:

1. saves the live source project;
2. loads a detached `Project`;
3. computes scene dependency closure from Main and prunes Scratch in the detached graph only;
4. rewrites detached VideoItem `FilePath` / `ContentOffset` to archive-clip paths;
5. saves the detached archive via `YukkuriMovieMaker.Json.Json.Save`;
6. proves the live project and source `.ymmp` stayed unchanged;
7. reloads the archive and verifies scene closure, SceneItem dependency, and VideoItem relinks.

## PASS boundary

A PASS proves the core YMM4-side archive transformation spine can be combined safely for v4.56.1.0. It does not test FFmpeg cutting itself; the clip files/actual cut start remain product-side inputs.

## NOT PROVEN

This experiment does not prove every third-party item/effect payload, variable or reverse playback, or FFmpeg keyframe behavior. Those remain separate product acceptance boundaries.
