# YMM4 no-Harmony Full — S2 Extended State / Visual Metadata Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

S2は、P3の `FolderDocument v1` を変更せず、その外側に製品状態schema v2を追加し、Folder Color / Hidden / visibility restoreを同じProject-owned ToolStateへ載せた。

## 1. Accepted runtime source

`748e76adb805c6a4b2f39504845cd7b364370b66`

P0-P3のCore契約は変更していない。

## 2. 保存形式

外側schema:

- `schemaVersion = 2`
- `core` — 既存 `FolderDocument v1`
- `folderOptions` — `(TimelineKey, FolderId) -> Color / Hidden`
- `visibilityRestore` — Timelineごとの `Layer -> 元のVisible`

外側にはStart/End、親子関係、FoldMap、表示座標を複製しない。

### Migration / downgrade safety

- v1 ToolStateを読むと、既存P3 codecでcoreを読み、Color / Hidden / restoreは既定値としてv2 stateへ移行する。
- 次回保存時はv2として保存する。
- 旧v1 codecへv2を渡すと `UnsupportedVersion` になり、「読めたつもり」で追加情報を落とさない。
- malformed / unknown schemaはP3と同じくruntime編集をblockし、元raw文字列をbyte-for-byteで保持する。

Pure run `35764572535`:

- **PASS_S2_EXTENDED_STATE**
- **29/29 PASS**

pureで確認済み:

- v1 -> v2 migration;
- old v1 codec rejection of outer v2;
- v2 exact roundtrip;
- Color / Hidden;
- nested Hidden union;
- restore map;
- insert/delete structural remap;
- orphan option cleanup;
- unknown raw preservation;
- malformed/invalid rejection.

## 3. Runtime ownership

状態の正本は引き続き1つ。

`FolderStateStore -> FolderPersistenceSession -> FolderProductState`

既存利用側は `state.Document` を `ProductState.Core` として読めるため、S0/S1のCore利用を分岐させていない。

Core編集では `ReplaceCore` が生存FolderIdを基準にoptionを整理し、Timeline消滅時のrestoreも整理する。

## 4. Color

共通 `FolderCommands` へ追加:

- `SetColor`;
- `ApplyFolderColorToLayers`.

TimelineメニューはLayerPatan基準の8色＋既定を持つ。

Folder Color変更とYMM4 Layer Color適用は別操作。

- Color変更だけではYMM4 layer色を書き換えない。
- 明示的な「フォルダの色を YMM4 のレイヤー色にする」でのみhost色を変更する。
- native color mutationはYMM4のUndoRedoManagerに1回 `Record()` して戻す。

## 5. Hidden / visibility restore

折り畳みとHiddenは別機能。

`FolderVisibilityCoordinator` が唯一のhost visibility所有者。

ルール:

- 非表示理由はHidden folder区間の和から毎回導出する。
- 最初にプラグインが隠すときだけ元のVisibleを1件保存する。
- 親子二重Hiddenでもrestore値を重複保存しない。
- 一方のHiddenを解除しても別のHidden理由があれば隠れたまま。
- 最後のHidden理由が消えたとき元のVisibleへ戻す。
- 元から非表示のレイヤーは非表示へ戻す。
- YMM4側でhidden中のレイヤーを明示的に表示した場合、再度falseへ奪い返さず、その値を解除後の希望値としてrestoreへ反映する。
- native eye Undo/Redo後もDispatcher settle後の最終native値へrestore metadataが追従する。

内部Hide書込みと外部eye変更を同じ変更として扱わない。

## 6. Structural integration

visibility restore用の別structural observerは作っていない。

標準Add/Delete/Move時は、S0の `FolderRangeTracker` が既に持つ同じLayer写像でrestore keyをremapする。

構造変更後に:

- Hidden理由が消えたLayerはrestore;
- Hidden folder内に新規Layerが入った場合は、そのLayerのnative初期値を一度だけ採取してHiddenへ合わせる。

これも `FolderVisibilityCoordinator` へ収束している。

## 7. Exact-host S2 acceptance

Run:

`35764572630`

### YMM4 4.56.1.0 Lite

Job `106870723777` — **PASS_S2_INTEGRATION**

Candidate DLL SHA256:

`fa907fb1be8d50effa3037f9d5e0dddf7427627957192b0cb0072efc574622da`

Artifact:

- ID `10712175127`
- digest `sha256:3abd7740115a722659eca66a23c174f7db26441ff7fb0d746c43335369e8c217`

### YMM4 4.55.1.1 Lite

Job `106870723993` — **PASS_S2_INTEGRATION**

Candidate DLL SHA256:

`a76ee1ff81b64fda4ac0862a827b5c7456ef4f2da54e8c60d35637900479b0d3`

Artifact:

- ID `10711820457`
- digest `sha256:687a029a37977f39989923c21e700ff2f5502b46dc9f9863c0c7ca70dd251537`

両hostで確認:

- nested parent/child Hidden;
- original per-layer visibility restoration;
- external YMM4 eye override;
- external eye Undo/Redo;
- folder Hidden Undo/Redo;
- Folder Color Undo/Redo;
- YMM4 Layer Color apply Undo/Redo;
- v2 ToolArea SavedState exact roundtrip;
- v2 runtime reload.

## 8. Regression / package

Same accepted runtime source:

- S0 integration run `35764572752` — **success**;
- S1 integration run `35764572766` — **success**;
- P4 hands-on candidate run `35764572573` — **success**;
- real `.ymme` run `35764572559` — **success**.

Real `.ymme`:

- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`;
- `PASS_P4_YMME_PACKAGE`;
- installed DLL SHA256 matches `fa907fb1be8d50effa3037f9d5e0dddf7427627957192b0cb0072efc574622da`;
- package SHA256 `a5d964bd87831a370815609c5c1dce2e4e73465c42602d6789261f0e6c4fde5e`;
- no Harmony.

## 9. S2 exit

S2 is **COMPLETE / FROZEN**.

Global preferenceはS2機能に必須の新規項目が無いため、SettingsBase用の設定は増やしていない。

次は **S3 structural convenience + Group Control**:

- folder末尾/内部への明示的Layer追加;
- folder内容ごとの破壊削除;
- Group Control追加;
- Fit GroupRange;
- GroupRange warning / structural auto-correction.

S3もFolderCommands / YmmHostAccess /既存native history所有権を維持し、第二の構造エンジンを作らない。
