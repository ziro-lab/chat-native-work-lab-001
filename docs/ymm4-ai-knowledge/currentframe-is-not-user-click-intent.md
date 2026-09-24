# CurrentFrame change alone is not user time-click intent

- Status: evidence-qualified
- Repository state: main
- Knowledge class: LAB-NATIVE + NEGATIVE-FINDING
- Surface: S2 with version-sensitive host structure for pointer classification
- YMM4 version: 4.55.1.1 Lite
- Revalidation trigger: YMM4 changes Timeline input routing or visual-tree structure

## Claim

On the tested YMM4 4.55.1.1 Lite host, `CurrentFrame` changed from multiple distinct causes:

- blank Timeline pointer click;
- ruler click/drag;
- keyboard Right navigation;
- Space-key playback.

Therefore `CurrentFrameChanged` alone cannot mean "the user clicked time."

The same experiment also observed that clicking an already-selected item re-fired selection notifications even when the effective selected item/count stayed the same.

## Safe use

For UI that must distinguish item intent from time-pointer intent, combine public Timeline state with pointer-origin information rather than deriving intent from CurrentFrame changes alone.

Keep playback/programmatic/keyboard frame movement from automatically masquerading as a pointer-context change unless the product explicitly wants that behavior.

## Do not infer

- YMM4 visual-tree type names are not a documented semantic Plugin API.
- A pointer-classification implementation validated on 4.55.1.1 is not automatically stable across future versions.
- Empty selection is not a reliable proxy for a blank Timeline click; the tested blank click retained selection.

## Failure behavior

When a pointer source cannot be classified on an unfamiliar host version, keep the current context rather than guessing and switching UI state.

## Evidence

- Lab report: [Round 2 host behavior findings](../../experiments/ymm4/ROUND2_HOST_BEHAVIOR_FINDINGS.md#l3l4--item-click-blank-click-and-selected-item-re-click)
- Workflow run: `35367666398`
- Artifact: `10557226813`
- Artifact SHA256: `bff069eee257782169ef4a53141911ced3c09e0bacb3d84883d2cccbb8508e2e`
