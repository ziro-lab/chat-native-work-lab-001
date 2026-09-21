# Isolated no-Harmony Track B

Authority: #63 WORK_PLAN.md. Prerequisite Track A #70: 48/48 assertions on both hosts, source a1eebb4d6a4e0c4d6567ce49a59d4e70b3287b23 / run 35634693661.

## Verified native acceptance

**PASS_TRACK_B, 120/120 strict assertions on YMM4 4.55.1.1 and 4.56.1.0**.

- source: `3d0ccecc5da14fa742a71bca850607bf2290ee86`
- run: [35636621293](https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/35636621293)
- checkout: `88a941f9f3ac67bf3660162ce1f75bf80279f8e5`
- tree: `7ca180295ae4b1c46892c3d352e8822bfec2e586`

| Host | Artifact | ZIP SHA256 |
| --- | --- | --- |
| 4.55.1.1 | 10656572902 | `5a40860f1de690a2bc67b47e5602e9aac4a609277c450ad6022d8dbdc94b3a49` |
| 4.56.1.0 | 10656138435 | `c62718c58c2a1a4e998819704151cd70d323abb4007998f1691fd4bd70bd4d09` |

Both archives downloaded and hashes, manifests, full result and progress checked. The documentation-head rerun is separate from this pinned evidence.

## Candidate

Exact known Timeline item/label/line Top and Height adaptation; WPF ScrollViewer content MaxHeight from #68; coalesced ContextIdle updates from native property/collection notifications; equality guards, reentrancy/rate/total budgets and immediate mutation traces. No MouseMove geometry callback or synchronous full UpdateLayout. The display adapter never changes item Frame/Layer. It restores subscriptions, native geometry and constraints on detach.

## Coverage

Unscaled viewport with real vertical virtualization; initial fold; three-second idle with no rewrite loop; native scroll; viewport movement; LayerHeight=40 and restoration; native refresh; eight A/B/identity cycles; unchanged live model state; late native click/right-click liveness; detach and native click recovery. Both hosts had zero reentries.

Diagnostic totals: 37/36 coalesced applies, 1447 writes and 196 FastCanvas refresh calls over the entire deliberate stress sequence, including teardown. This is not a large-project performance benchmark.

The previous run placed L20 at Frame480 while the viewport covered X0..461, conflating horizontal and vertical virtualization. The successful fixture uses Frame40+i*30, while keeping L20 genuinely outside the native vertical viewport. No vertical assertions were relaxed.

## Boundaries

Right-click remains native/unmapped in this display-only probe. Liveness is proven, not integrated coordinate semantics. No marquee/item-drag/file-drop input adapter is installed. Only VoiceItem fixtures were certified; arbitrary grouped/multilayer/third-party item geometry remains unproven. Rate/total budgets are bounded experiment safeguards, not production limits.

References: #65/#66 direct geometry evidence, #67 readonly CanvasHeight negative result, #68 extent candidate artifacts, shared FolderLayout, WPF property/collection scheduling. No third-party implementation code copied.

A and B are independently green; Track C may now integrate them. Full product UI, persistence, structural edit tracking, broad input/performance/lifecycle coverage, installer and release compatibility remain separate work.
