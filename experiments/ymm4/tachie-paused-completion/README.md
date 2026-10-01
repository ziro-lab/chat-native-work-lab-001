# Stopped real-player Tachie preparation notification

Question: Does one ordinary owner-parameter PropertyChanged notification after asynchronous preparation cause an already stopped **real YMM4 player** to call the Tachie source and visibly replace a red synthetic preview with green, without any other interaction?

This is a new native automated observation, not the older preview-refresh event-only observation. It is independent public Lab code with generated synthetic material. It contains no downstream product code, parser, user material or third-party binaries.

## Protocol

The workflow resolves the **current official stable Lite release at execution time**, records its release tag, exact executable/file version, ZIP and EXE SHA256, source checkout HEAD and run ID, and builds this probe against those official assemblies. Known 4.56.1.0 ZIP also has an expected-hash check.

1. A seed host generates an ordinary `.ymmp` containing one synthetic Tachie item and an opaque green PNG. Both stay in the runner work directory, outside the upload allowlist.
2. Two separate host processes open that saved project through the ordinary executable project argument. One is the notification-free control; the other emits a single parameter notification. Neither process uses detached Scene/factory rendering as substitute evidence.
3. The real Tachie source begins with an opaque red bitmap and one stable D2D output. A read-only observer Tool is opened through its regular public menu command **before baseline**, obtaining public TimelineToolInfo. The harness requires that its live Timeline item owns the exact parameter received by the Source. It waits for actual source Update traffic plus a **red screen-pixel baseline** from the public PreviewViewModel.Host WinForms surface. It independently reads Timeline.CurrentFrame through ToolInfo and public Preview.IsPlaying, requiring a stopped player.
4. Only after that baseline, a probe-owned Task delays two seconds and reads/decodes the generated green PNG on the CPU. In the notification case it dispatches **one** protected `OnPropertyChanged(nameof(File))` through normal Tachie parameter inheritance on the owner Dispatcher. It never changes a parameter value, creates an Undo point or simulates an edit.
5. Only a subsequent **host-invoked** ITachieSource2.Update may replace the GPU input. The completion worker/harness cannot call host Update or touch the GPU.
6. For nine seconds, the observer reads state and takes GDI desktop captures of the preview element. There is no seek, playback, selection, command, fake value edit or direct Update after preparation is armed. GDI capture does not call WPF RenderTargetBitmap/host rendering. Counts of host Update/usage/position and GPU-input application are recorded separately from observed pixels.

The capture uses the central half of the preview surface, with red/green dominance thresholds. Baseline and final **synthetic preview crops only** are uploaded; no desktop/window screenshot is uploaded. Black/unavailable capture, unknown surface, absent real source or unknown stopped/frame state is BLOCKED. No detached success is substituted.

## Verdicts

- `PASS_CAUSAL_PAUSED_REPAINT`: notification case reaches green at the same frame while stopped; no-notification control stays red; signal count and measured parameter/Undo invariants hold.
- `VALID_NEGATIVE_NO_REPAINT`: both stay red despite completed preparation, exactly one notification in the notification case, stable frame/paused state and measured invariants. The experiment executed successfully but **the notification redraw hypothesis is not established**. A green workflow for this verdict is a completed negative observation, not H-A1 PASS.
- `INCONCLUSIVE_OR_FAILED`: control also repaints, invalid invariants or other non-causal results. It must not support adoption.
- `BLOCKED`: cannot establish real-player/visible pixels/state/host boundary, black capture, unexpected consent/dialog or runtime failure. It never supports a rendering conclusion.

Phase JSON contains host callback events and pixel samples so source Update, GPU application and actual preview color can be assessed separately. Windows may refresh the screen for unrelated reasons; the identical control protocol is therefore required.

## Undo and saved-state boundary

Probe parameter UndoRedoCommandCreated count and serialized persisted parameter JSON are measured before/after the completion window. Public TimelineToolInfo.UndoRedoManager history events and IsUndoable/IsRedoable are measured as well. The public window title is observed for changes. A dedicated live dirty flag is not accessed and is explicitly false in JSON. These invariants do not prove every saved-marker behavior or every Undo-stack entry.

The probe uses bounded public-property reading on known preview DataContexts discovered through the public WPF visual tree, and public ToolMenuItems/Command to open its own observer before baseline. This is version-sensitive **observation instrumentation**, not a supported product integration contract. Reflection is restricted to BindingFlags.Public properties; ICommand executes normally. No non-public getter/field/method, reflected setter, private-player hook, Harmony or host patch is used. EndEdit or a fake BeginEdit/SetValue/EndEdit operation is not used to force repaint.

## Dialogs and artifacts

Only known informational About/update windows may receive Close before baseline; a popup during observation blocks instead of being closed. The recorded 4.56.1.0 file-association prompt may be **declined with its exact No button only before baseline**, after public UI Automation text matches the full purpose and all three `.ymmp/.ymmt/.ymme` extension descriptions. This does not accept an association, license or permission. The action and strict match are logged. No generic Enter/accept action is sent to Confirm, terms, security or permission dialogs. Other consent requires a separate decision: BLOCKED with public accessible text recorded. Unavailable UI Automation is recorded rather than installing software or bypassing the dialog.

Artifact upload is a literal allowlist: host identity JSON, minimal build/dialog-decision logs, summary/phase JSON and four synthetic preview PNG paths. ZIPs, DLLs, generated `.ymmp`, material PNG, host settings, personal files and arbitrary directories are excluded. Runtime files are kept only on the ephemeral runner. The dedicated branch/Draft PR must not contain the older unpublished Lab branch or product code.

## Reproduction and evidence identity

Workflow: `.github/workflows/ymm4-tachie-paused-completion.yml` (pull_request or explicit workflow_dispatch). It checks out the PR source HEAD, not a synthetic merge commit, and records both checkout and event SHA. Local API build is useful but is **not** live-player evidence. `RunNative.ps1` requires a dedicated official host directory plus separate work/evidence directories; never use an everyday installation.

Observe exact run/commit and downloaded evidence before recording a result here. A build-only success or a source-ready signal does not prove a stopped preview changed.
