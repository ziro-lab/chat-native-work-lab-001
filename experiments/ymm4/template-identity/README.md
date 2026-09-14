# YMM4 ItemTemplate Identity — Experiment 005

## Goal

Determine what a plugin-owned Library can safely store to re-identify a live YMM4 `ItemTemplate` without creating a second template database or assuming a nonexistent unique Template ID.

## Environment

- GitHub-hosted Windows runner
- real YMM4 4.55.1.1 Lite process
- .NET 10
- pinned YMM4 archive SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- synthetic redistribution-safe Templates only

## Discovery result

`ItemTemplate` exposes exactly one public readable `Guid` property in the tested host:

```text
SceneId : Guid
```

`ItemSettings` exposes public live Template collections and `Save()`:

```text
Templates
SortedTemplates
Save()
```

Two live Templates with the same Name can coexist.

## Restart ambiguity proof

The behavioral phase deliberately created two different Templates with:

```text
Name    = CNWL Persist Same
SceneId = 11111111-2222-3333-4444-555555555555
Path    = same value
```

The source item contents differed only enough to prove that two distinct records survived:

```text
Template A item Length = 21
Template B item Length = 22
```

The probe called `ItemSettings.Default.Save()`, terminated YMM4, launched the same temporary YMM4 installation again, and re-read `ItemSettings.Default.Templates`.

Native result:

```text
status=PASS_TEMPLATE_IDENTITY_RESTART_AMBIGUITY
read_count=2
same_name_after_restart=True
same_scene_id_after_restart=True
same_path_after_restart=True
content_recovered=True
only_public_guid_property_is_scene_id=True
lengths=21,22
```

Therefore `SceneId` is not a unique ItemTemplate identity, and even the obvious locator fields Name + Path + SceneId are not API-enforced unique keys.

## Product implication

Template Placer v0.4 should keep its own stable `LibraryEntryId` for Plugin settings, but it must treat the YMM4 side as a **locator**, not as a guaranteed identity.

A practical source locator may retain exact source metadata such as:

```text
Name
Path
Group
SceneId
```

Resolution rule:

```text
exact candidates == 1
-> resolved

exact candidates == 0
-> missing source
-> show broken Library entry
-> manual relink / remove

exact candidates > 1
-> ambiguous source
-> do not guess
-> manual relink / remove
```

No fuzzy-name fallback should be used. Plugin DisplayName remains independent from the source YMM4 Template name.

Because exact duplicate locators are possible, users who need persistence across restarts should keep their YMM4 source Template locator unique in practice (normally by its YMM4 Name / Path organization). The Plugin should detect ambiguity rather than silently choose one.

The Plugin still stores references/metadata only; it does not copy the YMM4 Template body into a second database.

## NOT PROVEN

- behavior after a source Template is renamed or moved; design intentionally treats an unresolved locator as requiring relink
- whether future YMM4 versions introduce a dedicated Template ID
- compatibility with YMM4 versions other than 4.55.1.1 Lite

## Evidence

Audited successful restart run:

- workflow run: `34834213261`
- source head: `934148d8ab612b0b2e6794f51768107b4153f058`
- result: `PASS_TEMPLATE_IDENTITY_RESTART_AMBIGUITY`
- artifact ID: `10343732270`
- artifact ZIP SHA256: `e225884aa2a56e8de64bda5d8cef25f6c7e4b20fc00d25f94bcc628dbf8015d1`

The final documentation head must reproduce the same restart proof before merge.
