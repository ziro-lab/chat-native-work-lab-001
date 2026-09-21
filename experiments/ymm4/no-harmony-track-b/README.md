# Isolated no-Harmony Track B

Prerequisite: Track A #70 passed 48/48 assertions on both pinned hosts, source a1eebb4d6a4e0c4d6567ce49a59d4e70b3287b23 / run 35634693661.

Authority remains #63 WORK_PLAN.md. This is an independent display-stability probe, not Track C and not a distributable product.

## Candidate

- exact known Timeline item/label/line Top and Height adaptation;
- WPF ScrollViewer content MaxHeight (the smallest successful #68 extent candidate);
- coalesced ContextIdle updates from native property/collection notifications;
- equality guards, reentrancy/rate/total budgets, immediate mutation traces;
- no MouseMove geometry callback, no synchronous full UpdateLayout;
- native model Frame/Layer are never modified by the display adapter;
- subscriptions and native geometry/constraints are restored on detach.

Host viewport is unscaled; lower-row realization must work at folded coordinates. Native item height ratios are preserved for the voice fixtures; this does not yet certify grouped/multilayer or every third-party item type.

## Acceptance

Initial fold, idle with no rewrite loop, native vertical scroll, viewport change, LayerHeight=40 then restoration, native refresh, eight A/B/identity cycles, unchanged live models, late native click/right-click liveness, detach and native click recovery.

Right-click coordinate semantics intentionally remain native/unmapped here. Track A owns input remapping; no success in this probe proves integrated right-click behavior. No marquee, item drag or file drop is installed in Track B.

References: existing #65/#66 direct geometry evidence, #67 readonly CanvasHeight negative result, #68 extent candidates and verified artifact; shared FolderLayout; public WPF scheduling/property notifications. No copied third-party implementation.

Native result pending. Do not proceed to Track C until all strict assertions pass on 4.55.1.1 and 4.56.1.0.
