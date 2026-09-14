# YMM4 Playhead Quick Drop — Experiment 003

## Goal

Prove whether a real YMM4 Timeline Tool can obtain the current playhead frame through a public host surface and place a registered Item Template clone at that frame while preserving the Template's intrinsic Length.

Target product flow:

```text
Palette entry double-click
-> read current playhead frame
-> clone live YMM4 Template item
-> Frame = playhead
-> Length = Template intrinsic Length
-> add to Timeline
```

## Environment

- GitHub-hosted Windows runner
- real YMM4 4.55.1.1 Lite process
- .NET 10
- pinned YMM4 SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- synthetic Character / TachieFaceItem / ItemTemplate only

## Proven public host surface

The `Timeline` delivered through `TimelineToolInfo` exposes a public read/write property:

```text
Timeline.CurrentFrame : Int32
```

The native proof changed it from `0` to `321` through that public property and immediately read back `321`.

The same real-host run registered a synthetic live ItemTemplate whose source Face item was:

```text
Frame=17
Length=37
Layer=12
```

It cloned that source independently, set only the clone Frame to the public playhead (`321`), and added the clone to the real Timeline.

Observed result:

```text
status=PASS_PLAYHEAD_QUICK_DROP
public_frame_property=CurrentFrame
before_frame=0
playhead_frame=321
clone_frame=321
clone_length=37
clone_layer=12
independent_clone=True
placed=True
frame_matches=True
length_preserved=True
source_unchanged=True
```

## Product implication

Template Placer v0.4 Quick Drop can use the Timeline already supplied to its Timeline Tool:

```text
var frame = timeline.CurrentFrame;
var clone = templateItem.GetClone();
clone.Frame = frame;
// keep Template intrinsic Length
// resolve Layer separately
// add through the normal atomic Timeline/Undo path
```

No private Timeline ViewModel access is required to read the current playhead on the pinned YMM4 version.

## NOT PROVEN

- physical mouse movement of the playhead; the proof drives the public Timeline property directly
- Palette double-click UI event itself
- Front/Base/Back layer resolution (separate Experiment 004)
- real PSD rendering
- other YMM4 versions

## Evidence

Audited successful behavioral run before this documentation update:

- workflow run: `34833809202`
- source head: `d1a9ecf53ba7feabc9ddcb4742bf0113dc0271c9`
- artifact ID: `10342743206`
- artifact ZIP SHA256: `0b062155d523d9069f9dbb34afdbc479053fca7c16541a421083b2bc10017dbd`

The final documentation/head must reproduce `PASS_PLAYHEAD_QUICK_DROP` before merge.
