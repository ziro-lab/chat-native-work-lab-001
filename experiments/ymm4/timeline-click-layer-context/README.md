# YMM4 Timeline Click Layer Context

## Question

On exact YMM4 4.55.1.1 Lite, when the user clicks **blank Timeline background on a known layer row**, can a Timeline Tool obtain that clicked layer number from a public semantic host value or a stable WPF/DataContext route **without deriving it from screen Y / row height**?

This experiment exists for Template Placer's proposed Generic placement mode:

- Timeline-clicked layer, if a semantic route exists;
- otherwise explicit numeric layer only.

## Method

The native probe creates three synthetic VoiceItems on layers 1, 3 and 5, renders the real Timeline, and injects OS clicks into blank background horizontally beside each rendered Item while staying on the same row.

For each click it records:

- the real WPF hit route;
- public integral properties whose names contain `Layer` on route elements/DataContexts;
- public `TimelineViewModel` methods whose names contain `Layer` and whose input can be the real local click point/Y.

A candidate is accepted only when **the same public semantic member** returns 1, 3 and 5 for the three independent clicks.

The probe may use private MainViewModel traversal only to bootstrap the harness. That bootstrap route is not counted as a product API.

## PASS boundary

`PASS_LAYER_CLICK_OBSERVATION` means all three real blank-background click rows were reached and the public semantic candidates were evaluated.

`usable_click_layer_route=True` is a stronger positive result: at least one identical public semantic member matched all three known layer numbers.

`usable_click_layer_route=False` is a valid negative observation and means Template Placer must not infer layer from Y coordinates.

## NOT PROVEN

- other YMM4 versions;
- arbitrary DPI/theme layouts;
- semantic routes that are private/internal only;
- coordinate/row-height formulas (intentionally excluded);
- product integration.
