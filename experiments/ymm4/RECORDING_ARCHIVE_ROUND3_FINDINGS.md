# Recording Archive — Round 3 boundary findings

Recorded: 2026-09-19 JST.

## Scope

This round asks which remaining Recording Archive concerns can be answered from the exact YMM4 host before relying on user-project manual testing.

Exact host:

- YMM4 Lite 4.56.1.0
- official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- source head: `56a202408452fa6ac9ca6bffde5948b6b3a06d69`
- workflow run: `35428911261`
- job: `105859748495`, completed / success
- artifact: `10580281320`
- artifact SHA256: `a2c0fa9637ff548dfa1490bc70e4f346c8a37bb16cefc4c04b980a7574d94a99`

All eight observation steps completed successfully on the final run. A PASS means the stated observation completed; it does not silently broaden the product support boundary.

## 1. Relative VideoItem paths are preserved but not project-relative portable

Two independent native observations were run.

A VideoItem with `FilePath=recordings\clip.mp4` serializes and reloads with the relative string unchanged. After the project/media folder is copied from A to B and the A-side media is removed:

- the B-side candidate media exists;
- the loaded VideoItem still contains `recordings\clip.mp4`;
- `ContentLength` remains zero;
- detached `LoadProjectFile(B\project.ymmp)` does not rewrite the VideoItem path;
- live opening of B points the live project path to B, but the relative VideoItem still has `ContentLength=0`.

Observed result: YMM4 v4.56.1.0 does not resolve that relative VideoItem path against the containing `.ymmp` directory in the tested routes.

### Product consequence

Changing Recording Archive from absolute paths to simple project-relative paths would not make the archive folder portable on this host. The current absolute-path strategy is justified unless a different YMM4-supported relocation mechanism is found.

## 2. Unicode and >260-character absolute paths work in the tested host

The baseline Unicode path test passed at:

- media absolute path length: 200 characters;
- project absolute path length: 207 characters.

The extended path case also passed:

- >260 attempt: true;
- media absolute path length: 402 characters;
- project absolute path length: 407 characters;
- VideoItem content length remained positive after save/load;
- the exact absolute media path survived.

### Product consequence

No artificial 260-character product limit is justified by this observation. This is still one synthetic Windows/YMM4 case, not a guarantee for every filesystem/share/provider.

## 3. Non-zero container timestamps are normalized to media-relative model time

The synthetic MKV reports container `start_time=4.979` seconds through FFprobe.

YMM4 VideoItem observation:

- `ContentLength=3` seconds;
- `OriginalContentLength=3` seconds;
- a 100% PlaybackRateMap with `ContentOffset=0` maps item times 0/1/2 seconds to source times 0/1/2;
- `ContentOffset=1` maps item-time zero to source time 1.

Observed result: the YMM4 project model / PlaybackRateMap uses media-relative source coordinates rather than exposing the container's 4.979-second timestamp origin.

### Product consequence

The product's current rejection of non-zero media start timestamps is a conservative FFmpeg-backend boundary, not a YMM4 model requirement. Removing that rejection still requires a product-side stream-copy/packet-time proof that correctly normalizes container PTS/DTS.

## 4. SceneItem has an explicit parent-item to child-scene time mapping surface

Static native inspection of `SceneSource.Update(TimelineItemSourceDescription)` verified that it:

- calls `PlaybackRateMap.GetSourceTime`;
- supplies SceneItem `ContentOffset`;
- supplies SceneItem `ContentLength`;
- reads `SceneItem.IsLooped`;
- reads the referenced child scene duration;
- forwards the mapped source time to child `ITimelineSource.Update`.

`SceneItem.ContentLength` is resolved through `GlobalSceneInfo`.

### Product consequence

The host exposes enough timing structure to investigate dependency-scene interval minimization instead of always conserving every VideoItem in a referenced scene. This round proves the mapping surface, not a complete recursive-pruning algorithm. Product behavior should remain the current safe superset until an end-to-end nested-scene equivalence proof is added.

## 5. Exact-EOF 0% freeze reaches EOF; it is not clamped before the file source

The synthetic media duration is 3 seconds at 60 fps.

Observed:

- a 0% PlaybackRateMap can map both item-time zero and later item time to source offset exactly 3.0 seconds;
- the last-frame probe time 2.983333... seconds decodes one frame;
- exact 3.0-second EOF decodes zero frames through FFmpeg;
- native `VideoSource.CalculateSourceTime` returns approximately 2.9833433 seconds for the last-frame case;
- at exact EOF it returns exactly 3.0 seconds;
- therefore VideoSource does not clamp exact EOF to a prior decodable frame before passing source time onward.

### Product consequence

An exact-EOF 0% item is a real boundary case. The product should not silently rewrite it to the previous frame merely to make FFmpeg verification pass, because that would change the model source time. Until actual YMM4 final-render/file-source semantics at EOF are proven equivalent, fail-closed behavior is safer than an implicit clamp.

## 6. Looping is applied above PlaybackRateMap and depends on media duration

Toggling `VideoItem.IsLooped` does not change the PlaybackRateMap mapping itself.

Native `VideoSource.CalculateSourceTime` applies the loop behavior after that mapping. With a 3-second source, the observed loop sequence wraps at the media boundary (values include the host's small ~10 microsecond boundary offset). Repeating the same item against a 1-second shortened clip changes the mapped sequence. Changing the supplied source duration also changes loop mapping.

Final observation flags:

- `map_identical_with_loop_toggle=True`
- `shortening_changes_loop_mapping=True`
- `source_duration_changes_loop_mapping=True`

### Product consequence

Arbitrarily trimming a looped source changes loop semantics even if PlaybackRate2 and ContentOffset are preserved. The current product rejection of looped VideoItems is therefore justified.

Loop support is not impossible, but it needs a strategy that preserves the original loop period/source-duration semantics (for example retaining a proven equivalent loop window or the full source) plus archive save/reload equivalence tests.

## 7. Product-style all-frame PlaybackRateMap verification is not a major CPU bottleneck on the runner

The probe intentionally mirrors the product's reflection-heavy per-frame source-time lookup.

Observed on the GitHub Windows runner:

- 36,000 uncached calls: 0.0419328 s;
- 360,000 uncached calls: 0.2959582 s;
- 360,000 cached-MethodInfo calls: 0.0558035 s;
- estimated 7,200,000 uncached calls: 5.919164 s.

7.2 million calls corresponds roughly to 100 ten-minute 60 fps VideoItems checked twice.

### Product consequence

The correctness-oriented all-frame validation does not need to be removed. Caching the reflected `GetSourceTime` MethodInfo remains a cheap optimization with a substantial margin, especially for slower user CPUs, but the current validation model is not inherently impractical.

This is a microbenchmark of the mapping loop only; YMM4 JSON save/load, media probing, stream-copy and decoded-frame verification are separate costs.

## Round 3 product decisions

| Topic | Host observation | Current recommendation |
| --- | --- | --- |
| Relative archive paths | Relative string preserved but unresolved after relocation | Keep absolute paths; do not advertise folder portability |
| Unicode / long absolute paths | 402/407-character paths passed | No artificial MAX_PATH restriction |
| Non-zero container timestamp | YMM4 model normalizes to 0-based media time | Product support is plausible after FFmpeg backend proof |
| Dependency SceneItem | Parent-to-child time mapping surface exists | Future optimization candidate; keep current safe superset for now |
| 0% exact EOF | VideoSource passes exact EOF; FFmpeg has no frame there | Keep fail-closed until rendered/file-source semantics are proven |
| Looped VideoItem | Loop depends on source duration outside PlaybackRateMap | Keep unsupported until equivalent loop-period preservation is proven |
| All-frame map validation | ~5.9 s estimated for 7.2M uncached calls on runner | Keep validation; optionally cache MethodInfo |

## What remains outside the public Lab boundary

The main remaining unknowns are not simple model/reflection questions:

- actual final-composited YMM4 pixel/audio equivalence for unusual EOF/file-source behavior;
- real OBS/game-recording codec/GOP/VFR/edit-list variations;
- arbitrary third-party custom VideoItem/effect payloads;
- physical user workflow and filesystem/provider behavior;
- global knowledge of whether another project still needs an original recording.

Those belong in product/manual acceptance or targeted future experiments when a concrete failure appears.
