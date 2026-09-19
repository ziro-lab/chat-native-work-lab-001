# YMM4 Timeline Navigation Handoff

## Question

On exact YMM4 4.55.1.1 Lite, after a Timeline Tool performs public `Timeline.CurrentFrame + SelectItem` navigation while plugin UI owns keyboard focus:

1. does returning WPF keyboard focus to the real Timeline make the visible Preview catch up, and
2. does public `TimelineViewModel.ScrollFrame(int)` move the Timeline viewport without changing the playhead?

The experiment also inventories public TimelineViewModel visibility/viewport/frame members so a product can decide whether "follow only when off-screen" is supportable without hand-written coordinate formulas.

## Method

A synthetic 4-second video is generated from YMM4's bundled FFmpeg: red for the first 2 seconds and blue for the next 2 seconds. The probe places it in the real Timeline and samples the center of the real Preview region directly from the desktop pixels.

Sequence:

- Timeline focused, frame 30 -> establish red Preview fixture;
- plugin Tool button focused;
- public `CurrentFrame=180` + `SelectItem`;
- sample Preview;
- return public WPF focus to the Timeline;
- sample Preview again.

Separately, the probe extends the Timeline with a far Item, changes CurrentFrame/selection without scrolling, then calls public `ScrollFrame(farFrame)` and records the real Timeline ScrollViewer offsets before/after.

Desktop pixel sampling and private MainViewModel traversal are **lab instrumentation only**. They are not proposed product APIs.

## PASS boundary

`PASS_NAVIGATION_HANDOFF_OBSERVATION` means the actual Tool received a Timeline, real plugin/timeline focus transitions were observed, the Preview pixel probe was attempted, and the public ScrollFrame viewport probe completed.

The result records independently:

- whether the pixel fixture was usable;
- whether direct plugin-focus navigation already updated Preview;
- whether Timeline focus changed the Preview to the target frame;
- whether ScrollFrame changed a real Timeline viewport offset;
- whether ScrollFrame changed CurrentFrame;
- public visibility/viewport/frame surface candidates.

A negative result is still a valid host observation.

## NOT PROVEN

- other YMM4 versions;
- arbitrary hardware/GPU/DPI/theme combinations;
- product shortcut routing;
- private preview-refresh APIs;
- coordinate-derived viewport/layer formulas;
- end-user UX.
