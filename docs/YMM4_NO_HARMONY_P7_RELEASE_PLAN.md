# YMM4 no-Harmony Full — P7 Release Gate Plan

作成日: 2026-09-23  
状態: **ACTIVE / RELEASE CANDIDATE PREP**

P6 is frozen at:

- `docs/YMM4_NO_HARMONY_P6_HARDENING_FREEZE.md`
- accepted runtime source `4d9fae9a8b96d069d6d2b6923bc33f00035967eb`
- final P6 evidence source `ea0feed7b06c7b9fa172c62163fe41486654e54b`

P7 adds no new folder feature. It converts the frozen product into one identifiable distributable release candidate and proves that the **same package bytes** work on both supported hosts.

## 1. Release identity policy

Initial public release version:

`0.1.0`

User-facing product name:

`レイヤーフォルダ (no-Harmony)`

Internal compatibility identifiers remain unchanged for the first release:

- assembly: `Ymm4NoHarmonyFolderHandsOn.dll`;
- install/root folder: `Ymm4NoHarmonyFolderHandsOn`;
- Tool type identity remains unchanged.

Reason:

> Renaming the assembly/plugin installation identity at the release boundary can create side-by-side duplicate plugin loads or disconnect existing ToolState association. P7 prioritizes upgrade compatibility over cleaning up the historical internal `HandsOn` identifier.

The public `.ymme` filename may use a release-facing name without changing the internal root.

Canonical release filename:

`YMM4_LayerFolder_noHarmony_v0.1.0.ymme`

## 2. Release package contents

The canonical package must contain exactly the intended release payload under the existing install root:

- `Ymm4NoHarmonyFolderHandsOn.dll`;
- `README.md`;
- `LICENSE`;
- `THIRD_PARTY_NOTICES.md`.

Do not package:

- P6 hardening source/probe files;
- Lab evidence;
- screenshots;
- YMM4 binaries;
- Harmony;
- P4 hands-on/candidate documents.

The repository license is Apache-2.0. YMM4 remains third-party software and is not redistributed by this package.

## 3. Release README contract

The release README must document:

- what the plugin does;
- no-Harmony implementation claim;
- tested YMM4 versions: 4.55.1.1 Lite / 4.56.1.0 Lite;
- install;
- upgrade identity;
- uninstall;
- major folder / panel / visual-summary capabilities;
- project persistence/schema behavior;
- recovery behavior for unreadable/newer state;
- critical unavailable-plugin warning;
- compatibility scope;
- known navigation limitation;
- backup recommendation.

Critical warning:

> If the plugin is unavailable and a project containing folder metadata is opened and then re-saved, YMM4 may drop that absent plugin's ToolState. The YMM4 project remains usable, but folder metadata can be lost. Keep the previous project file / backup before uninstalling or opening important projects without the plugin.

Known navigation boundary:

> Bare host `TimelineViewModel.ScrollToItem` calls that produce no selection signal are not fold-aware. Selection-driven and plugin-owned navigation routes are supported. The unsupported route is a visual navigation limitation and does not mutate folder/item state.

## 4. P7 gates

### P7.1 — metadata / documentation / package layout

- version = 0.1.0;
- release-facing Product / Description;
- internal assembly identity preserved;
- release README replaces P4 candidate README;
- LICENSE / THIRD_PARTY_NOTICES included;
- legacy P4 HANDS_ON document removed from packaged payload;
- release filename fixed.

### P7.2 — canonical one-package build

Build **once** against the oldest pinned supported host:

- YMM4 4.55.1.1 Lite.

Reason:

> The canonical release must be one binary that loads on both supported hosts. Building the first attempt against 4.56.1.0 produced a package that installed on 4.55.1.1 but did not attach after restart. The package itself was intact; the failure was the backward runtime load boundary. P7 therefore treats the oldest supported host as the compile-time compatibility floor and validates the exact same bytes forward on 4.56.1.0.

Record:

- source HEAD/tree;
- canonical DLL SHA256;
- canonical .ymme SHA256;
- exact package file list;
- no Harmony;
- no P6 hardening compilation.

Upload the canonical `.ymme` and expected DLL as one workflow artifact.

### P7.3 — same-byte dual-host install

Two host jobs download the artifact from P7.2.

Both jobs must use:

- the same canonical `.ymme` SHA256;
- the same canonical DLL SHA256.

Test unchanged on:

- YMM4 4.55.1.1 Lite;
- YMM4 4.56.1.0 Lite.

Require:

- real .ymme install;
- installed DLL == canonical DLL;
- startup/attach;
- Harmony files = 0.

No rebuilding inside the host matrix.

### P7.4 — uninstall / absent-plugin safety

After canonical install/startup:

- stop YMM4;
- remove `user/plugin/Ymm4NoHarmonyFolderHandsOn`;
- confirm plugin DLL is absent;
- launch YMM4 without the plugin and confirm the host reaches a stable main window/process state.

This does not widen the P3 unavailable-plugin persistence claim. The README must retain the warning that re-saving a project while the plugin is absent may lose folder metadata.

### P7.5 — release contract validation

Machine-check source/package documentation for:

- version;
- supported hosts;
- install/uninstall sections;
- Apache-2.0 license presence;
- recovery warning;
- known ScrollToItem limitation;
- package file allowlist;
- no candidate/manual-P4 wording in packaged README;
- no P6 PASS/stress instrumentation strings in canonical release DLL where practical;
- no Harmony file/reference in package.

### P7.6 — final freeze

Record:

- accepted release source;
- one canonical package SHA;
- one canonical DLL SHA;
- build artifact ID/digest;
- both-host install evidence IDs/digests;
- uninstall evidence;
- final known limitations;
- final release status.

## 5. Reuse of frozen evidence

P7 does not replay every discovery workflow.

Runtime behavior is already frozen through P6, including:

- Track C / structural regression after the P6 subscription fix;
- P5 built-in/media/Voice/third-party compatibility;
- P6 density/history/lifecycle/failure soak.

P7 reopens runtime behavior only if release packaging/metadata changes a runtime-relevant surface.

## 6. Exit

P7 is complete only when one canonical release package, without rebuild, passes both supported hosts and the package/documentation/recovery contracts above.

At that point the no-Harmony Full candidate is release-ready.
