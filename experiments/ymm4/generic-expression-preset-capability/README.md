# Generic expression preset capability experiment

## Question

Can Template Placer discover and apply expression presets without a per-plugin allowlist by recognizing common YMM4 tachie patterns?

The experiment focuses on three routes:

1. Direct named preset: a face parameter exposes a writable Preset string and the character side exposes preset definitions.
2. YMM4 property-editor route: a face-parameter property is decorated with PropertyEditorForTachieParameterAttribute whose property or attribute name indicates Preset. The probe creates the same editor contract YMM4 uses, supplies the character parameter, binds it to a fresh face parameter, enumerates choices, and changes selection.
3. Resolved/expanded state: a preset editor writes several concrete face-parameter properties rather than retaining the preset name.

These patterns are intentionally structural. The detector does not branch on a third-party plugin assembly name.

## Why this experiment

A private user project showed two real storage shapes side by side: one tachie implementation retained a preset name on its face parameter, while YMM4 built-in PSD tachie stored the resolved FilePath and enabled-layer state. No private project data is committed here.

A public third-party tool, bluemistel/YMM4EmotionMekerKIT, independently handles YMM4 movable-tachie preset.ini and PSD -ymm.json as two forms of the same expression-preset concept. The official YMM4 plugin sample also shows that custom tachie property editors derive from PropertyEditorForTachieParameterAttribute.

## Exact host

- YMM4 4.55.1.1 Lite
- Windows / .NET 10
- YMM4 release ZIP SHA256 125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b

## Fixtures

- movable tachie: synthetic preset.ini plus tiny generated PNGs
- PSD tachie: psd-tools tests/psd_files/1layer.psd fetched from pinned psd-tools commit 5fe6781f32c8d9af39ed9a3786934dd1c26ce212, with a synthetic YMM4 -ymm.json beside it
- synthetic face-parameter classes exercise direct-name, editor-expanded, noise and throwing-editor shapes

Third-party fixture binaries are downloaded during CI and are not committed.

## PASS boundary

PASS_GENERIC_EXPRESSION_PRESET_CAPABILITY_SURVEY means:

- the real YMM4 host exposed the tachie plugins and the common property-editor contract;
- built-in preset-capable tachie implementations were discovered by structure rather than assembly allowlist;
- synthetic direct-name and editor-expanded unknown-plugin shapes were detected and applied;
- a Preset-looking unrelated property was not promoted to a usable capability;
- a throwing candidate was isolated without aborting the survey.

The survey separately reports whether each built-in editor produced choices and whether a generic selection actually mutated a fresh face parameter. Failure of one built-in fixture path is recorded as NOT PROVEN for that route, not silently upgraded to PASS.

## Downstream intent

An experimental Template Placer feature may expose medium-confidence candidates with an Experimental label. It should apply a preset only to a fresh uncommitted TachieFaceItem, preserve Undo, and let the user verify Preview. Compatibility can be remembered by a fingerprint containing YMM4 version plus tachie plugin type, face-parameter type and module MVID.
