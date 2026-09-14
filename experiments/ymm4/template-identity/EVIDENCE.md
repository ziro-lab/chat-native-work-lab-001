# Evidence — YMM4 ItemTemplate Identity

Audited native restart run:

- workflow run: `34834213261`
- source head: `934148d8ab612b0b2e6794f51768107b4153f058`
- host: YMM4 4.55.1.1 Lite
- host ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- result: `PASS_TEMPLATE_IDENTITY_RESTART_AMBIGUITY`
- artifact ID: `10343732270`
- artifact ZIP SHA256: `e225884aa2a56e8de64bda5d8cef25f6c7e4b20fc00d25f94bcc628dbf8015d1`

Native assertions:

```text
write_count=2
same_name=True
same_scene_id=True
same_path=True
lengths=21,22
read_count=2
same_name_after_restart=True
same_scene_id_after_restart=True
same_path_after_restart=True
content_recovered=True
only_public_guid_property_is_scene_id=True
```

This proves that the tested YMM4 API does not enforce uniqueness for the obvious public ItemTemplate locator fields. `SceneId` must not be treated as a Template ID.

Product consequence: resolve saved source metadata only when it matches exactly one live YMM4 Template. Zero or multiple candidates are an unresolved Library entry requiring explicit relink/removal; no fuzzy fallback.
