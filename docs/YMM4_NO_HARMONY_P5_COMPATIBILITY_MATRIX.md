# YMM4 no-Harmony Full — P5 Compatibility Matrix

Status: **COMPLETE / FROZEN**

P4 Full is frozen at:

- `docs/YMM4_NO_HARMONY_P4_FULL_FREEZE.md`
- runtime acceptance source `4672ffdd06f490a3ce4f9b6f21e51ad2278ae60c`

P5 does not redesign the folder engine. It checks representative YMM4 item/plugin classes against the frozen common path and records the smallest necessary compatibility boundary.

## 1. Classification

Each representative item family receives one of:

- **Confirmed** — representative exact-host item passes the defined compatibility route.
- **Common-path with setup** — frozen engine works, but fixture creation requires media/voice/plugin setup.
- **Graceful fallback** — unsupported specialized surface is skipped without corrupting folder state or native timeline state.
- **Unsupported** — a concrete incompatibility remains and must be documented before release.

Do not add a type-specific adapter merely because setup differs.

## 2. Required checks per representative family

| Check | Meaning |
| --- | --- |
| Add / realize | Item can enter Timeline through a supported/public host route and, when applicable, realize a TimelineItemView |
| Fold geometry | Fold/unfold keeps the item on the correct logical/display row without stale views |
| Timing summary | Hidden item can supply common Timeline item VM `Left / Width` and appears on the collapsed owner summary where applicable |
| Selection | Folder/layer item selection uses the common public selection path |
| Explicit block move | Item Layer follows the shared panel move mapping |
| Undo / Redo | Representative move/edit restores item + FolderProductState in one user-visible history unit |
| Persistence isolation | Item type does not require another folder persistence model |
| Failure behavior | Missing external resource/plugin does not corrupt folder state |

## 3. Target families

| Family | Candidate host type | Fixture route | 4.55.1.1 | 4.56.1.0 | Notes |
| --- | --- | --- | --- | --- | --- |
| Voice / speech | `VoiceItem` | Recorded Voice + deterministic local WAV | **Confirmed** | **Confirmed** | P5.5 configured Voice crosses native history + block move Undo/Redo |
| Text-like | `TextItem` | public parameterless ctor | **Confirmed** | **Confirmed** | Full P5.3 common path + block move Undo/Redo |
| Image | `ImageItem` | real PNG via public `ImageItem(string file)` | **Confirmed** | **Confirmed** | P5.4 real-file add/fold/timing/select/block-move Undo/Redo |
| Video | `VideoItem` | real MP4 via public `VideoItem(string file)` | **Confirmed** | **Confirmed** | P5.4 real-file add/fold/timing/select/block-move Undo/Redo |
| Audio | `AudioItem` | real PCM WAV via public `AudioItem(string file)` | **Confirmed** | **Confirmed** | P5.4 real-file add/fold/timing/select/block-move Undo/Redo |
| Shape | `ShapeItem` | public parameterless ctor | **Confirmed** | **Confirmed** | Full P5.3 common path + block move Undo/Redo |
| Group Control | `GroupItem` | public item construction | **Confirmed** | **Confirmed** | P5.3 common path + block move Undo/Redo; P4 GroupRange-specific behavior also frozen |
| Effect-bearing timeline item | `EffectItem` | public parameterless ctor | **Confirmed** | **Confirmed** | P5.3 common geometry/fold/timing/select + block move Undo/Redo |
| Transition / special timeline item | `TransitionItem` | public parameterless ctor | **Confirmed** | **Confirmed** | P5.3 common geometry/fold/timing/select + block move Undo/Redo |
| Third-party custom item | `YMM43D.Project.Items.LightItem` | pinned MIT YMM43D build | **Confirmed** | **Confirmed** | Commit `a5fe44443d9b62912dec6861edf68cbdff9e7810`; P5.6 common path + Undo/Redo |

## 4. Frozen assumptions that P5 may challenge

P5 is specifically looking for evidence that one of these common assumptions fails for a concrete type:

1. The item implements the common `IItem` Layer/Frame/Length contract.
2. Timeline display geometry is represented by the common Timeline item VM path.
3. Hidden-item timing summary can read public `Left / Width`.
4. Moving the item's logical Layer through the common host operation is sufficient.
5. Selection is exposed through the common Timeline selection path.
6. The item does not require folder-specific persistence.

If an assumption fails, record the exact type and surface first. Do not broaden the architecture preemptively.

## 5. Exact-host discovery gate — COMPLETE

Workflow:

- `35834475296`

Result on both pinned hosts:

- `PASS_P5_ITEM_INVENTORY`;
- total IItem-compatible types = **19**;
- concrete IItem types = **13**;
- no Harmony loaded;
- **the concrete type set and public constructor surfaces are identical on 4.55.1.1 and 4.56.1.0**.

Artifacts:

- 4.55.1.1: `10738468027`, `sha256:3199c95106b99fe09fd6f3adb29a886fa7c0782606bdc2d283bceff23c07f02c`;
- 4.56.1.0: `10738491905`, `sha256:0c199cba3548953a5a499dd8eca172778cb7edcb9ba403b419c5607632198190`.

Concrete built-in types found on both hosts:

| Type | Public parameterless ctor | Additional public ctor |
| --- | --- | --- |
| `AudioItem` | Yes | `AudioItem(string file)` |
| `EffectItem` | Yes | — |
| `FrameBufferItem` | Yes | — |
| `GroupItem` | Yes | — |
| `ImageItem` | Yes | `ImageItem(string file)` |
| `SceneItem` | Yes | — |
| `ShapeItem` | Yes | — |
| `TachieFaceItem` | Yes | `TachieFaceItem(Character)`, `TachieFaceItem(VoiceItem)` |
| `TachieItem` | Yes | `TachieItem(Character)` |
| `TextItem` | Yes | `TextItem(VoiceItem)` |
| `TransitionItem` | Yes | — |
| `VideoItem` | Yes | `VideoItem(string file)` |
| `VoiceItem` | Yes | `VoiceItem(Character)` |

All 13 concrete types expose public `Frame`, `Length`, and `Layer` get/set through the common `BaseItem` surface.

### P5.1 conclusion

The first frozen common-path assumption is strongly supported:

> built-in concrete item classes share one public BaseItem positional contract on both pinned hosts.

This is still metadata evidence, not runtime compatibility. The next gate is zero-resource fixture viability: instantiate each public parameterless type, attempt native Timeline insertion, and classify which families can be exercised without external media/voice/plugin resources.

## 5.2 Zero-resource fixture viability — COMPLETE

Workflow:

- `35835578672`

Both pinned hosts:

- all **13/13** concrete built-in types construct through their public parameterless constructor;
- all **13/13** are accepted by `Timeline.TryAddItems`;
- all **13/13** remain live Timeline items;
- no Harmony loaded.

Artifacts:

- 4.55.1.1: `10738953558`, `sha256:7f25a24cf3a7cfba0f1809149b37e22888f4837864407b5e325f2378ae44d57a`;
- 4.56.1.0: `10739246243`, `sha256:894e45bd4ecdca96caf62dec8494c094fbce65988bc8154309cd8e6335e72eb4`.

The standalone viability probe deliberately does **not** use its VM-count as a type-compatibility verdict. Timeline item VMs are viewport-virtualized; the number present depends on the realized Timeline viewport. That observation is useful host behavior, but it is not an item-family failure.

Common geometry compatibility is therefore accepted only in the P5.3 product-candidate gate below, where the viewport is realized and controlled.

## 5.3 Common built-in product compatibility — COMPLETE

Workflow:

- `35835578837`

Both pinned hosts returned:

- `PASS_P5_COMMON_BUILTINS`;
- concrete built-in types = **13**;
- construct / add / live = **13/13**;
- public Timeline item VM geometry = **13/13**;
- FoldMap owner mapping = **13/13**;
- hidden timing summary = **13/13**;
- folder item selection = **13/13**;
- no item-type-specific folder adapter.

Representative built-in set:

`AudioItem, EffectItem, FrameBufferItem, GroupItem, ImageItem, SceneItem, ShapeItem, TachieFaceItem, TachieItem, TextItem, TransitionItem, VideoItem, VoiceItem`

### History-bearing explicit move

A synthetic parameterless `VoiceItem` without a configured speaker is a known invalid resource fixture for YMM4 history recording. P4 already observed that YMM4 resource refresh can reject it at `UndoRedoManager.Record()`.

P5 therefore separates two claims:

1. **VoiceItem common folder/display path** — confirmed for construct/add/live/common geometry/fold/timing summary/selection.
2. **Configured VoiceItem history path** — still **Common-path with setup** and requires a real configured speaker/voice fixture.

After removing only the synthetic zero-resource Voice fixture, the remaining **12/12 built-in types** passed:

- explicit folder block move;
- shared Layer mapping;
- one-step Undo;
- one-step Redo;
- common geometry after move.

Artifacts:

- 4.55.1.1: `10739685300`, `sha256:7eccc47921180ca12fcfc73e1c8b2b4902da48b5dfc89763a8ed9b23d74837bb`;
- 4.56.1.0: `10738572961`, `sha256:b7320261ec61428d3247278d20b5dd33f7844b4d4e8c6a08e1e4957d0e5ce933`.

### P5.3 conclusion

The frozen P4 common path is valid across every built-in concrete item class for non-resource-specific folder/display operations on both pinned hosts.

No built-in type has yet justified:

- a type-specific FolderDocument;
- a type-specific FoldMap;
- a type-specific structural observer;
- a type-specific Undo path;
- a type-specific timing-coordinate formula.

The remaining built-in work is **resource realism**, not core folder architecture: configured Voice history and media-backed Image/Video/Audio behavior.

## 5.4 Real media-backed compatibility — COMPLETE

Workflow:

- `35844737768`

The workflow creates deterministic local fixtures without downloading media:

- PNG image;
- PCM WAV audio;
- H.264 MP4 video.

Fixture SHA256:

- image: `785090597e739d0ec824dc66223d33f26ae4be8da5fc5711f202d5a41dafffb5`;
- audio: `3fa20276d8a4131431490ed129d0b05fafa4e9c82d4339b5cf635512103d6615`;
- video: `507246f89713a5f008a495371976daf4a4afdf9ed67ec9236b4257f91ffbbb9b`.

Both pinned hosts returned:

- `PASS_P5_MEDIA_REALISM`;
- real-file construction = **3/3**;
- add / live / public geometry = **3/3**;
- FoldMap owner mapping = **3/3**;
- hidden timing summary = **3/3**;
- folder item selection = **3/3**;
- explicit block move + one-step Undo/Redo = **3/3**;
- no item-type-specific folder adapter.

Media-backed types:

- `ImageItem(string file)`;
- `AudioItem(string file)`;
- `VideoItem(string file)`.

Artifacts:

- 4.55.1.1: `10742972187`, `sha256:1cc3ac3a99acb91ef056528e83643a7e90495a97b544a821d43d2e80bee2625b`;
- 4.56.1.0: `10743305164`, `sha256:4247cc21921c02ac8e61c45f193cfea747ebfcc876008d445c4ee0194879cb57`.

### P5.4 media conclusion

The file-backed Image / Audio / Video paths do not require a media-specific folder architecture. Resource realism preserves the same P4 common path that the parameterless fixtures used.

Remaining resource/setup work is now narrowed to configured Voice and any special/third-party plugin semantics that are not represented by the built-in BaseItem path.

## 5.5 Configured Voice compatibility — COMPLETE

Discovery proved on both pinned hosts that:

- `Character.Voice` is public get/set `VoiceDescription`;
- `Character.VoiceParameter` is public get/set `IVoiceParameter`;
- Community `RecordedVoiceSpeaker`, `RecordedVoiceParameter`, and `VoiceDescription(IVoiceSpeaker)` are available with matching surfaces on both hosts;
- `RecordedVoiceParameter.AudioFilePath` and `RecordsDirectory` are public settable.

The configured Voice gate uses YMM4's bundled Community **Recorded Voice** implementation with a deterministic local PCM WAV. No external TTS engine, service, account, or network resource is required.

Workflow:

- `35845819363`

Both pinned hosts returned:

- `PASS_P5_CONFIGURED_VOICE`;
- construct / add / live = **1/1**;
- common public geometry = **1/1**;
- FoldMap owner mapping = **1/1**;
- hidden timing summary = **1/1**;
- folder item selection = **1/1**;
- native history `Record()` boundary = **PASS**;
- explicit block move + one-step Undo/Redo = **1/1**;
- no Voice-specific folder adapter.

Voice WAV SHA256:

`3fa20276d8a4131431490ed129d0b05fafa4e9c82d4339b5cf635512103d6615`

Artifacts:

- 4.55.1.1: `10743666802`, `sha256:82cd6f10ac396f725ae5d493f1b175f004304323a88041513b46a2e760ae0ca3`;
- 4.56.1.0: `10743336836`, `sha256:4b52b127aee2ac28fc57990e7048c6d087efa36bdc844589562bf74368c7b629`.

This closes the only resource-specific built-in history gap left by P5.3.

## 5.6 Third-party custom item compatibility — COMPLETE

Representative third-party plugin:

- repository: `Dolphin-kun/YMM43D`;
- pinned commit: `a5fe44443d9b62912dec6861edf68cbdff9e7810`;
- license: **MIT**;
- representative custom item: `YMM43D.Project.Items.LightItem : BaseItem`.

The workflow fetches the pinned source, verifies the commit and MIT license marker, builds YMM43D against each exact pinned YMM4 host, rejects Harmony in its output, installs it through the ordinary YMM4 plugin path, and then exercises the folder plugin against the loaded custom item.

Workflow:

- `35845819534`

Both pinned hosts returned:

- `PASS_P5_THIRD_PARTY`;
- construct / add / live = **1/1**;
- common public geometry = **1/1**;
- FoldMap owner mapping = **1/1**;
- hidden timing summary = **1/1**;
- folder item selection = **1/1**;
- explicit block move + one-step Undo/Redo = **1/1**;
- no type-specific folder adapter.

YMM43D.dll SHA256:

- 4.55.1.1 build: `f3b3f355d4cea2179af95a84076b6886f677f5ae6761de1647786d820ef4aff5`;
- 4.56.1.0 build: `c8e108ad49dbf5bbfe7debc64d070819c3cd61a0a1c8d99cc458914f47dedcfc`.

Artifacts:

- 4.55.1.1: `10742907908`, `sha256:6c4c0f67ea0bcf151b05922e8c4c732500c83b029e7ecb922603cdb40cb6f2ce`;
- 4.56.1.0: `10743057676`, `sha256:07b962c48b38ba12b098e629148e91a021fc622a8fd9f7b1678f629aa5729cb1`.

### P5 compatibility conclusion

Across the frozen P4 product path:

- every built-in concrete item class shares the common folder/display contract;
- file-backed Image / Audio / Video remain on the common path;
- configured Voice remains on the common path once given a real bundled Recorded Voice configuration;
- special built-ins including Group / Effect / Transition remain on the common path;
- a real third-party custom BaseItem remains on the common path.

No compatibility result justified a new FolderDocument, FoldMap, structural observer, Undo stack, or per-item timing-coordinate formula.

## 6. Exit gate — SATISFIED

P5 exit conditions are satisfied:

- representative built-in families are classified on both pinned hosts;
- all 13 concrete built-in item types pass the frozen common folder/display route;
- 12/12 zero-resource history-valid built-ins pass explicit block move Undo/Redo;
- configured Voice closes the remaining history setup gap;
- real PNG / WAV / MP4 fixtures validate Image / Audio / Video resource realism;
- Group / Effect / Transition provide representative built-in special-item coverage;
- pinned MIT YMM43D `LightItem` provides real third-party custom-item coverage;
- resource/setup limitations were isolated without folder-state corruption;
- no duplicate state / Undo / FoldMap ownership was introduced.

Freeze record:

- `docs/YMM4_NO_HARMONY_P5_COMPATIBILITY_FREEZE.md`

The active completion path advances to **P6 Hardening & Performance**.
