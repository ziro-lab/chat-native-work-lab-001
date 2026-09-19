# YMM4 Timeline Navigation Handoff

## Question

On exact YMM4 4.55.1.1 Lite, after a Timeline Tool performs public `Timeline.CurrentFrame + SelectItem` navigation while plugin UI owns keyboard focus:

1. does returning WPF keyboard focus to the real Timeline make the visible Preview catch up, and
2. does public `TimelineViewModel.ScrollFrame(int)` move the Timeline viewport without changing the playhead?

The experiment also inventories public TimelineViewModel visibility/viewport/frame members so a product can decide whether "follow only when off-screen" is supportable without hand-written coordinate formulas.

## Method

A synthetic 4-second video is generated from YMM4's bundled FFmpeg: red for the first 2 seconds and green for the next 2 seconds. The probe places it in the real Timeline and samples the center of the real Preview region directly from the desktop pixels.

Sequence:

- Timeline focused, frame 30 -> establish red Preview fixture;
- plugin Tool button focused;
- public `CurrentFrame=target` + `SelectItem`;
- sample the whole visible Preview region;
- perform one real Timeline Item click and record both Preview pixels and the actual WPF keyboard-focus target;
- reset, repeat the plugin-focus navigation, then programmatically return WPF focus to that exact observed target and sample Preview again.

Separately, the probe extends the Timeline with a far Item and uses public `ContainFrameInViewport(int)` before/after direct navigation and public `ScrollFrame(farFrame)`. This tests off-screen detection and viewport following semantically, without screen-coordinate math.

Desktop pixel sampling and private MainViewModel traversal are **lab instrumentation only**. They are not proposed product APIs.

## PASS boundary

`PASS_NAVIGATION_HANDOFF_OBSERVATION` means the actual Tool received a Timeline, real plugin/timeline focus transitions were observed, the Preview pixel probe was attempted, and the public ScrollFrame viewport probe completed.

The result records independently:

- whether the pixel fixture was usable;
- whether direct plugin-focus navigation already updated Preview;
- whether a real Timeline click changed the Preview without changing CurrentFrame;
- which WPF element actually received focus after that real click;
- whether programmatically focusing that exact observed target reproduces the Preview update;
- whether `ContainFrameInViewport` reports the far frame outside before ScrollFrame and inside afterward;
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
