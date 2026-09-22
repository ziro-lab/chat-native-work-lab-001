# P2.1 — selection-triggered folded navigation candidate

Status: implementation candidate; native result pending. This is not P2 completion.

## Narrow question

Can a real public Timeline selection change reveal a hidden single target and follow its folded display row, without a harness calling Reveal, without changing item coordinates/playhead, and without Harmony?

## Prior material checked

- P0 Track C DirectDisplay and FolderLayout, reused unchanged through linked source.
- P1 FolderRangeTracker validation, reused by the pure candidate policy.
- `docs/YMM4_PLUGIN_SURFACE_GUIDE.md` and `docs/YMM4_REFERENCE_SOURCES.md`.
- PR #57 timeline-navigation-handoff: public selection/frame navigation and horizontal ScrollFrame evidence. That does not prove folded vertical navigation.
- PR #83 lean validation policy. V0 pure tests plus V1 on pinned 4.56.1.0 only.

## Runtime candidate

`Timeline.PropertyChanged(SelectedItems)` -> coalesced dispatcher request -> candidate hidden-destination policy -> unchanged DirectDisplay settle -> FolderLayout visual row -> off-screen-only vertical scroll.

Only ancestors whose collapsed bodies hide the target are expanded. A nested owner remains collapsed itself; unrelated folders remain collapsed. The bridge does not modify folder IDs/ranges, item Layer/Frame/Length, selection, CurrentFrame, horizontal offset or native history. Collapse-state persistence/Undo UX is not frozen by this candidate.

A new selection invalidates pending work. Empty or multi-selection is not guessed. Detach invalidates queued work and unsubscribes. Active/captured gestures are deliberately not adapted in this slice.

## V0

The real C# policy runs without YMM4/WPF on Linux. Cases cover nested target/owner, inclusive end, independent folder, bounds, invalid ranges, non-mutation and idempotence. No substitute implementation is used as evidence.

## V1 native contract

The probe first observes the raw public selection route with the adapter absent. It then changes only public Timeline selection; the bridge, not the harness, reveals/follows the target. Machine checks cover realized on-screen targets, preserved unrelated collapse state, rapid replacement/cancellation, ambiguous multi-selection, detach, unchanged item geometry and no Harmony.

The runner checks required assertion names, not an arbitrary assertion-count threshold. Exact host ZIP and source/checkout/tree are recorded automatically by CI; exploratory runs are not promoted into phase-freeze evidence.

Reproduce with `.github/workflows/ymm4-no-harmony-p2-navigation.yml`.

## NOT PROVEN / next routes

- 4.55.1.1; both-host V2 remains a P2 exit requirement.
- native keyboard layer navigation or grouped/multi-layer gestures.
- arbitrary ScrollToItem calls, especially calls without a selection change; public member inventory is static discovery only.
- viewport changes arriving after the queued settle, drag-time selection, or same-item re-navigation without SelectedItems notification.
- automatic seek-only navigation, Preview playback synchronization or perceptual smoothness.
- other placement/template/add/context routes.
- native Undo/Redo of auto-expansion, persistence, product UX, release package.

The existing P0/P1 sources and golden assertions are not modified or replayed by this leaf candidate.
