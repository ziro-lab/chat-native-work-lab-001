# no-Harmony structural mutation semantics

Authority chain:

- #63 frozen A/B/C work plan;
- #69 discovery of public structural-edit surfaces;
- #72 Track C + FileDrop: 134/134 strict assertions on both pinned hosts.

This probe is the first mutation step after the central no-Harmony input/display spine became green.

It does **not** guess how folder ranges should respond to structural edits. It first records how YMM4 itself transforms marker-item layer indices for public:

- `Timeline.AddLayer(3)`;
- `Timeline.DeleteLayer(3)`;
- `LayerSelection.SelectedLayers=[3]` + `Timeline.MoveLayer(+1)`.

Each mutation must change the marker map, emit at least one public Timeline/LayerSettings UndoRedoCommandCreated event, survive real Ctrl+Z/Ctrl+Y exactly, and return to the same baseline before the next mutation.

The output records target-item sets, layer maps, layer selection and event counts. These facts will define the FolderRangeTracker rules in the next commit. No folder policy is frozen until both YMM4 4.55.1.1 and 4.56.1.0 agree.

No Harmony or product UI/persistence is introduced here.

## Product-boundary note

The operation-specific structural probe in this directory is intentionally Lab-only evidence.

The product-facing P1.2 `FolderRangeTracker` must stay independent from YMM4/WPF host types. Add/Delete/Move command observation belongs outside the tracker and is translated into small structural deltas first.

Likewise, this probe does not establish a requirement for one product adapter per structural command. P1.3 will compare the standard routed-command evidence with a generalized Harmony-free before/after delta observer (including the Timeline/LayerSettings + UndoRedoManager pattern used by prior LayerPatan-style tracking) before freezing the product observation boundary.

See `docs/YMM4_NO_HARMONY_FULL_COMPLETION_ROADMAP.md`, especially the Product architecture convergence rules and P1.6 Architecture Convergence Gate.
