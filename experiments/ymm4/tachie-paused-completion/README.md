# Stopped preparation: same-frame assignment and one-frame round trip

Question: after a synthetic Tachie preparation completes, does the public `Timeline.CurrentFrame` setter refresh a real stopped player when assigned the same value, or when moved by one frame and immediately restored in one UI turn?

The earlier one-owner-parameter-notice experiment completed a valid negative on 4.56.1.0 at source `294edc936369e451c76920906494dffd2c9218c7`, [run 36879610296](https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/36879610296). Those artifacts remain separate evidence. This revision tests frame actions, not another parameter notice.

## Public route and limits

The official 4.56.1.0 public `Timeline.CurrentFrame` setter's inspected IL returns immediately for an equal value, without PropertyChanged. For a different value it updates the field and emits CurrentFrame PropertyChanged. This static fact does not establish player refresh. The public TimelineViewModel.ScrollFrame method scrolls the viewport; it is not used as Seek. PreviewViewModel has SeekAsync methods but is an internal type; no such method is invoked here. The experiment only assigns the plugin-facing public Timeline property obtained from TimelineToolInfo.

## Protocol

1. Resolve the current official stable Lite host during execution and record version, asset/EXE hash, actual source checkout HEAD, event SHA, run and attempt. Build only this independent probe and pure guard checks. No host binary is published.
2. Seed a normal synthetic project with a 300-frame Tachie item and generated green PNG. Every subsequent case opens it through the ordinary executable argument in a separate pristine host copy; no imported settings are carried between cases.
3. Establish the real paused Source, match its exact owner parameter to the live Timeline item, and obtain the real public Preview host surface. For end cases, set frame Length-1 before baseline and wait for a real Paused host Update at that frame. The generated fixture is red before readiness.
4. A read-only tool and all setup occur before baseline. The runner requires three seconds with one enabled main window and no visible popup before granting baseline permission; the in-process observer independently checks native/WPF enabled state and visible dialogs. Known association No and the exactly matched future-association information OK are allowed only before this permission. Unknown warning/terms/Confirm gets no response. After permission, any popup blocks without an action.
5. Arm the owner once. CPU preparation delays two seconds and decodes the generated green PNG on a worker. After ready, the owner Dispatcher captures the then-current Timeline frame. The same case assigns that same value once. Nudge cases choose +1, or -1 at the last frame, and restore immediately without an await/yield between writes. No parameter notice, fake edit, Play, command, direct host Update, private Seek or player hook is used.
6. Only real host Update can apply the green GPU input. Independently record callback frame/Usage, GPU application and screen pixels. Sample for approximately nine seconds from arming (roughly seven after readiness), requiring original frame/paused state, unchanged selected items, native Timeline serialization, persisted parameter JSON, input project bytes and measured Undo state. Record the synchronous frame action duration.

Cases run sequentially: control-start, same-start, nudge-start, control-end, nudge-end, nudge-error-start. A failed/blocked control stops dependent cases. If same-frame assignment refreshes, nudge is unnecessary and is not entered. The error case injects an exception after the temporary move and verifies restoration on the actual Timeline property. No UI frame navigation is done in control cases after baseline.

## Guard boundary

`FrameRefresh` executes once on the owner UI thread after readiness. It uses the completion-time frame, same Timeline identity, navigation-event version, stopped state, current owner membership and active Source membership. It refuses old-position restoration after reentrant navigation, Scene identity change, Play or disposal. There is no retry or notification loop. A one-frame Timeline has no valid neighboring frame and is skipped.

Pure checks exercise start/end, completion-time position, no neighbor, injected post-move error, user movement and return to the temporary target, Scene/Play/disposal model states, and failure of the restoration setter itself. These are adversarial state/fault simulations, not live user input or live Scene/Play evidence. A persistent restoration setter failure cannot guarantee position restoration; it is reported as RESTORE_FAILED and must not be hidden as a safe result or adopted without a separate error policy. The live injected error tests the earlier failure point, not a broken restoration setter.

## Verdict and NOT PROVEN

Each valid control must stay red after ready. Action cases report PASS_FRAME_ACTION_REPAINT only when actual pixels become green with final frame/paused state and measured invariants intact; otherwise OBSERVED_NO_REPAINT means a valid negative phase. Overall VALID_NEGATIVE_FRAME_ACTIONS is a completed negative observation, not a redraw PASS. BLOCKED or failed invariants never support adoption. Callback events and pixels are separate evidence; two CurrentFrame notifications alone prove neither Seek nor repaint.

The synthetic project has no audio tracks. Audio/click effects are not measured. At 250-ms sampling, very brief flicker or playhead-cursor flicker is not proven absent. Live user input/Scene switching/Play races and a live dirty flag are not proven by guard simulations, window title or unchanged final serialization. Physical GPU performance, real PSD compatibility, saving/reopening edits and export are outside this probe. Product integration remains a later decision.

## Artifacts

Only the literal identity/result/startup/permission JSON, minimal plugin/dialog/build logs, pure guard JSON and synthetic preview crops are uploaded. No host ZIP/DLL/settings, generated project/material, arbitrary directories, user assets or downstream private code are uploaded. Phase startup JSON distinguishes native process exit/code before cleanup from deadline with a live host; constructor, culture, Dispatcher, project loading and Source connection are separate stages. All phase EXE copies must match the official EXE hash.
