# ToolState SavedState can carry project-specific Tool data across reopen/switch

- Status: evidence-qualified
- Repository state: stacked Draft PR #87
- Knowledge class: LAB-NATIVE
- Surface: S1 / public Tool and project surfaces
- YMM4 version: 4.56.1.0
- Tested source: `0600506b55b379880e4f30c5ef3c958bf6869491`
- Revalidation trigger: YMM4 changes ToolState / ToolArea / project-open lifecycle

## Claim

On the tested YMM4 4.56.1.0 host:

- project-specific plugin data survived in `ToolStates.*.SavedState`;
- two Save-As project files carried different saved Tool documents without cross-contamination;
- public `OpenProject(string)` restored the corresponding host-owned ToolArea state;
- public ToolArea `SaveState()` returned the project-owned state after switching;
- `Timeline.ID : Guid` remained stable through the tested project reopen;
- `ProjectFilePath` change notification was a usable project-switch signal;
- an already-existing plugin inner ViewModel did **not** receive a second `LoadState()` callback during project switch.

The observed switch synchronization route was:

```text
host restores ToolArea state
  -> ProjectFilePath changes
  -> plugin reads public ToolArea.SaveState()
```

## Safe use

For project-owned Tool data, `ToolState.SavedState` is a viable host persistence surface on the tested version.

Do not rely on repeated `LoadState()` callbacks as the project-switch notification mechanism for an already-existing inner ViewModel.

## Do not infer

- This card does not cover new-project initialization.
- It does not cover plugin-unavailable preservation/resave behavior.
- It does not cover malformed-state recovery.
- It does not prove every Tool lifecycle/layout transition.
- It does not prove future YMM4 versions.

## Failure behavior

On project switch, synchronize from the host-owned current ToolArea state using the validated public signals instead of fabricating a second LoadState lifecycle.

## Evidence

- Lab PR: [#87](https://github.com/ziro-lab/chat-native-work-lab-001/pull/87)
- Workflow run: `35710875053`
- Native job: `106691172892`
- Artifact: `10686861509`
- Workflow digest: `sha256:93c68003558eb70383905a222bc07655a0d2a2ac8208054d48d9ab81793c97a6`
- Marker: `PASS_P3_TOOLSTATE_ROUNDTRIP`
