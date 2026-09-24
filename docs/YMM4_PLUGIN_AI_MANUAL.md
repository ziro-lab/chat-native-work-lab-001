# YMM4 Plugin AI Manual

> **AIと一緒に、ゆっくりMovieMaker4（YMM4）のプラグインを作るための配布用マニュアル**
>
> This is an unofficial, evidence-aware development guide for AI-assisted YMM4 plugin development.
>
> **Last curated:** 2026-09-25  
> **Current project baseline:** YMM4 v4.47.0.0+ / .NET 10  
> **Major native evidence currently includes:** YMM4 4.55.1.1 Lite / 4.56.1.0 Lite

## このマニュアルは何？

このマニュアルは、ChatGPT / Codex / Claude などのコーディングAIに **YMM4プラグインを作らせるときの共通土台** です。

目的は「AIに大量のAPI情報を暗記させること」ではありません。

目的は次の4つです。

1. 現在のYMM4向けプロジェクトを正しい土台から始める。
2. 公開API・公式サンプル・実装例・実機観測を混同しない。
3. いきなりReflection/Harmonyへ飛ばず、低リスクな実装面から探す。
4. 分からないYMM4挙動を推測せず、必要なら小さく検証する。

このリポジトリには、そのための **公式資料整理 + 実YMM4での検証Evidence** があります。

---

## 配布するときのおすすめ

### おすすめ: このLabリポジトリごと共有する

一番おすすめです。

共有相手には:

- この `YMM4_PLUGIN_AI_MANUAL.md`
- `YMM4_PLUGIN_AI_PROMPT.md`
- 同じリポジトリ内のP0〜P3 / Knowledge Card / Lab Evidence

をまとめて参照してもらえます。

AIがGitHubを直接読める環境なら、**LabのリポジトリURL + このManualのパス**を渡すのが最も情報量を保てます。

### 軽量: Manual + Promptだけ共有する

この2ファイルだけでも:

- 現在の開発baseline
- surface選択
- S1〜S4
- source conflict rule
- AIが避けるべき推測
- Evidenceの読み方

は利用できます。

ただし、Knowledge Cardの根拠やexact tested SHA/run/artifactまで辿るにはLab本体が必要です。

### コピーして別リポジトリへ置く場合

Manual本文だけをコピーして「YMM4の公式仕様書」として再配布しないでください。

このManualは:

```text
official/reference information
+
ziro-lab Lab evidence
+
implementation guidance
```

を区別して使うこと自体が重要です。

Evidenceへのリンク・version boundary・非公式であることを残してください。

---

## 30秒で使い始める

### 1. AIにこのリポジトリを読ませる

AIがGitHubを参照できる場合は、このリポジトリと本ファイルを指定してください。

最初に読む順番は次です。

1. この `YMM4_PLUGIN_AI_MANUAL.md`
2. [Current Official Development Baseline](YMM4_AI_P0_OFFICIAL_BASELINE.md)
3. [Public and Reference Plugin Surfaces](YMM4_AI_P1_PLUGIN_SURFACES.md)
4. [Official Plugin Surface Map](YMM4_AI_P1B_OFFICIAL_PLUGIN_SURFACE_MAP.md)
5. 必要な場合だけ [Evidence-qualified Host Behavior](YMM4_AI_P2_CANONICAL_HOST_BEHAVIOR.md)
6. 実装時に必要な場合だけ [Implementation Guidance](YMM4_AI_P3_IMPLEMENTATION_GUIDANCE.md)

### 2. AIに作りたいものを普通に説明する

例:

```text
YMM4で、選択したアイテムの開始位置にテンプレートを配置するTool Pluginを作りたい。
YMM4 4.56系を対象にしたい。
できるだけ公開APIを使い、Reflection/Harmonyは必要な場合だけにして。
```

### 3. AIが分からないYMM4挙動を推測し始めたら止める

このLabに既存Evidenceがないか確認してください。

なければ、製品コードへ推測を埋め込む前に **小さい検証** に分離するのが推奨です。

---

## AIに最初に守らせるルール

AIには次を守らせてください。

```text
1. YMM4の現在の公開API・公式サンプルを最初に確認する。
2. 公開Plugin APIでできるなら、それを優先する。
3. 次に公開Host/WPF面を検討する。
4. Reflectionは、必要な正確な型・メンバーだけに限定する。
5. Harmonyや非公開内部パッチは最後の手段にする。
6. YMM4の実行時挙動を、API名だけから推測しない。
7. Lab Evidenceがある場合は、YMM4版・tested SHA・PASS boundaryを守る。
8. Draft PRでもEvidenceが十分なら利用可能。ただしexact tested SHAを参照する。
9. 公式READMEと公式実装コードが食い違う場合は、その不一致を隠さない。
10. 不明な内部挙動はfail closedし、似たprivate memberを推測で呼び出さない。
```

コピー用の短い版は [YMM4 Plugin AI Prompt](YMM4_PLUGIN_AI_PROMPT.md) にあります。

---

# まず知っておくYMM4開発の土台

## 現在のTarget Framework

YMM4 v4.47.0.0以降向けの現在の公式サンプルは:

```xml
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<UseWPF>true</UseWPF>
```

を使用しています。

古い.NET 8/9向け記事・コードを、そのまま現在のYMM4向けプロジェクトへ持ち込まないでください。

詳細:
[Current Official Development Baseline](YMM4_AI_P0_OFFICIAL_BASELINE.md)

## YMM4のDLL参照

まず、作りたいPlugin surfaceに実際に必要なDLLだけを参照します。

現在の公式開発資料/サンプルでは主に:

- `YukkuriMovieMaker.Plugin.dll`
- `YukkuriMovieMaker.Controls.dll`

など、YMM4インストールフォルダ内のDLLを参照します。

映像/D2D系ではVortice/SharpGen系の参照が追加で必要になる場合があります。

### `Private=false` について

`<Private>false</Private>` は **現在の公式YMM4必須要件としては扱わないでください**。

現在の公式サンプルcsprojはYMM4/Vortice/SharpGen参照にこれを設定していません。

プロジェクト側で出力/パッケージ制御のために使う場合は、YMM4仕様ではなく **そのリポジトリのビルド方針** として扱います。

---

# どのPluginを作る？

現在の公式サンプルで確認できる代表的なsurfaceです。

| やりたいこと | 主な入口 |
| --- | --- |
| 音声エフェクト | `AudioEffectBase` + `[AudioEffect]` |
| 映像エフェクト | `VideoEffectBase` + `[VideoEffect]` |
| 音声ファイル読み込み | `IAudioFileSourcePlugin` |
| 動画ファイル読み込み | `IVideoFileSourcePlugin` |
| 画像読み込み | `IImageFileSourcePlugin` |
| 立ち絵 | `ITachiePlugin` |
| 動画出力 | `IVideoFileWriterPlugin` |
| 波形 | `IAudioSpectrumPlugin` |
| 図形 | `IShapePlugin` |
| テキスト補完 | `ITextCompletionPlugin` |
| 場面切り替え | `ITransitionPlugin` |
| 音声合成 | `IVoicePlugin` |
| Tool / Timeline Tool | `IToolPlugin` / Tool関連public surface |
| カスタムProperty Editor | `PropertyEditorAttribute2` + `IPropertyEditorControl` |

詳しい対応:
[Official Plugin Surface Map](YMM4_AI_P1B_OFFICIAL_PLUGIN_SURFACE_MAP.md)

---

# 実装面を選ぶ順番

YMM4内部へどこまで踏み込むかは、次の順番で考えます。

```text
S1  Plugin-facing public API
 ↓
S2  public Host / WPF surface
 ↓
S3  bounded Reflection / adapter
 ↓
S4  Harmony / non-public host internals
```

## S1 — 公開Plugin API

最優先です。

例:

- `IToolPlugin`
- `ITimelineToolViewModel`
- public Timeline state
- public Effect / FileSource interfaces
- `SettingsBase<T>`
- public VoiceItem / AudioEffect / Voice interfaces

## S2 — 公開Host / WPF surface

S1に必要な意味情報がないときに使います。

例:

- public event
- `INotifyPropertyChanged`
- routed input
- public WPF command
- live DataContext上のpublic member

ただし、YMM4固有View型やVisual Tree構造を意味解析に使う場合は、バージョン依存性を明記してください。

## S3 — 限定Reflection

「Reflectionを使って何か探す」ではなく、

> **このYMM4版の、この正確な型の、この正確なmemberを使う**

という形に限定します。

必要なもの:

- exact YMM4 version
- exact type/member
- 必要な理由
- signature
- fail-safe
- revalidation trigger

期待したmemberがなければ **別のprivate memberを推測で探して続行しない** でください。

## S4 — Harmony / 非公開パッチ

最後の手段です。

必要なhost behaviorがS1〜S3で表現できないことを確認し、対象となる内部挙動を専用のEvidenceで固定してから使います。

---

# 「APIがある」ことと「その挙動」は別

YMM4で一番事故りやすいポイントです。

例えば:

> `Timeline.CurrentFrame` がpublicで書き換えられる。

これはAPI factです。

しかし、

> 書き換えれば必ずPreviewが見た目上再描画される。

まではAPI factではありません。

実際のLabでは:

- `CurrentFrame` 変更で `PropertyChanged("CurrentFrame")` は観測済み;
- しかしpixel-level preview repaintまでは、そのEvidenceでは証明していません。

このように、

```text
public symbol exists
!=
desired runtime semantics are guaranteed
```

として扱ってください。

---

# 情報源が食い違ったら？

一律に「公式が最優先」ではありません。

**何を知りたいか**で使う情報源を変えます。

## 型名・signature・コンパイル形

優先:

1. 現在の実装コード
2. 現在のAPI/assembly情報
3. README / 解説文

実例:

公式VideoSource READMEでは `IVideoSourcePlugin` と書かれている箇所がありますが、同じ公式サンプルの実装コードと現在のAPI indexは:

```text
IVideoFileSourcePlugin
IVideoFileSource
```

を使用しています。

この場合、コード生成では実装/API shapeを採用し、READMEとの不一致を記録します。

## 開発手順・作者が意図する使い方

現在の公式ドキュメント・公式サンプルを優先します。

## undocumented runtime behavior

実YMM4で再現したversion-pinned Evidenceを優先します。

## 製品として問題なく動くか

最終的には、そのPlugin自身のnative acceptanceで確認してください。

---

# よくあるAI生成ミス

## 1. 存在しない/古いinterfaceを作る

まず現在のP1/P1Bを確認してください。

名前が似ているinterfaceを推測しないでください。

## 2. `VideoEffectProcessorBase` を唯一の作り方だと思う

現在の公式VideoEffectサンプルは `IVideoEffectProcessor` を直接実装します。

一方、YMM4 Communityでは `VideoEffectProcessorBase` も現役で広く使われています。

つまり:

- 最小公式route: `IVideoEffectProcessor`
- 実績ある高度route: `VideoEffectProcessorBase`

の両方があります。

## 3. PropertyEditorは必ず `IPropertyEditorControl2`

現在の公式カスタムPropertyEditorの基本形は:

```text
IPropertyEditorControl
+
PropertyEditorAttribute2
```

です。

より多くのEditor contextが必要な別interfaceを使う場合は、現在のAPIを別途確認してください。

## 4. 値が変わった理由を勝手に推定する

例:

> CurrentFrame changed → user clicked Timeline

は成立しません。

LabではCurrentFrame変更が:

- Timeline pointer
- ruler
- keyboard
- playback

など複数経路から起こることを確認しています。

## 5. object referenceを永続IDとして使う

VideoItem splitでは元objectが消えることがあります。

逆にtrim/moveでは同じobjectのまま中身/位置が変化します。

さらにcopy/pasteでは同じsource rangeの別Occurrenceが作れます。

「object referenceだから大丈夫」「source rangeが同じだから同じItem」といった単純化を避けてください。

## 6. すぐReflection/Harmonyへ行く

まず現在の公開/standard routeを検索してください。

Labでは、後からpublic Command routeやpublic AudioEffects storageが見つかった例があります。

---

# Evidence-qualified Knowledge

このLabでは、mainへmergeされたかどうかをEvidenceの強さとはみなしません。

Labは実験branch/Draftを長期に残す運用を想定しています。

## 強いEvidenceの目安

native observationなら、できるだけ:

- exact YMM4 version
- host hash
- exact tested source SHA
- final PASS marker/assertions
- workflow run
- artifact ID/hash
- PASS boundary / NOT PROVEN
- 後続結果との矛盾なし

を揃えます。

この条件を満たすDraft/stacked branchの結果は **evidence-qualified** として利用できます。

ただし、必ずtested SHAを参照してください。

Branch HEADが後から変わっても、tested result自体を曖昧にしないためです。

入口:
[Evidence-qualified Host Behavior](YMM4_AI_P2_CANONICAL_HOST_BEHAVIOR.md)

索引:
[YMM4 AI Knowledge Index](YMM4_AI_KNOWLEDGE_INDEX.md)

---

# 現在ある便利なEvidence例

Knowledge Indexには、例えば次のような知見があります。

- ItemTemplate.SceneIdはrestartを跨ぐ一意IDではない
- CurrentFrame changeだけではpointer intentを判定できない
- VideoItem splitは元objectを置き換える
- trim/move/copy/UndoRedoを考えるとobject identityだけでは追跡できない
- `VideoItem.ContentLength` は消費source rangeではない
- `PlaybackRateMap` の定速source-time mapping
- YMM4同梱FFmpegのpublic locator
- standard CommandSettings routeでUndo/Redo/Split等を実行可能
- ToolState.SavedStateによるproject-specific Tool state
- Fold済みTimelineでnative-safeなnavigation / 補正が必要なrouteの区別
- public VoiceItem / IVoiceSpeakerによる再生成
- `VoiceItem.AudioEffects` の保存・UndoRedo・Item Editor surface

全てのPluginがこれらを必要とするわけではありません。

必要なものだけ参照してください。

---

# 自分のPluginで新しいYMM4挙動が必要になったら

既存Evidenceで答えられない場合は、いきなり大きなPluginへ組み込まず、問いを小さくしてください。

悪い問い:

> YMM4のTimelineを自由にいじれる？

良い問い:

> YMM4 4.56.1.0で、Tool Pluginから選択中Itemをpublic APIだけでSplitできるか？

検証には:

1. Question
2. exact environment
3. assertions
4. PASS boundary
5. NOT PROVEN
6. reproduction
7. evidence identity

を残すと、AIが後から結果を再利用しやすくなります。

---

# 実装Tipsについて

P3にはD2D/WPF/.NETの実装Tipsもあります。

ただし、これらは **YMM4 API仕様ではありません**。

例えば:

- `DrawingVisual`
- `StreamGeometry`
- `Freeze()`
- `stackalloc`
- `MemoryMarshal.Cast`
- `CollectionsMarshal.AsSpan`
- JSON source generator
- reflection caching

などは、必要な場面では有効ですが、AIが自動的に全部投入するものではありません。

**measure first** を基本にしてください。

[Implementation Guidance](YMM4_AI_P3_IMPLEMENTATION_GUIDANCE.md)

---

# 配布パッケージについて

現在の公式資料では、Pluginをzipにして拡張子を `.ymme` に変更する配布方法が案内されています。

ただし:

```text
.ymmeとしてインストールできる
!=
同一Pluginのupdate時に任意のファイルがどう保持されるかまで保証される
```

update/overwrite/preservationのようなruntime behaviorは、別Evidenceとして扱ってください。

---

# この資料の立場

- YMM4公式ドキュメントではありません。
- YMM4本体や第三者Pluginの権利を取得/再配布するものではありません。
- 外部資料は可能な限りリンク/commitを参照し、大量コピーしません。
- 実YMM4 behaviorは、検証したversion/boundaryを超えて一般化しません。
- YMM4更新時は、影響するsubsystemだけ再検証する設計を想定しています。

このLabのoriginal code / workflow / documentationのライセンスは、リポジトリの `LICENSE` を確認してください。

第三者のコード・バイナリ・商標・資料は、それぞれ元の権利条件に従います。

---

# AI開発者向け最終チェック

実装前:

- [ ] 現在のYMM4 target frameworkを確認した
- [ ] 作りたいPlugin surfaceをP1/P1Bで確認した
- [ ] S1から順に実装面を検討した
- [ ] 似たAPI名を推測で作っていない
- [ ] runtime behaviorが必要なら既存Evidenceを検索した

実装中:

- [ ] undocumented behaviorを推測で固定していない
- [ ] Reflectionはexact targetだけに限定した
- [ ] fail closedになっている
- [ ] host-owned / plugin-owned resourceを区別した
- [ ] Undo/Redo・保存・再読込が関係するなら必要なLifecycleを確認した

完成前:

- [ ] 実YMM4でPlugin loadを確認した
- [ ] 実際のユーザー操作経路を確認した
- [ ] 配布物に不要/host-owned DLLを混ぜていないか確認した
- [ ] version-sensitiveな依存を記録した
- [ ] Evidenceで証明していないことを「保証」と書いていない
