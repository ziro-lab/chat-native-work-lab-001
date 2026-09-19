# VideoItem edit rebinding — YMM4 4.56.1.0

## Goal

Verify the real YMM4 host behavior that matters when Highlight Navigator analyzes a long recording once and the user then edits that recording while reviewing candidates.

The focused questions are:

1. What does head/tail trim do to object identity and source-time coordinates?
2. What does moving a split piece do to object identity and source-time coordinates?
3. Can two Timeline occurrences become indistinguishable by `FilePath + source range` after copy/paste?
4. What semantic/object lifecycle does the split-generated Undo/Redo command produce?

This experiment complements `videoitem-split-lifecycle`; it does not replace that evidence.

## Environment / evidence

- GitHub Actions `windows-latest`
- YMM4 Lite **4.56.1.0**
- Host ZIP SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Generated FFV1 Matroska fixture; no private project/media
- Tested source / runner contract: `df92cf66d64b3beea3b7e4fc4324539a6f373d27`
- Native run: **35424106866**
- Job: **105846989339**
- Build: **0 warnings / 0 errors**
- **25 independently required assertions PASS**
- Artifact: **10578391490**
- Artifact ZIP SHA256: `97e51bbed4ba6d3a04e31462710ed8dd40a241a9f54e54096cd7a84018d057e7`

The behavioral run uses the real 4.56.1.0 `Timeline` instance and its public edit surfaces. Undo/Redo is exercised through the actual undo command emitted by that Timeline split. This isolates the host mutation lifecycle from UI-focus/shortcut routing; it is not a physical Ctrl+Z/Ctrl+Y acceptance test.

## Observed result — PASS

### 1. Head trim changes source start in-place

At 60fps / 100%:

```text
before:
  Frame         300
  Length        600   (10 s)
  ContentOffset 5 s

head trim +120 frames:

after:
  Frame         420
  Length        480   (8 s)
  ContentOffset 7 s
```

The **same VideoItem object reference remains in the Timeline**. `FilePath` and playback rate remain unchanged.

So a strict frozen snapshot still becomes stale even though the object itself survives: Frame, Length and source start all changed.

### 2. Tail trim shortens the source interval in-place

At 60fps / 100%:

```text
before:
  Frame         1200
  Length        600
  ContentOffset 5 s

tail trim -120 frames:

after:
  Frame         1200
  Length        480
  ContentOffset 5 s
```

Again the **same VideoItem reference remains**. The source start is unchanged; only the represented source end moves earlier.

### 3. Moving a split piece preserves its source coordinates

A 100% item was split first. The right piece had:

```text
Frame         2440
Length        360
ContentOffset 9 s
```

After `MoveSelectedAndGroupedItems(+600, 0, 0)`:

```text
Frame         3040
Length        360
ContentOffset 9 s
```

The **same right-piece object reference remains**. `FilePath`, length, ContentOffset and playback rate are unchanged.

Therefore cached Timeline Frame is not a durable candidate location after ordinary movement, even though the source-time location is still valid.

### 4. Copy/paste creates genuine source-time ambiguity

Using the host's real public `CopySelectedAndGroupedItems()` and `PasteCopiedItemsAsync(...)`, two distinct VideoItem objects existed simultaneously with:

```text
same FilePath
same ContentOffset = 8 s
same Length        = 300 frames
same rate          = 100%

occurrence A: Frame 4000 / Layer 20
occurrence B: Frame 5000 / Layer 21
```

So **source identity + source range alone cannot uniquely identify a Timeline occurrence** after copy/paste.

This is not a theoretical corner case: the real host produced the ambiguity.

### 5. Split Undo/Redo restores semantics and reuses the original/split references in this observed cycle

The real Timeline split emitted an UndoRedo command. Executing that emitted command produced:

```text
original:
  one item
  Frame 6500 / Length 600 / ContentOffset 5

split:
  left  Frame 6500 / Length 240 / ContentOffset 5
  right Frame 6740 / Length 360 / ContentOffset 9
  original reference is absent

Undo:
  one item restored
  Frame 6500 / Length 600 / ContentOffset 5
  the original pre-split object reference is restored

Redo:
  two split items restored
  the same left/right object references from the first split are reused
```

The important product rule is still **do not use object identity as the candidate authority**. The initial split replaces the original object; Undo/Redo may subsequently reintroduce prior references. A current-Timeline source-time rebind handles both states without special-casing that identity churn.

## Downstream implications for Highlight Navigator

The observed edit behaviors support this separation:

```text
Review source/session authority
  Source identity
  + analyzed source range
  + Feature Index
  + candidate SourceRange
  + candidate AnchorSourceTime

Current Timeline binding
  rescan current VideoItems
  -> compute their current native source ranges
  -> choose the appropriate occurrence
  -> project AnchorSourceTime through its current PlaybackRateMap
  -> derive current Timeline Frame
```

Consequences:

- **Split:** original object disappears; rebind to current pieces.
- **Head/tail trim:** object may survive but its source range changes; re-evaluate containment rather than trusting the frozen snapshot.
- **Move:** source-time candidate stays valid while cached Frame becomes wrong; recompute current Frame/order/display.
- **Undo/Redo:** rescan after the edit state changes; do not require the same object graph.
- **Duplicate:** a separate occurrence discriminator is required. Source + time alone cannot decide which copy is the intended review lineage.
- A source Feature Index does **not inherently require re-decoding** for these Timeline-only edits. Re-decode is needed only when the source data / required analyzed source coverage itself changes.

For a candidate range that straddles a split, the product should retain a representative source anchor (for example the strongest transition time) in addition to the wider review range. That is a downstream design inference, not a YMM4 host behavior asserted by this experiment.

## Failure / discovery history

The intermediate runs are retained because they established useful boundaries rather than being silently relabeled PASS.

- `35423130633`: initial probe could not resolve the live Timeline.
- `35423215657`: exposed the active TimelineViewModel shape but still did not resolve Timeline.
- `35423367366`: generic Tool-opening helper did not produce the expected callback.
- `35423489480`: runtime graph discovery found the live Timeline and exact edit surfaces; the producer passed but the first runner gate had a PowerShell predicate bug.
- `35423689207`: trim/move/duplicate assertions passed; direct Timeline split did not make the first discovered UndoRedoManager report undoable.
- `35423788401`: enumerating discovered managers confirmed the same limitation for direct-manager testing.
- `35423904879`: Win32 shortcut attempt did not split because UI input focus was not established.
- `35423999048`: broad Timeline visual lookup was insufficient for a reliable keyboard route.
- `35424106866`: final route verifies Undo/Redo through the command emitted by the real Timeline split and passes the full 25-assertion contract.

## PASS boundary

PASS establishes the named trim/move/copy-paste/split-command lifecycle observations on the pinned YMM4 Lite 4.56.1.0 host.

## NOT PROVEN

This experiment does **not** prove:

- physical mouse drag behavior for trim/move;
- physical keyboard/menu Ctrl+B / Ctrl+Z / Ctrl+Y routing;
- Undo/Redo behavior for every other edit operation;
- project save/restart identity;
- variable, zero or reverse playback-rate edit semantics;
- how Highlight Navigator should choose between duplicate occurrences after the original lineage has intentionally been copied;
- Highlight Navigator product implementation or user acceptance;
- behavior on other YMM4 versions.

Those should be tested only when they become material to the product.
