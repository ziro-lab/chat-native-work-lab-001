# YMM4 no-Harmony Full — P7 Release Gate Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN — v0.1.0 RELEASE-READY**

P7の目的は、P0-P6で凍結した製品を、ひとつの識別可能な配布物へ収束させ、その**同一package bytes**をサポート対象YMM4の両Hostでinstall/startup/uninstall確認することだった。

P7では新しいfolder機能を追加していない。

## 1. Release identity

Public version:

`0.1.0`

User-facing name:

`レイヤーフォルダ (no-Harmony)`

Canonical public package filename:

`YMM4_LayerFolder_noHarmony_v0.1.0.ymme`

Upgrade / ToolState compatibilityのため、最初のreleaseでは内部識別子を維持する。

- assembly / DLL: `Ymm4NoHarmonyFolderHandsOn.dll`
- install root: `user\plugin\Ymm4NoHarmonyFolderHandsOn\`
- Tool type identity: unchanged

P7 accepted release source:

`83305edf47059ee4d707176c53e33d75d36e724c`

Branch:

`work/ymm4-no-harmony-p7-release`

PR:

- #126 `p7: prepare canonical no-Harmony layer-folder release`

## 2. Canonical build policy

Final workflow:

`35866123710`

The canonical release is built **once** against the oldest supported host:

- YMM4 4.55.1.1 Lite
- host ZIP SHA256:
  `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

Reason:

The first P7 attempt built the canonical DLL against 4.56.1.0. That package installed on 4.55.1.1, but the plugin did not attach after restart. The same package worked on 4.56.1.0.

P7 therefore uses 4.55.1.1 as the compile-time compatibility floor and validates the exact same bytes forward on 4.56.1.0.

No host-matrix rebuild is allowed in the accepted gate.

## 3. Canonical artifact identity

Canonical DLL SHA256:

`aedc719d9147ec1dde0d1ee760d85ff7072d65eefe3c4aa64b482d69ac39a3d7`

Canonical .ymme SHA256:

`1cf501c85ef85333991d1120a0a4a9d6ec107bdd77acbe16ddbfbb118952b8d0`

Assembly version:

`0.1.0.0`

Canonical package contents:

- `LICENSE`
- `README.md`
- `THIRD_PARTY_NOTICES.md`
- `Ymm4NoHarmonyFolderHandsOn.dll`

Not packaged:

- P6 hardening sources/probes
- Lab evidence
- YMM4 binaries
- Harmony / HarmonyLib
- P4 hands-on document

Canonical workflow artifact:

- ID `10752374238`
- name `ymm4-layer-folder-v0.1.0-canonical`
- artifact ZIP digest:
  `sha256:eb5222df8dc7cc28fa4f4c5f9f6c47c3329748651688e305e7d5ab7e3060e96c`

The artifact ZIP digest is not the public .ymme digest. The public .ymme identity is the SHA256 above.

## 4. Same-byte dual-host acceptance

Both host jobs downloaded the artifact produced by the single build job.

They did **not** rebuild the plugin.

### YMM4 4.55.1.1 Lite

Result:

- real .ymme install: PASS
- startup / Timeline attach: PASS
- installed package SHA == canonical package SHA
- installed DLL SHA == canonical DLL SHA
- Harmony files installed = 0
- uninstall: plugin DLL removed
- startup after uninstall: PASS
- `PASS_P7_SAME_BYTE_HOST`

Evidence artifact:

- ID `10752424470`
- digest:
  `sha256:bd994ea9efe34d13b397f83178b0fdddaabad25f1c31e2a8d70f9fc27469747f`

### YMM4 4.56.1.0 Lite

Result:

- real .ymme install: PASS
- startup / Timeline attach: PASS
- installed package SHA == canonical package SHA
- installed DLL SHA == canonical DLL SHA
- Harmony files installed = 0
- uninstall: plugin DLL removed
- startup after uninstall: PASS
- `PASS_P7_SAME_BYTE_HOST`

Evidence artifact:

- ID `10751494099`
- digest:
  `sha256:469cd14b5a57ae44917efea4bf845811c1399f9df92ce33418974013fb18cfc7`

Both hosts accepted:

- canonical .ymme:
  `1cf501c85ef85333991d1120a0a4a9d6ec107bdd77acbe16ddbfbb118952b8d0`
- canonical DLL:
  `aedc719d9147ec1dde0d1ee760d85ff7072d65eefe3c4aa64b482d69ac39a3d7`
- matrix rebuild: **false**

## 5. Final release marker

Release-gate job:

- `PASS_P7_CANONICAL_RELEASE`
- release version = `0.1.0`
- same package tested on = `4.55.1.1,4.56.1.0`
- no Harmony = true

Release-gate artifact:

- ID `10751953508`
- digest:
  `sha256:0d4067600c66ca82ef53cb5132759e3a65833294b921f846f9b6423302dafdf6`

## 6. Same-HEAD regression

At accepted release source `83305edf47059ee4d707176c53e33d75d36e724c`, all triggered release-relevant regressions were GREEN:

- P7 canonical release
- P6 density stress
- P5 common built-ins
- P5 configured Voice
- P5 real media
- P5 third-party item
- S0 integration
- S1 integration
- S2 integration
- S3 integration
- S4 integration
- S5 coordinate discovery
- S5 integration
- P4 hands-on candidate
- P4 .ymme package

No release metadata/package change invalidated the frozen runtime contracts.

## 7. Release documentation contract

The package README documents:

- purpose and no-Harmony implementation;
- tested hosts: 4.55.1.1 Lite / 4.56.1.0 Lite;
- .ymme and manual install;
- internal upgrade identity;
- uninstall;
- primary folder/panel/visual-summary behavior;
- Undo/Redo behavior;
- product schema v2 persistence;
- unreadable/newer-state preservation and edit lock;
- unavailable-plugin/re-save metadata-loss warning;
- representative built-in/media/Voice/third-party compatibility;
- bare `TimelineViewModel.ScrollToItem` navigation limitation;
- bounded hardening scope;
- Apache License 2.0 and third-party notices.

## 8. Known release limitations

### Bare ScrollToItem navigation

A host `TimelineViewModel.ScrollToItem` call that occurs without the selection signal used by the supported navigation path is not fold-aware.

This is a visual navigation limitation. It does not mutate item coordinates or folder state.

### Plugin absent during project re-save

If the plugin is completely unavailable and a project containing folder ToolState is opened and then re-saved, YMM4 may omit the absent plugin's ToolState.

The YMM4 project remains usable, but folder metadata can be lost.

The release README therefore requires a backup before uninstalling or opening/re-saving important projects without the plugin.

### Compatibility scope

The release is validated on:

- YMM4 4.55.1.1 Lite
- YMM4 4.56.1.0 Lite

Other versions may work but are not part of this release's verified support matrix.

## 9. Exit decision

P7 Release Gate is **COMPLETE / FROZEN**.

P0-P7 are all frozen.

`レイヤーフォルダ (no-Harmony) v0.1.0` is **release-ready** with one canonical .ymme identity accepted unchanged on both supported hosts.

This freeze does **not** publish a GitHub Release, merge the stacked PR chain, or otherwise distribute the package. Publication/merge remains an explicit operator action.
