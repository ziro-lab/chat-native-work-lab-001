# YMM4 no-Harmony Full — P5 Compatibility Matrix

Status: **ACTIVE / DISCOVERY**

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
| Voice / speech | `VoiceItem` | public ctor; runtime viability next | Metadata confirmed | Metadata confirmed | Parameterless + Character ctor exist on both hosts |
| Text-like | `TextItem` | public parameterless ctor | Metadata confirmed | Metadata confirmed | Also has VoiceItem-copy ctor |
| Image | `ImageItem` | parameterless / string file / real FileDrop | Metadata confirmed | Metadata confirmed | Existing PNG FileDrop is related evidence, not full P5 classification |
| Video | `VideoItem` | parameterless / string file | Metadata confirmed | Metadata confirmed | Media-backed runtime route still needs fixture |
| Audio | `AudioItem` | parameterless / string file | Metadata confirmed | Metadata confirmed | Media-backed runtime route still needs fixture |
| Shape | `ShapeItem` | public parameterless ctor | Metadata confirmed | Metadata confirmed | Strong zero-resource fixture candidate |
| Group Control | `GroupItem` | public item construction | **P4 confirmed** | **P4 confirmed** | P5 may reuse as baseline/special-item control |
| Effect-bearing item | discovery pending | one representative item + effect | Pending | Pending | Effect itself need not become a folder-owned model |
| Transition / special multi-layer | discovery pending | pending | Pending | Pending | Only test if a concrete shipped item uses distinct layer/visual semantics |
| Third-party item | candidate pending | opt-in redistribution-safe plugin | Pending | Pending | One reasonable plugin implementation is enough for P5 representative coverage |

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

## 6. Exit gate

P5 is complete when:

- representative built-in families are classified;
- both pinned hosts have a compatibility matrix;
- Group/special behavior has at least one representative exact-host route;
- one reasonable third-party item path is either confirmed or explicitly unavailable with a documented reason;
- unsupported/resource-missing cases fail without folder-state corruption;
- no new duplicate state/Undo/FoldMap ownership was introduced.
