# P2.1 — selection-triggered folded navigation candidate

Status: **V0/V1 GREEN on YMM4 4.56.1.0 Lite**. This is not P2 completion or a product/release gate.

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

## Observed result — 2026-09-22

Source / actual checkout: `bb5146189573454f45ee95ee8331caccba34c412`.
Tree: `838f6a36bc5b420d7aaaec54cad35158c4f75384`.
Run: [35699152547](https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/35699152547).

| Gate | Result |
| --- | --- |
| Linux pure C# policy, job `106652771759` | 17/17 PASS |
| YMM4 4.56.1.0 native, job `106652862394` | 24/24 named assertions PASS |
| Native build | 0 warnings, 0 errors |
| Native marker | `PASS_P2_NAVIGATION` |

Native automated observation:

- Raw public selection leaves the nested target folded and not visible.
- The bridge receives real SelectedItems notifications and reveals the target on screen without an explicit harness Reveal call.
- Only blocking ancestors open; unrelated collapsed folders remain; selecting a nested owner preserves that owner's own collapse.
- A visible off-screen target is followed using the frozen FolderLayout mapping.
- Tested selection/playhead/horizontal offset are preserved; all fixture item Layer/Frame/Length values are unchanged.
- Rapid selection replacement, clearing, ambiguous multi-selection and detach pass their cancellation/non-guessing checks.
- No navigation/display failure, no display reentry, no Harmony assembly; subscriptions release on detach.

Artifact: `10681539220`, `ymm4-no-harmony-p2-navigation-4.56.1.0`.
ZIP SHA256 reported by upload-artifact: `786f2e9ab49da37cdf6586ac38c17b1f0836b898a90548841f86c6e1a936f09b`.
This V1 result was checked through the completed job logs; the artifact was not independently downloaded/rehashed. No P0/P1 native golden rerun or secondary-host run was performed for this leaf integration.

The public-member inventory found `ScrollToItem(IItem)`, `ScrollToLowerLayer()` and `ScrollToHigherLayer()`. Their presence is static discovery, not native-route acceptance. They are the next bounded discovery entry points.

## NOT PROVEN / next routes

- 4.55.1.1; both-host V2 remains a P2 exit requirement.
- native keyboard layer navigation or grouped/multi-layer gestures.
- arbitrary ScrollToItem calls, especially calls without a selection change; public member inventory is static discovery only.
- viewport changes arriving after the queued settle, drag-time selection, or same-item re-navigation without SelectedItems notification.
- automatic seek-only navigation, Preview playback synchronization or perceptual smoothness.
- other placement/template/add/context routes.
- native Undo/Redo of auto-expansion, persistence, product UX, release package.

The existing P0/P1 sources and golden assertions are not modified or replayed by this leaf candidate. Result-record-only commits after the tested source do not change the probe/runtime/workflow bits.
