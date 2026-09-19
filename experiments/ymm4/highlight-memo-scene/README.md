# Highlight memo scene — YMM4 4.56.1.0

## Goal

Verify the host behavior needed for Highlight Navigator's proposed **「見どころを確保」** action.

Target workflow:

```text
review long recording in Main
-> press 「見どころを確保」
-> create/reuse dedicated 「見どころメモ」 Scene
-> create a short VideoItem beginning at the candidate source anchor
-> place every memo at Timeline Frame 0
-> use one consecutive Layer per memo
-> keep Main active and untouched
-> preserve hit Filter identity in Remark
-> save/reload the project
```

The intended first-value feature is deliberately not a final edit extractor. Its job is to preserve an obvious starting point for later review.

## Environment / evidence

- GitHub Actions `windows-latest`
- YMM4 Lite **4.56.1.0**
- Official host ZIP SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Generated 120s FFV1 fixture; no private project/media
- Tested source head: `aa934561ff20f38f2e20c79763be110b7ee1b87e`
- Native run: **35430972362**
- Job: **105865350826**
- Build: **0 warnings / 0 errors**
- **37 required assertions PASS**
- Artifact: **10580383042**
- Artifact ZIP SHA256: `441c596b35cda229043a5abb68c9488f3b2e13324be08be6b8526a96f01cd535`

## Verified host route

The pinned host exposes and accepted the already-known public scene/project surfaces:

```text
MainModel.Scenes
MainModel.CreateNewScene()
MainModel.SelectScene(Timeline)
MainModel.SaveProject(string)
MainModel.LoadProjectFile(string)
Timeline.TryAddItems(...)
```

## Observed result — PASS

### Dedicated memo Scene can be created once and reused

Starting from one `Main` Timeline, the probe created one Scene named `見どころメモ`.
A second ensure/get operation reused the same Timeline object and did not add another Scene. The memo Timeline exposed a non-empty Guid identity.

Product-safe lookup rule supported by this probe: exactly one memo Scene -> reuse it; none -> create it; multiple -> do not guess silently.
The experiment does not claim YMM4 itself enforces Scene-name uniqueness.

### Main can remain active while memo items are added to the non-active Scene

After memo Scene creation, the probe selected `Main` again and then performed three separate `memoTimeline.TryAddItems(...)` calls.
After every add, the active Timeline was still Main.

The original long-review VideoItem in Main kept the same object reference and the same Frame, Layer, Length, ContentOffset, PlaybackRate, FilePath and Remark. Main's item count also stayed unchanged.

This is the key product fact: `見どころを確保` does not need to switch the user into the memo Scene or split/mutate the long recording.

### Frame 0 + one Layer per memo works

```text
Layer 1  Frame 0  30 s  Source offset 15 s
Layer 2  Frame 0  30 s  Source offset 55 s
Layer 3  Frame 0  12 s  Source offset 95 s
```

All three coexist successfully at the same Timeline time. There is no host requirement to stagger them along the time axis.

### Configurable duration works

The first two clips used 30 seconds and the third deliberately used 12 seconds. All lengths survived save/reload.
Therefore the product can default to 30 seconds while allowing a configurable positive duration without changing the shelf layout.

### Filter identity in Remark survives

The test Remarks survived exactly:

```text
見どころナビ｜戦闘開始
見どころナビ｜MAP切替
見どころナビ｜戦闘開始 / 大きな画面変化
```

Using `VideoItem.Remark` as a quick human-readable hit/filter label is viable.

### Save/reload preserves the memo shelf

After native save and `LoadProjectFile`:

- exactly one `見どころメモ` Scene remained;
- the memo Scene Guid was unchanged;
- all three VideoItems remained at Frame 0;
- Layers remained 1 / 2 / 3;
- 30s / 30s / 12s lengths remained;
- source offsets remained 15s / 55s / 95s;
- Japanese Remarks remained exactly;
- FilePaths remained;
- Main's original source VideoItem semantics remained intact.

The stable memo Timeline ID means a future Navigator revision can persist a project-specific memo Scene ID if that becomes preferable to name-only lookup.

## Downstream product implication

```text
current Candidate
  AnchorSourceTime
  Hit Filter names
       ↓
resolve/create memo Scene
       ↓
Frame         = 0
Layer         = next memo layer
ContentOffset = AnchorSourceTime
Length        = configured capture duration
Remark        = hit Filter label(s)
FilePath      = analyzed source
       ↓
memoTimeline.TryAddItems(...)
```

No media cut, source-file copy, re-decode or mutation of the review VideoItem is required for this memo operation itself.

## Product decisions still open

- final button wording/icon;
- exact setting UI and min/max for capture duration;
- duplicate Candidate behavior;
- whether deleted Layer numbers are reused or new captures always append below the current maximum Layer;
- recovery if multiple Scenes named `見どころメモ` exist;
- whether Navigator should persist the memo Timeline Guid or resolve primarily by name;
- behavior if original source media later moves or is deleted;
- whether to open the memo Scene automatically after a review pass.

## PASS boundary

PASS establishes on YMM4 Lite 4.56.1.0 that a Plugin can create/reuse a memo Scene, add VideoItems to that **non-active** Timeline at Frame 0 on separate consecutive Layers, leave Main unchanged, and roundtrip the result through native Project save/load.

## NOT PROVEN

- future YMM4-version compatibility;
- variable/reverse playback memo construction;
- physical layer ON/OFF UI interaction;
- source-file relocation/deletion handling;
- user-facing Navigator integration;
- Undo/Redo behavior of the eventual product button as one atomic transaction;
- final duplicate/Layer-allocation policy.
