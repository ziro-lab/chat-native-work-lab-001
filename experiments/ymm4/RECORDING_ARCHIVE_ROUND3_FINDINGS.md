# Recording Archive — Round 3 boundary findings

Recorded: 2026-09-19 JST.

## Scope

This round asks which remaining Recording Archive concerns can be answered from the exact YMM4 host before relying on user-project manual testing.

Exact host:

- YMM4 Lite 4.56.1.0
- official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- final tested head: `737127e52d9dcb7ef3a0aaef5b7c87862c170c24`
- final workflow run: `35431031566`
- native-boundaries job: `105865515319`, completed / success
- SceneItem behavior job: `105865515231`, completed / success
- native artifact: `10580747697`
- native artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
- SceneItem behavior artifact: `10580058903`
- SceneItem behavior artifact SHA256: `cf5155427488abe569167b56e246716415fad88f2629feac29c9db14a118f444`

All observations completed successfully on the final run. A PASS means the stated host behavior was observed; it does not silently broaden product support.

## 1. Relative VideoItem paths are preserved but not project-relative portable

A VideoItem with `FilePath=recordings\clip.mp4` serializes and reloads with the relative string unchanged. After the project/media folder is copied from A to B and the A-side media is removed:

- the B-side candidate media exists;
- the loaded VideoItem still contains `recordings\clip.mp4`;
- `ContentLength` remains zero;
- live opening of the B-side project points the project path to B, but the relative VideoItem still has `ContentLength=0`.

### Product consequence

Simple project-relative FilePath rewriting does not make the archive folder portable on this host. Keep the verified absolute-path behavior unless YMM4 exposes a different relocation mechanism.

## 2. Unicode and >260-character absolute paths work in the tested host

The baseline Unicode path test passed around 200 characters.

The extended case also passed:

- media absolute path: **402 characters**
- project absolute path: **407 characters**
- exact media path preserved
- VideoItem content loaded successfully

### Product consequence

No artificial 260-character product limit is justified by this evidence.

## 3. Non-zero container timestamps are normalized to media-relative model time

A synthetic three-second MKV shifted to a roughly +5-second container origin loads successfully.

Observed YMM4 model behavior:

- `ContentLength=3 s`
- `OriginalContentLength=3 s`
- model source samples at item times 0/1/2 s are **0/1/2 s**

The YMM4 project model / PlaybackRateMap therefore exposes media-relative zero-based source coordinates rather than the container PTS origin.

### Product consequence

The current product rejection of non-zero media start timestamps is conservative, not a YMM4 model requirement. Removing it still requires a product-side FFmpeg/ffprobe proof that translates between YMM4 model time and container timestamps without changing media identity.

## 4. SceneItem parent-to-child time mapping is explicit and behaviorally usable

Static native inspection of `SceneSource.Update(TimelineItemSourceDescription)` verified that it:

- calls `PlaybackRateMap.GetSourceTime`;
- supplies SceneItem `ContentOffset`;
- supplies SceneItem `ContentLength`;
- reads `SceneItem.IsLooped`;
- reads the referenced child-scene duration;
- forwards the mapped source time to child `ITimelineSource.Update`.

A separate fresh-runner real-project behavior probe created Main → Child, with a three-second child scene and parent `ContentOffset=0.5 s`.

Observed:

- 100%: parent 0 s → child 0.5 s
- 100%: parent 0.5 s → child 1.0 s
- 100% relative consumed range: `[0,1]` s
- 200%: parent 0 s → child 0.5 s
- 200%: parent 0.5 s → child 1.5 s
- 200% relative consumed range: `[0,2]` s
- positive-rate cases start from the child-front origin

### Product consequence

A future optimizer can propagate **non-loop** parent SceneItem intervals into child-scene time instead of always conserving every VideoItem in the referenced scene. Current whole-scene retention remains the safe fallback for loop/cycle/unproven nested cases.

## 5. Exact-EOF 0% freeze reaches EOF; it is not clamped before the file source

For the synthetic 3 s / 60 fps media:

- one-frame-before-EOF time `2.983333...` s decodes one frame;
- exact `3.000000` s EOF decodes zero frames through FFmpeg;
- PlaybackRateMap/native VideoSource calculation allows the exact 3.0 s source time;
- native VideoSource does **not** clamp it to the previous decodable frame.

### Product consequence

This is a real edge case, but silently rewriting the archived source time from exact EOF to the previous frame would violate the primary requirement that the archive Project preserve the original Project's source-time semantics.

Therefore the product should **remain fail-closed for an exact-EOF 0% item** until actual YMM4 final-render/file-source semantics prove a different representation equivalent. Do not add an implicit clamp merely to satisfy FFmpeg verification.

## 6. Looping is applied above PlaybackRateMap and depends on media duration

Toggling `VideoItem.IsLooped` does not change PlaybackRateMap itself.

Native `VideoSource.CalculateSourceTime` applies loop behavior using `IsLooped` plus source duration. Replacing the original three-second source with a one-second shortened clip changes the repeated source-time mapping. Changing source duration also changes the mapping.

Final flags:

- `map_identical_with_loop_toggle=True`
- `shortening_changes_loop_mapping=True`
- `source_duration_changes_loop_mapping=True`

### Product consequence

Trimming a looped VideoItem and simply keeping `IsLooped=true` is not equivalent. Current product rejection is justified. Loop support needs a transform that preserves the original loop-period/source-duration semantics plus save/reload equivalence proof.

## 7. Product-style all-frame PlaybackRateMap verification is not a primary CPU bottleneck

Final-run timing on the hosted runner:

- 36,000 uncached reflective calls: **0.0467 s**
- 360,000 uncached calls: **0.2743 s**
- 360,000 cached-MethodInfo calls: **0.0627 s**
- 7.2 million uncached calls extrapolate to about **5.49 s**

Other successful Round 3 runs landed in the same general range.

### Product consequence

Keep the strong all-frame equivalence gate. Caching MethodInfo is a cheap optimization if real user projects show a need, but correctness does not need to be weakened preemptively.

## Round 3 product decisions

| Topic | Host observation | Current recommendation |
| --- | --- | --- |
| Relative archive paths | Relative string preserved but unresolved after relocation | Keep absolute paths; do not advertise folder portability |
| Unicode / long absolute paths | 402/407-character paths passed | No artificial MAX_PATH restriction |
| Non-zero container timestamp | YMM4 model normalizes to 0-based media time | Add product media-coordinate proof, then support |
| Dependency SceneItem | Static + behavioral parent→child mapping is available | Future non-loop dependency minimization |
| 0% exact EOF | Model reaches exact EOF; no decodable frame exists there | Fail closed; do not silently shift source time |
| Looped VideoItem | Loop depends on actual source duration above PlaybackRateMap | Keep unsupported until equivalent loop preservation is proven |
| All-frame map validation | Low mapping cost in hosted observations | Keep validation; cache reflection only if useful |

## What remains outside the current public-Lab proof

The main remaining unknowns are no longer basic model/reflection questions:

- actual final-composited YMM4 pixel/audio behavior for the unusual exact-EOF file-source case;
- real OBS/game-recording codec/GOP/VFR/edit-list variations;
- arbitrary third-party custom VideoItem/effect payloads;
- physical user workflow and filesystem/provider behavior;
- global knowledge of whether another project still needs an original recording.

Those belong in product/manual acceptance or a targeted future experiment when a concrete failure appears.
