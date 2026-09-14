# YMM4 Timeline Selection Context — Experiment 002

## Goal

Prove whether a YMM4 Timeline Tool can observe the editor's current single-item selection well enough to support context-sensitive character palettes without depending on private Timeline ViewModel state.

Target product flow:

```text
single Timeline selection
-> public Timeline selection state changes
-> plugin identifies VoiceItem / TachieFaceItem
-> plugin reads the item's Character
-> selection clear is observed
-> character palette context can be restored
```

## Environment

- GitHub-hosted Windows runner
- real YMM4 4.55.1.1 Lite process
- .NET 10
- YMM4 archive SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- synthetic redistribution-safe Character / VoiceItem / TachieFaceItem fixtures only

## Proven host surface

A real `ITimelineToolViewModel` receives `TimelineToolInfo`, whose public `Timeline` property exposes the selection state needed by the product.

Observed public `Timeline` surface includes:

```text
SelectedItem  : IItem
SelectedItems : ImmutableList<IItem>
```

The real Timeline also raises `INotifyPropertyChanged.PropertyChanged` notifications for selection changes. The successful native proof observed these names:

```text
SelectedItems
SelectedItem
GroupedItems
SelectedAndGroupedItems
```

No product-side access to private `TimelineViewModel` state is required for the intended Character Palette behavior.

## Behavioral assertions

The native proof inserts two synthetic Items with the same Character:

```text
VoiceItem       Character=CNWL_SelectA Frame=120 Length=40 Layer=10
TachieFaceItem  Character=CNWL_SelectA Frame=120 Length=40 Layer=20
```

PASS requires all of the following inside the real pinned YMM4 process:

1. The real Timeline Tool receives its public `Timeline` through `TimelineToolInfo`.
2. Selecting only the synthetic `VoiceItem` is reflected by public `SelectedItem` / `SelectedItems`.
3. The selected Voice Character is readable as `CNWL_SelectA`.
4. Selecting only the synthetic `TachieFaceItem` is reflected by the same public selection state.
5. The selected Face Character is readable as `CNWL_SelectA`.
6. Clearing the selection yields `SelectedItem == null` and zero `SelectedItems`.
7. Timeline property-change notification is observed for the selection transition.
8. The probe builds with warnings treated as errors.

The required native status is:

```text
PASS_TIMELINE_SELECTION
```

## Product implication

For Template Placer v0.4, Character Palette context switching can use the Timeline already supplied to the Timeline Tool:

```text
SetTimelineToolInfo(info)
-> subscribe to info.Timeline.PropertyChanged
-> react to SelectedItem / SelectedItems
-> exactly one VoiceItem or TachieFaceItem
   -> derive Character and apply temporary Character Palette context
-> otherwise
   -> clear temporary context and restore the previous manual palette
```

A polling loop and private reflection are not required for this behavior on YMM4 4.55.1.1 Lite.

## NOT PROVEN

This experiment does not prove:

- physical mouse/keyboard automation; the proof drives the same public Timeline selection model directly
- Playhead/current-frame access
- Quick Drop placement
- Front/Base/Back layer resolution
- Template identity persistence across restart
- real PSD asset rendering
- compatibility with YMM4 versions other than 4.55.1.1 Lite

Those remain separate experiments.

## Evidence

Audited successful behavioral run before this documentation update:

- workflow run: `34832664863`
- source head: `6f0eff7b1b7b9d639af3a2767751fd5d6db674ab`
- result: `PASS_TIMELINE_SELECTION`
- artifact ID: `10342384882`
- artifact ZIP SHA256: `28fc8732d23167fe872ef1fadb0efe35ae8e20b44a796883427ac4dccdbc7a1d`

The final documentation commit is required to pass the same workflow again before merge.
