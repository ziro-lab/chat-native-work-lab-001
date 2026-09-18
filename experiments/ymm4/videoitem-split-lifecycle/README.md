# VideoItem split lifecycle — YMM4 4.56.1.0

## Goal

Observe what the **real YMM4 host** does when a selected `VideoItem` is split at an interior timeline frame, so downstream tools can decide whether source-time analysis can survive edit-time splits.

The key question for Highlight Navigator is whether an analyzed candidate should stay attached to the original object reference, or whether it can be rebound to the current pieces by source identity/time.

## Environment / evidence

- GitHub Actions `windows-latest`
- YMM4 Lite **4.56.1.0**
- Host ZIP SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Generated 30s FFV1 Matroska fixture; no private project/media
- Tested source head: `1041e9300ce134c23359dcfa3d4aba8512881aa6`
- Native run: **35359881285**
- Job: **105648639203**
- **23 required assertions PASS**
- Artifact: **10554237891**
- Artifact ZIP SHA256: `2d77175964a67675b1b53fc83274b93f9317678b84194cb1c4efa423b41ecd3f`

The probe uses the host's public split surface:

```text
Timeline.CanSplitSelectedAndGroupedItems(Int32 frame)
Timeline.SplitSelectedAndGroupedItems(Int32 frame, ItemSplitSelectionMode mode)
```

The automated mutation uses `ItemSplitSelectionMode.None` so selection policy does not affect the object/range observation. The enum seen on this host is `None / LeftOnly / RightOnly / Center / Both`.

## Native observations — PASS

A 10-second item with `ContentOffset=5s` was split 4 timeline-seconds after its start at constant positive rates 50%, 100% and 200%.

For all three tested rates:

- YMM4 replaces the original `VideoItem`; **neither resulting piece is the original object reference**.
- two new `VideoItem` objects partition the original timeline interval with no gap or overlap;
- `FilePath`, Layer, Remark and constant `PlaybackRate2` are preserved;
- the left piece keeps the original `ContentOffset`;
- the right piece advances `ContentOffset` by the source time consumed before the cut;
- with split-selection mode `None`, selection is empty after the split.

Observed source boundaries:

```text
50%:
  before  offset 5s, timeline length 4s
  after   offset 7s, timeline length 6s

100%:
  before  offset 5s, timeline length 4s
  after   offset 9s, timeline length 6s

200%:
  before  offset 5s, timeline length 4s
  after   offset 13s, timeline length 6s
```

This matches the already-adopted positive constant-rate mapping: the cut advances source time by `timelineSeconds * rate / 100`.

The 100% right piece was then split a second time two timeline-seconds later. YMM4 produced three pieces:

```text
timeline 4s  / source offset 5s
timeline 2s  / source offset 9s
timeline 4s  / source offset 11s
```

The left piece from the first split remained. The piece targeted by the second split was itself replaced by two new objects.

## Downstream implication

**Object reference is not a stable lineage identity across split.** A product that stores only the original `VideoItem` reference must treat a normal YMM4 split as stale.

For edit-while-review, the reusable invariant is instead:

```text
analyzed source identity + source-time candidate
             ↓
scan current Timeline items
             ↓
find current item piece(s) whose native source range contains that source time
             ↓
project through the current item's PlaybackRateMap
```

Therefore an already-built source Feature Index does not inherently need to be decoded again merely because the Timeline item was split. Product correctness still requires its own rebinding/integration tests.

## Failure history

- run `35358505907`: probe compilation failed; no split claim.
- run `35358683675`: build passed; direct TimelineViewModel command discovery was the wrong surface. Runtime reflection exposed the actual Timeline split methods; no split behavior claim.
- run `35359272227`: exact split signature captured; parameter mapping intentionally stopped before mutation.
- run `35359567480`: real 100% split succeeded and showed both output references are new. The then-expected “one piece keeps original reference” assertion failed and was corrected rather than hidden.
- run `35359865007`: all expanded host assertions passed, but the runner still contained the previous requirement-count gate.
- run `35359881285`: final source + runner contract PASS.

## NOT PROVEN

This experiment does **not** prove:

- physical mouse/keyboard split gestures;
- selection modes other than `None` beyond enum discovery;
- Undo/Redo semantics;
- project save/restart identity;
- arbitrary animated, zero or reverse playback-rate split semantics;
- moving a split piece by physical drag;
- copied/duplicated occurrence disambiguation after edits;
- Highlight Navigator's rebinding implementation or product acceptance;
- behavior on YMM4 versions other than the pinned 4.56.1.0 host.

Re-run or extend this probe if one of those claims becomes material.
