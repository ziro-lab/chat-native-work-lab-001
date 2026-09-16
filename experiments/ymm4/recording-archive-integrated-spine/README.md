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
- Main VideoItem at constant 200% and UsedSub VideoItem at constant 50% `PlaybackRate2`

The native-host probe then:

1. saves the live source project;
2. loads a detached `Project`;
3. computes scene dependency closure from Main (`Main + UsedSub`, excluding Scratch);
4. rewrites detached VideoItem `FilePath` / `ContentOffset` to archive-clip paths;
5. serializes the detached Project with YMM4 `GetJsonText()`;
6. filters only the top-level `Timelines` JSON array by stable Timeline ID, then rebuilds a typed Project with YMM4 `LoadFromText<Project>()`;
7. saves the detached archive via `YukkuriMovieMaker.Json.Json.Save`;
8. proves the live project and source `.ymmp` stayed unchanged;
9. reloads the archive and verifies scene closure, SceneItem dependency, VideoItem relinks and both PlaybackRate2 values.

No private Project backing field is modified. The only structural JSON edit is removal of whole top-level Timeline objects that are outside the dependency closure; Timeline contents and unknown Item/Effect payloads are not rewritten by custom code.

## Observed PASS

All native assertions passed:

- archive exists;
- source `.ymmp` is byte-for-byte unchanged;
- live YMM4 state is unchanged;
- exactly Main + UsedSub remain in the archive;
- Main SceneItem still resolves to UsedSub;
- Main VideoItem relinks to the new archive clip and offset;
- UsedSub VideoItem relinks to its archive clip and offset;
- 200% / 50% PlaybackRate2 values survive archive save/reload;
- validating the archive does not switch or dirty the live project.

## PASS boundary

A PASS proves the core YMM4-side recording-archive transformation spine can be combined safely for v4.56.1.0.

It does **not** test FFmpeg cutting itself, keyframe-aligned actual cut start discovery, every third-party Item/Effect payload, or variable/reverse playback. Those remain product-side acceptance boundaries.

## Evidence

- Source head: `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`
- Workflow run: `35113196291`
- Artifact: `10453342033`
- Artifact SHA256: `e8c038738faeddf6c2bae17f6eaaebae56f528a3fd2af0a3fd1603e94bcfe585`
- Exact host: YMM4 Lite v4.56.1.0
- YMM4 release SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

## Downstream decision

The Garage recording-archive plugin may now adopt this spine as its YMM4-side baseline. Product work still needs FFmpeg stream-copy planning/validation and real-project acceptance, but no remaining native-host architecture blocker was found for the agreed MVP.
