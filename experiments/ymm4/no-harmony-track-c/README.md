# Track C: integrated display and input

A prerequisite: #70, source a1eebb4d6a4e0c4d6567ce49a59d4e70b3287b23 / run35634693661, 48/48 both hosts.
B prerequisite: #71, source3d0ccecc5da14fa742a71bca850607bf2290ee86 / run35636621293, 120/120 both hosts.

The frozen A/B implementations remain untouched. C links the proven DirectDisplay and FolderLayout and adds an input-only adapter. Both adapters read the same DirectDisplay.Layout; there is no second RenderTransform fold and no direct geometry refresh from MouseMove.

This first C gate covers unscaled/virtualized normal click, right/add converter before and after native drag, marquee, single/multi non-uniform native drag with exact control Frame delta, live-identity Undo/Redo, repeated nested-layout switching, lower-row realization, idle and detach/native-edit recovery.

Fixtures keep X within the actual native viewport. The harness records only synthetic creation once; no artificial Record/Clear between native gestures. Explicit Reveal calls are harness navigation through the public viewport; they do not prove interception of every native ScrollToItem route.

Pending: integrated FileDrop, other native add routes, automatic navigation/structural edits, diverse item types, real folder UX/persistence/package. PASS_TRACK_C_INPUT is not a Full plugin acceptance marker. No production/installer claim.
