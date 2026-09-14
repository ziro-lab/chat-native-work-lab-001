# Evidence — YMM4 Timeline Selection Context

## Audited successful behavioral run

- Repository: `ziro-lab/chat-native-work-lab-001`
- Experiment: `ymm4-timeline-selection-context-002`
- Workflow run: `34832664863`
- Source head: `6f0eff7b1b7b9d639af3a2767751fd5d6db674ab`
- Host: YMM4 4.55.1.1 Lite
- Host ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- Result: `PASS_TIMELINE_SELECTION`
- Artifact ID: `10342384882`
- Artifact ZIP SHA256: `28fc8732d23167fe872ef1fadb0efe35ae8e20b44a796883427ac4dccdbc7a1d`

## Native assertions observed

```text
status=PASS_TIMELINE_SELECTION
voice_selected=True
face_selected=True
selection_clear=True
character_readable=True
selection_event_observed=True
property_changed_names=SelectedItems,SelectedItem,GroupedItems,SelectedAndGroupedItems
```

Synthetic fixture:

```text
VoiceItem character=CNWL_SelectA frame=120 length=40 layer=10
TachieFaceItem character=CNWL_SelectA frame=120 length=40 layer=20
```

Public host surface observed:

```text
TimelineToolInfo.Timeline : YukkuriMovieMaker.Project.Timeline
Timeline.SelectedItem      : IItem
Timeline.SelectedItems     : ImmutableList<IItem>
```

`Timeline.SelectedItems` was publicly writable in the tested host and changing it drove the public selection state. `Timeline` also emitted `PropertyChanged` notifications including `SelectedItems` and `SelectedItem`.

## What this supports

Template Placer v0.4 can implement temporary Character Palette context switching using only the Timeline already provided to `ITimelineToolViewModel`:

```text
Timeline.PropertyChanged
-> inspect SelectedItems
-> exactly one VoiceItem / TachieFaceItem
   -> read Character
   -> temporary Character Palette override
-> selection clear or unsupported selection
   -> remove override
   -> restore manual palette
```

No polling or private Timeline ViewModel reflection is required for this feature on the pinned YMM4 version.

## Boundary

The proof programmatically changes the public Timeline selection model inside the real YMM4 process. It does not inject a physical mouse click. Therefore it proves the Plugin-facing selection state and notification behavior, not desktop input automation.

Playhead access, Quick Drop, layer ordering and Template identity are intentionally outside this experiment.
