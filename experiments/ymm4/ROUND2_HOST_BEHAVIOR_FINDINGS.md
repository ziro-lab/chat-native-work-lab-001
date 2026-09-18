# YMM4 4.55.1.1 Host Behavior — Round 2 Findings

This report records the Round 2 public-lab observations used by Template Placer context-palette design.

## Fixed environment

- YMM4: **4.55.1.1 Lite**
- OS: GitHub-hosted Windows
- Runtime: .NET 10
- YMM4 ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- PR: #47
- Tested source head for all three reference runs: `9eca8374be42e86b8d905a1999a70a0e345eb2d7`
- Reference evidence:
  - L1 Tool group: run **35367666297**, artifact **10557201726**, SHA256 `32c19045b2f2aa63fb6ccc73aa418fc711087eba19771f04727df05d77fa5e71`
  - L2 Preview refresh: run **35367666289**, artifact **10556713160**, SHA256 `efa97e31639c66886a449e653e8759cc662cab1c08f8989afb74331f16ce356a`
  - L3-L5 Timeline input intent: run **35367666398**, artifact **10557226813**, SHA256 `bff069eee257782169ef4a53141911ced3c09e0bacb3d84883d2cccbb8508e2e`

The input tests use OS-injected mouse/keyboard input against the real YMM4 window. They are automated input, not a human hand.

---

## L1 — Tool group resolution

### Result

**Use YMM4's localized resource, not a literal group string:**

```csharp
public string DefaultGroupName =>
    YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
```

Reference run 35367666297 observed on the real 4.55.1.1 host:

```text
status=PASS_TOOL_GROUP_OBSERVED
culture=en-US
CNWL_Group_English_path=Utilities > CNWL Group English
CNWL_Group_Japanese_path=ユーティリティ > CNWL Group Japanese
CNWL_Group_Default_path=CNWL Group Default
CNWL_Group_Resource_path=Utilities > CNWL Group Resource
resource_value=Utilities
resource_parent=Utilities
resource_matches_existing_english_parent=True
```

The actual built-in Utilities group contained Explorer, Browser, Plugin Portal and Notepad. A literal `Utilities` joined it in this en-US host, while literal `ユーティリティ` created a separate group.

The built-in Community plugin source also uses the same localized resource for Explorer, Browser, Plugin Portal and Notepad:

- `YukkuriMovieMaker.Plugin.Community/Tool/Explorer/ExplorerToolPlugin.cs`
- `YukkuriMovieMaker.Plugin.Community/Tool/Browser/BrowserTool.cs`
- `YukkuriMovieMaker.Plugin.Community/Tool/PluginPortal/PluginPortalTool.cs`
- `YukkuriMovieMaker.Plugin.Community/Tool/Notepad/NotepadToolPlugin.cs`

Observed source revision: `ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa`.

### Implication

Do not hard-code either English or Japanese display text. Returning `Texts.ToolGroupUtilityName` follows the same mechanism as the built-in tools and localizes with YMM4.

---

## L2 — Preview refresh surface

### Result

No dedicated forced-preview-refresh route was found on the Timeline Tool plugin-facing public surface.

Reference run 35367666289:

```text
status=PASS_PREVIEW_REFRESH_OBSERVATION
plugin_dedicated_preview_refresh_candidate_count=0
preview_vm_found=False
timeline_vm_scrollframe_public=True
timeline_vm_scrollframe_moves_currentframe=False
scrollframe_before_currentframe=22
scrollframe_after_currentframe=22
preview_property_changed_total=0
preview_property_changed_during_direct_currentframe=0
final_current_frame=22
```

Direct writes to `Timeline.CurrentFrame` produced `Timeline.PropertyChanged("CurrentFrame")`.

`TimelineViewModel.ScrollFrame(int)` is **not** a seek/refresh substitute: calling `ScrollFrame(33)` left `CurrentFrame` at 22.

The assembly-wide public semantic scan found rendering/seek/update members for sources, item-editor render helpers, effects and file/audio/video components, but no established public plugin-facing instance route meaning “force the current YMM4 preview to redraw now”.

### Important boundary

This experiment did **not** prove pixel-level preview repaint after a direct `CurrentFrame` write. It proves the public surface and event behavior only.

### Implication

For a Timeline Tool, `Timeline.CurrentFrame` is the supported public state change found here. Do not rely on `ScrollFrame` or an unrelated rendering helper as a forced preview refresh. If the visible preview can remain stale after a public CurrentFrame write, Round 2 found no separate supported public redraw command to append.

---

## L3/L4 — Item click, blank click and selected-item re-click

### Item click ordering

Reference run 35367666398 observed:

```text
PreviewMouseDown source=TimelineItemView
-> Timeline PropertyChanging SelectedItems
-> Timeline PropertyChanged SelectedItems
-> MouseDown / MouseUp
```

The hit route contained:

```text
TimelineItemView[dc=TimelineItemViewModel]
<- Control[dc=TimelineItemViewModel]
<- FastCanvasItemsControl[dc=TimelineViewModel]
...
```

### Selected-item re-click

A re-click of the already-selected VoiceItem was observable and produced selection notifications again:

```text
item_reclick_pointer_route_observed=True
item_reclick_source_is_timeline_item=True
item_reclick_selection_changed_event=True
item_reclick_selection_value_same=True
```

So on exact YMM4 4.55.1.1, “same selected Item clicked again” is **not silent** at the Timeline notification level: selection notifications re-fire even while the effective selected Item/count remain unchanged.

### Blank Timeline click

A blank Timeline click had a different input source and moved CurrentFrame while retaining the selection:

```text
blank_pointer_route_observed=True
blank_currentframe_changed=True
blank_source_is_timeline_item=False
```

Observed trace:

```text
PreviewMouseDown source=Grid[dc=TimelineViewModel]
-> CurrentFrame 0 -> 471
-> selected VoiceItem remains selected
```

### Implication

Do not use “selection became empty” to recognize a generic Timeline click. Blank time clicks can preserve the selected Item.

---

## L5 — Ruler, drag, keyboard and playback variants

Reference run 35367666398:

```text
ruler_pointer_route_observed=True
ruler_source_is_timeline_scale=True
ruler_currentframe_changed=True
ruler_drag_currentframe_changed=True
keyboard_right_frame_changed=True
space_playback_frame_moved=True
```

Ruler click:

```text
PreviewMouseDown source=Canvas[dc=TimelineScaleViewModel]
-> CurrentFrame 471 -> 250
```

Ruler drag:

```text
PreviewMouseDown source=...TimelineScaleViewModel
-> CurrentFrame 250 -> 270 -> 290 -> 310 -> 330 -> 350 -> 370
```

Keyboard Right also changed CurrentFrame.

Space playback generated repeated CurrentFrame changes (reference run: 378 -> ... -> 455).

### Implication

`CurrentFrameChanged` alone cannot mean “the user clicked time”. It is also generated by keyboard navigation and playback.

For a context palette, the cleanest Round 2 behavior model is:

| User action | Suggested palette intent |
| --- | --- |
| Pointer route hits `TimelineItemView` | Item-context Set |
| Pointer route hits blank Timeline background | Generic Set |
| Pointer route hits `TimelineScale` / ruler and changes time | Generic Set |
| Playback moves CurrentFrame | Keep current Set |
| Keyboard moves CurrentFrame | Keep current Set unless explicitly designed otherwise |
| Programmatic CurrentFrame change | Keep current Set |

---

## Stability / API boundary

The event plumbing used by the input probe is public WPF / public Timeline notification surface:

- `InputManager.Current.PreProcessInput`
- routed `PreviewMouseDown` / `MouseDown`
- `Timeline.PropertyChanging` / `Timeline.PropertyChanged`

However, interpreting the pointer target by YMM4 visual-tree types/data contexts such as `TimelineItemView`, `TimelineItemViewModel`, `TimelineScaleViewModel` and `TimelineViewModel` is **version-specific host structure**, not a documented semantic Plugin API.

Therefore a production implementation should fail safe:

- recognize the known 4.55.1.1 routes,
- do not auto-switch the palette when the pointer source cannot be classified,
- keep the selection/property-only fallback for host versions whose visual tree changes.

---

## Round 2 answer in one line

The context-palette idea is viable on 4.55.1.1: Item intent and time-pointer intent are distinguishable, selected-item re-click is observable, and playback can be excluded by pointer-origin gating. The two caveats are that preview has no separate public forced-redraw route found here, and YMM4 visual-tree classification is version-sensitive.
