# Track C integrated native navigation

Prerequisites: core Track C #72 and real FileDrop #73 are both green on the pinned YMM4 hosts.

This gate asks a deliberately small question: once the accepted direct display owns the Timeline item VM Top values, does YMM4's **unchanged native TimelineViewModel.ScrollToItem(item)** automatically navigate using the folded coordinates?

No navigation adapter is installed initially. Adding a new interception layer is justified only if native ScrollToItem still uses logical/uncompressed geometry.

## Acceptance

Under disjoint layout A and nested layout B:

- native ScrollToItem for visible logical L9 and L20;
- target VM Top matches shared FolderLayout;
- target is realized and visible;
- native Viewport contains the folded target;
- geometry/extent remain stable and non-reentrant;
- LayerHeight=40 still works;
- detach restores native navigation/geometry;
- no Harmony.

A hidden L3 target is observed separately. Its final UX policy (stay collapsed owner vs auto-expand) is not inferred from the visible-target PASS.
