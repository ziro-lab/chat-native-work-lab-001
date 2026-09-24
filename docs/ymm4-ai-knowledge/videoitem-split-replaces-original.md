# VideoItem split replaces the original object

- Status: canonical
- Knowledge class: LAB-NATIVE + NEGATIVE-FINDING
- Surface: S1
- YMM4 version: 4.56.1.0 Lite
- Revalidation trigger: YMM4 changes Timeline split semantics or VideoItem source-coordinate handling

## Claim

On the tested YMM4 4.56.1.0 Lite host, splitting a `VideoItem` through the public Timeline split surface replaced the original object.

Neither resulting piece was the original object reference.

For tested constant positive rates 50%, 100% and 200%:

- two new VideoItems partitioned the timeline interval;
- FilePath, Layer, Remark and PlaybackRate2 were preserved;
- the left piece retained the original ContentOffset;
- the right piece advanced ContentOffset by the source time consumed before the cut.

## Safe use

Do not use a VideoItem object reference as the durable authority for analysis/candidate lineage across normal split edits.

For source-based workflows, rebind against the current Timeline using source identity/source-time information and the current item's source mapping.

## Do not infer

- This card does not cover variable, zero, reverse or non-monotonic playback rates.
- It does not prove physical keyboard/mouse split gestures.
- It does not prove every selection mode or general Undo/Redo behavior.
- It does not establish durable occurrence identity after copy/paste.

## Failure behavior

When the original item reference disappears, rescan/rebind from current Timeline state. Do not assume one split piece must preserve the original reference.

## Evidence

- Lab experiment: [videoitem-split-lifecycle](../../experiments/ymm4/videoitem-split-lifecycle/)
- Host ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Source head: `1041e9300ce134c23359dcfa3d4aba8512881aa6`
- Workflow run: `35359881285`
- Artifact: `10554237891`
- Artifact SHA256: `2d77175964a67675b1b53fc83274b93f9317678b84194cb1c4efa423b41ecd3f`
