# Stopped async Tachie: item/parameter refresh matrix

Tests three no-playhead-movement ideas after CPU preparation finishes while the real YMM4 preview is paused:

1. Replace only the live TachieItemParameter with a content-equivalent ready clone.
2. Replace the live TachieItem with a content-equivalent clone at the same frame/layer/length.
3. Add a ready clone Item, wait until the host observes it, then remove it and ask whether the original owner is re-observed.

Each phase runs in a pristine official host copy after a no-action red control. The probe records real ITachieSource2.Update traffic/source IDs, final preview pixels, source creation count, Timeline serialization, parameter JSON, selection, input project bytes and measured Undo state.

A repaint is not automatically considered preferable: OBSERVED_ITEM_REPAINT_WITH_SIDE_EFFECTS is intentionally distinct from OBSERVED_ITEM_REPAINT_CLEAN. This experiment does not use user material or downstream product code.
