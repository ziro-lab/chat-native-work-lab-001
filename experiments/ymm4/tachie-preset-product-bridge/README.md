# Tachie Preset product bridge P0

## Goal

Answer one bounded downstream question for Template Placer PR #19:

Can YMM4 4.55.1.1 Lite support the exact product bridge needed by the experimental Tachie Preset source without private editor state, UI Automation, global input hooks, Timeline probing, or background access to mutable host/plugin objects?

This child experiment starts from the already-green generic capability survey in Lab PR #58. It does not repeat that broad survey.

## Environment

- Windows GitHub-hosted runner
- .NET 10
- YMM4 4.55.1.1 Lite
- YMM4 ZIP SHA256: 125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b
- parent evidence: Lab PR #58 / source 621cff8199c6fe6daf54aa5ccd7c44e35e3c0ce7 / run 35488799463

Fixtures intentionally reuse the redistribution-safe setup from PR #58:
- synthetic Animation Tachie preset.ini + generated 1px PNGs
- psd-tools 1layer.psd from pinned commit 5fe6781f32c8d9af39ed9a3786934dd1c26ce212
- synthetic YMM4 PSD preset metadata

## Assertions

P0 passes only when all of these are true in the exact host:

1. A Character whose TachieType and TachieCharacterParameter are configured can be resolved to exactly one current PluginLoader.TachiePlugins entry by exact runtime type, preserving the Character's current parameter object.
2. A real public YMM4 ItemProperty can be constructed through public surface and supplied to the modern PropertyEditorAttribute2 SetBindings(FrameworkElement, ItemProperty[]) route; the bound synthetic editor actually mutates its fresh FaceParameter.
3. For built-in Animation Tachie and PSD Tachie, a bounded canonical fingerprint over public FaceParameter state is deterministic, changes when the selected preset changes semantic state, and is identical on two fresh FaceParameters after applying the same fixture preset.
4. The applied fresh FaceParameter can be attached to a fresh TachieFaceItem for the Character and read back with the same bounded fingerprint.
5. Temporary editor binding/staging cleanup is proved on successful application, an intentional post-bind failure, and cancellation before candidate application; ClearBindings succeeds and the bounded staging host returns to its prior child count.

All editor/plugin work runs on the YMM4 UI thread. No Timeline mutation is performed.

## PASS means

PASS_TACHIE_PRESET_PRODUCT_BRIDGE_P0 means the exact YMM4 4.55.1.1 Lite host exposes a bounded product route for the five P0 gaps above, for the built-in Animation/PSD fixtures plus the modern ItemProperty synthetic binding check.

It is sufficient to unblock Template Placer PR #19 P1 implementation under its frozen boundaries.

## NOT PROVEN

This experiment does not prove:
- compatibility with every third-party Tachie plugin;
- future YMM4 versions;
- project-file serialization roundtrip of a TachieFaceItem;
- Preview perceptual correctness;
- product performance or cancellation behavior after the full coordinator is implemented;
- any private/internal editor contract.

Those remain downstream product acceptance or later host checks when relevant.

## Reproduction

Run the GitHub Actions workflow named YMM4 Tachie Preset product bridge P0.

## Evidence

The workflow emits result.json, report.txt, fixture.json, host-log.txt and provenance.json, then uploads them as one artifact. Provenance records source HEAD, actual checkout SHA/tree, workflow run, exact host and result marker.


## Child extension — expression-item preset surface

This child experiment tightens the abstraction boundary from plugin/Character preset definitions to the
actual expression item edited by the user in YMM4.

For built-in Animation and PSD tachie it additionally proves that:

1. `new TachieFaceItem(character)` creates an item-owned FaceParameter;
2. two fresh expression items do not share the Character default or each other;
3. the item-owned FaceParameter exposes the same public expression-preset PropertyEditor choices;
4. choosing the named preset mutates only the item-owned face state;
5. the Character default face remains unchanged;
6. two fresh items selecting the same candidate converge to the same bounded state fingerprint;
7. the applied state stays attached to each TachieFaceItem after editor cleanup.

A green child run emits `PASS_EXPRESSION_ITEM_PRESET_SURFACE`.

This does not by itself prove that every third-party plugin exposes its expression-item preset selector through
the same public PropertyEditor contract. A real external plugin binary remains a separate compatibility observation.
