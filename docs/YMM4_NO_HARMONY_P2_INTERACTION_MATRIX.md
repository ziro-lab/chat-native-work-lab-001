# YMM4 no-Harmony Full — P2 Interaction Matrix v0.1

Status: **primary-host interaction matrix; secondary-host V2 freeze pending**.

This matrix consolidates existing P0/P1 evidence with the new P2 navigation evidence so P2 does not re-prove already-frozen mechanisms.

## Classification

| Interaction route | Classification | Evidence / decision |
| --- | --- | --- |
| normal folded item click | native-safe under folded display | P0 Track C #72, both pinned hosts |
| right-click cursor -> standard add position | mapped through FoldMap/FolderLayout | P0 Track C #72, both pinned hosts |
| Character/File/Generic Item/Template/Voice standard add-position routes | common mapped mechanism | #61 observed all five converter subclasses under `AddItemCommandParameterConverterBase`; #72 proves corrected standard converter route |
| real FileDrop | mapped through FoldMap + native AddFileItem | #72, both pinned hosts |
| Shift marquee | mapped/filtered folded interaction | #72, both pinned hosts |
| native single-item drag | GestureLease candidate / FoldMap correction | #72 and P1 #82, both pinned hosts |
| native same-row multi-item drag | same shared gesture mechanism | #72, both pinned hosts |
| standard Add/Delete/MoveUp/MoveDown layers | structural observer -> FolderRangeTracker | P1 #82, both pinned hosts |
| single-item host selection change | shared fold-aware navigation | #84 V1 GREEN on 4.56.1.0 |
| product-owned item navigation | shared `NavigateTo(IItem)` route | #84 V1 GREEN on 4.56.1.0 |
| bare/same-item `TimelineViewModel.ScrollToItem` without selection notification | **not fold-aware / intentionally not guessed** | #84 raw observation: logical viewport offsets, target remains invisible; no selection signal |
| `ScrollToLowerLayer` / `ScrollToHigherLayer` | native-safe candidate | #84 V1 GREEN, including L1..L10 folded-boundary 32 -> 64 -> 32 |
| real foreground Down/Up layer scroll | native-safe candidate | #84 V1 GREEN, same folded-boundary 32 -> 64 -> 32 |
| horizontal frame follow / seek | no fold-specific vertical adaptation | #57 public CurrentFrame/ScrollFrame/Preview Seek evidence; P2 adds no vertical ownership |
| arbitrary special/grouped/multi-layer item implementations | deferred to compatibility equivalence classes | P5, unless a distinct interaction mechanism appears |
| folder collapse-state persistence / restore / migration | not an interaction-route question | P3 |
| collapse auto-expansion UX / user-visible history policy | not frozen by P2 | P3/P4 |

## Standard add-route convergence

PR #61 observed these concrete converter subclasses:

- `AddCharacterItemCommandParameterConverter`;
- `AddFileItemCommandParameterConverter`;
- `AddItemCommandParameterConverter`;
- `AddTemplateItemCommandParameterConverter`;
- `AddVoiceItemCommandParameterConverter`.

They share the same `AddItemCommandParameterConverterBase.GetTimelinePosition(object[])` coordinate family. P0 Track C later proves the right-click cursor plus actual standard add-position converter is corrected through the common FolderLayout mapping on both pinned hosts.

Therefore P2 does **not** run one native matrix per item-add type. A newly discovered add route needs a new P2 gate only when it bypasses this converter family and FileDrop's separately-proven route.

## ScrollToItem boundary

Raw `ScrollToItem` on the folded layout remains intentionally outside the automatic observer boundary when it produces no selection notification.

On 4.56.1.0:

- hidden L4: viewport Y `0 -> 96`, target remains invisible;
- visible logical L45: viewport Y `0 -> 1408`, target still misses the folded viewport;
- same-item call emits no new `SelectedItems` signal.

Viewport movement alone is not a safe discriminator because ordinary user scrolling can produce the same observable changes. Do not add a heuristic viewport interceptor merely to widen this route.

Supported routes are:

1. host actions that change single selection and therefore enter the selection observer;
2. product-owned navigation that explicitly calls the shared `NavigateTo(IItem)` boundary.

Unsupported bare calls fail visually, not structurally: they do not mutate item coordinates or folder metadata.

## P2 exit interpretation

The generic interaction mechanisms are now covered without a Cartesian item-type matrix.

Before P2 freeze:

- run the new P2 navigation acceptance on YMM4 4.55.1.1 as the secondary pinned host;
- preserve the already-green 4.56.1.0 result;
- if the secondary host agrees, freeze the navigation classifications above;
- do not replay P0/P1 golden suites because their shared mechanisms were not changed.

If a future host route reveals a genuinely new interaction mechanism, reopen only that row.
