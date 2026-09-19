# Highlight memo scene — YMM4 4.56.1.0

## Goal

Verify the host behavior needed for Highlight Navigator's proposed **「見どころを確保」** action.

Target workflow:

```text
review long recording in Main
-> press "見どころを確保"
-> create/reuse dedicated "見どころメモ" Scene
-> add a short VideoItem at Frame 0
-> use one new Layer per memo
-> keep Main active and untouched
-> preserve filter identity in Remark
-> save/reload the project
```

The first implementation target is deliberately simple:

- no pre-roll;
- memo clip begins at the candidate source anchor;
- default product duration may be 30 seconds, but the host adapter must accept arbitrary configured lengths;
- all memo clips start at Timeline Frame 0;
- memo ordering/selection is represented by Layer, not by Timeline time;
- source media is referenced, not physically cut/re-encoded.

## Pinned host

- YMM4 Lite 4.56.1.0
- Official ZIP SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Generated FFV1 fixture only; no private user media/project.

## Required observations

The real host probe must verify:

1. `MainModel.Scenes`, `CreateNewScene()`, `SelectScene(Timeline)`, save/load surfaces are usable.
2. A scene named `見どころメモ` can be created once and later reused without creating a duplicate.
3. After returning to Main, the plugin can add VideoItems directly to the non-active memo Timeline.
4. Adding to the memo Timeline does not switch the active scene away from Main.
5. All memo items can coexist at Frame 0 on consecutive Layers.
6. FilePath, ContentOffset, Length, PlaybackRate2 and Remark are preserved.
7. A 30-second memo and a different configured duration can coexist, proving duration is not host-hardcoded.
8. The original Main VideoItem remains unchanged.
9. Save/reload preserves the memo Scene, its Timeline ID, Japanese name, item geometry/source metadata and Remarks.

## Product question this resolves

If PASS, Highlight Navigator does not need to split or mutate the long review item merely to "keep" an interesting start point. It can create a lightweight reference shelf in a dedicated scene.

This experiment does not decide final UI wording, duplicate-candidate policy, layer deletion/compaction policy or project-level persistence of a Navigator-owned memo-scene ID.
