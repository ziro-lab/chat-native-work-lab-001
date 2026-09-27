# Folded Timeline navigation has mixed native-safe and fold-unaware routes

- Status: evidence-qualified
- Repository state: stacked Draft PR #84
- Knowledge class: LAB-NATIVE + NEGATIVE-FINDING
- Surface: S1/S2 with version-sensitive folded-view adaptation
- YMM4 version: 4.55.1.1 Lite and 4.56.1.0 Lite
- Tested runtime source: `0d2fecfd1d73a79c17a40dc9f79b647ef662faaa`
- Revalidation trigger: YMM4 changes Timeline navigation/viewport behavior

## Claim

In the tested no-Harmony folded Timeline model:

- single-item selection change could be routed through shared fold-aware navigation;
- product-owned navigation through shared `NavigateTo(IItem)` was fold-aware;
- bare/same-item `TimelineViewModel.ScrollToItem` without a selection notification was **not** fold-aware;
- public `ScrollToLowerLayer` / `ScrollToHigherLayer` were already native-safe;
- real foreground Down/Up layer navigation was also native-safe.

With logical L1..L10 collapsed, the tested Lower/Higher and foreground Down/Up routes moved by display rows, not hidden logical rows.

## Safe use

Do not globally override every navigation path merely because folding exists.

Adapt only the routes proven fold-unaware, and leave native display-row-aware routes alone.

## Do not infer

- Raw viewport movement is not a reliable generic interception heuristic.
- Bare ScrollToItem should not be guessed into fold semantics without an explicit product-owned/selection route.
- This card does not prove every grouped/multi-layer/special-item navigation route.

## Failure behavior

If a navigation source cannot be classified as a validated fold-aware/fold-unaware route, avoid speculative correction.

## Evidence

- Lab PR: [#84](https://github.com/ziro-lab/chat-native-work-lab-001/pull/84)
- YMM4 4.56.1.0 run: `35704217666`
- 4.56.1.0 artifact: `10684021588`
- 4.56.1.0 digest: `sha256:a3c7091d6c2590aa417a31ba0c33eb3482961081b121bdac0b4375e2998d8017`
- YMM4 4.55.1.1 run: `35704912554`
- 4.55.1.1 artifact: `10683603059`
- 4.55.1.1 digest: `sha256:683444cb772b4d45f9d82ddd443a014f7240d43b1c11ae7567b96c075543a887`
- Marker: `PASS_P2_NAVIGATION`
