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
| Voice / speech | discovery pending | resource/config dependent | Pending | Pending | Voice resource availability must not be faked as compatibility |
| Text-like | discovery pending | pending | Pending | Pending | Use actual shipped item type, not guessed class name |
| Image | discovery pending | public item/file route | Pending | Pending | Existing real PNG FileDrop is related evidence, not full P5 classification |
| Video | discovery pending | media fixture | Pending | Pending | Exact test media can be generated in CI if host route is public |
| Audio | discovery pending | media fixture | Pending | Pending | Same |
| Shape | discovery pending | public shape/item route | Pending | Pending | Prefer built-in shape with no external asset |
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

## 5. Exact-host discovery gate

Before selecting fixtures, enumerate concrete `IItem` types on both pinned hosts and record:

- full type name;
- assembly;
- abstract/sealed status;
- public constructors;
- whether a public parameterless constructor exists;
- direct/base interfaces relevant to `IItem`;
- public Frame / Length / Layer properties;
- host-version additions/removals.

The discovery result is metadata evidence only. It does not count as runtime compatibility for that item family.

## 6. Exit gate

P5 is complete when:

- representative built-in families are classified;
- both pinned hosts have a compatibility matrix;
- Group/special behavior has at least one representative exact-host route;
- one reasonable third-party item path is either confirmed or explicitly unavailable with a documented reason;
- unsupported/resource-missing cases fail without folder-state corruption;
- no new duplicate state/Undo/FoldMap ownership was introduced.
