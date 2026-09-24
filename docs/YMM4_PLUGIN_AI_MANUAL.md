# YMM4 Plugin AI Manual

> **ChatGPT / Codex / Claude などのコーディングAIと、ゆっくりMovieMaker4（YMM4）のプラグインを開発するための非公式マニュアル**
>
> **このMarkdown単体で使えることを目的にしています。**
>
> 最終整理: 2026-09-25  
> 現行開発基準: YMM4 v4.47.0.0以降 / .NET 10  
> 実機確認を含む主な対象版: YMM4 4.55.1.1 Lite / 4.56.1.0 Lite

---

## これは何？

このマニュアルは、YMM4プラグインをAIに実装させるときに起きやすい、

- 古い.NET向けコードを生成する
- 存在しないAPIを作る
- 似た名前のinterfaceを取り違える
- APIが存在するだけで実行時挙動まで保証されたと思い込む
- いきなりReflectionやHarmonyへ飛ぶ
- YMM4内部の挙動を推測で補う
- 一般的な最適化Tipsを「YMM4必須仕様」として過剰適用する

といった事故を減らすための基準資料です。

**YMM4公式ドキュメントではありません。**

公式資料・公式サンプル・公開API・実装例・実機確認で得られた知見を、AIが扱いやすい形に整理しています。

---

# 0. AI Lazy Read Protocol — 最初にここだけ読む

このManualは**最初から最後まで通読する前提ではありません。**

AIはまずこの章だけを読み、ユーザー要件から必要な章を選んでください。  
**関係ない章を先読みしない**ことを推奨します。

## Core Rules — 常時適用

以下だけは全タスクで適用してください。

1. 現在のYMM4公開API・公式サンプルを優先する。
2. 実装面は **Public Plugin API → Public Host/WPF → 限定Reflection → Harmony/非公開内部** の順に検討する。
3. APIが存在することと、期待するruntime behaviorが保証されることを混同しない。
4. undocumentedなYMM4挙動を推測で固定しない。
5. 公式READMEと公式実装/APIが食い違う場合は不一致を明示し、型名・signatureはcurrent implementation/APIを優先する。
6. Reflectionはtarget YMM4 version・exact type・exact memberを限定し、見つからなければfail closedする。
7. 似たprivate memberを推測で探して実行しない。
8. Harmony/non-public patchは公開面で実現できない理由がある場合だけ使う。
9. performance optimizationはmeasure-first。
10. build成功だけで完了にせず、機能に必要な実YMM4 acceptanceを確認する。
11. 「確認済み」と書かれた挙動は、記載されたYMM4版・条件の範囲だけで使う。
12. 必要のない章は読まず、依存が発生した時点で追加ロードする。

---

## Lazy Readの意味

利用するAI/クライアントによっては、添付Markdown全体が物理的には一度にcontextへ入る場合があります。

その場合でも、このManualでいうLazy Readは:

- **参照対象として採用する章を先にRoutingする**
- **Routingされていない章を設計根拠へ勝手に混ぜない**
- **新しい依存が出た時だけ追加章を有効化する**

という意味で使います。

部分読み込み・file searchが使えるAIでは、実際に必要章だけ取得してください。

全文がcontextにあるAIでも、**attention / evidence scopeをlazyにする**ことを目的にします。

---

## 最初のRouting手順

AIはユーザー要件を受けたら、実装前に短く次を決めてください。

```text
Plugin種別:
必要なManual章:
追加条件:
- 保存/Project switch?
- Undo/Redo/標準Command?
- Timeline input/Preview?
- Media source-time?
- Reflection/Harmony?
- FFmpeg?
- D2D resource?
- Performance hot path?
- .ymme配布?
```

そのあと、下のRouting Tableで必要章だけ読んでください。

---

## Routing Table — 作るもの別

| 作りたいもの | 最初に読む章 | 条件付きで追加 |
| --- | --- | --- |
| 新規project / build土台 | **1, 2, 3, 4** | 26, 29 |
| Tool Plugin | **5** | 6, 7, 17, 21, 25, 29 |
| Timeline Tool | **5, 17** | 6, 7, 18-21, 25, 29 |
| Custom Property Editor | **8** | 7, 29 |
| Video Effect | **9, 23** | 24, 29 |
| Audio Effect | **10** | 11, 23, 29 |
| VoiceItem補助 / AudioEffect保存 | **11** | 7, 12, 29 |
| Voice / VOICEVOX系 | **12** | 11, 29 |
| Video File Source | **13, 23** | 19, 20, 22, 24, 29 |
| Audio File Source | **13** | 22, 24, 29 |
| Image File Source | **13, 23** | 24, 29 |
| Tachie Plugin | **14** | 15, 16, 23, 29 |
| Template / Tachie配置 | **15, 16** | 5-7, 29 |
| 長時間動画解析 / Navigator | **18, 19, 20** | 17, 22, 29 |
| Layer折り畳み / 表示row変換 | **21** | 5, 17, 29 |
| FFmpeg利用 | **22** | 13, 29 |
| Direct2D / GPU resource | **23** | 9, 13, 24, 29 |
| 性能改善だけが目的 | **24** | 対象機能章 |
| 状態監視 / polling削減 | **25** | 5, 17 |
| .ymme配布 | **26** | 29 |
| AI生成コードのレビュー | **27** | 対象機能章, 29 |
| 未確認YMM4挙動の調査 | **28** | 対象機能章 |
| 完成前acceptance | **29** | 対象機能章 |
| AIへ要件を渡す | **30** | 対象機能章 |
| 出典確認 / Manual更新 | **31, 32** | 必要箇所のみ |

---

## Cross-cutting Trigger — 条件が出たら追加で読む

タスク途中で次の条件が出た場合だけ追加章を読んでください。

| 条件 | 追加章 |
| --- | --- |
| Projectごとに状態を保存したい | **6** |
| Undo/Redo / Split / 標準操作を呼びたい | **7** |
| 独自設定UIを作る | **8** |
| VoiceItemに永続設定を持たせたい | **11** |
| Pronounce / VOICEVOX再生成 | **12** |
| Template/Characterのidentityが必要 | **15, 16** |
| CurrentFrame / pointer intent / Preview同期 | **17** |
| VideoItemのSplit/Trim/Move/Copy追跡 | **18** |
| source rangeを計算する | **19, 20** |
| Layer fold / display row | **21** |
| FFmpeg/ffprobe | **22** |
| D2D leak / resource再生成 | **23** |
| 実測で性能問題が出た | **24** |
| Timer/pollingを入れそう | **25** |
| 配布installer/update | **26** |
| AIの推論が怪しい | **27** |
| YMM4挙動が未確認 | **28** |
| 完成判定 | **29** |

---

## Starter Recipe Routing — コードを書き始めるときだけ読む

設計方針が決まったあと、最初の骨格が欲しい場合だけ **33章** の該当Recipeを追加で読んでください。

| 作りたいもの | Recipe |
| --- | --- |
| 新規Plugin project | **33.1** |
| Tool Plugin | **33.2** |
| Timeline Tool | **33.3** |
| Custom Property Editor | **33.4** |
| VideoEffect — 公式最小route | **33.5** |
| VideoEffect — `VideoEffectProcessorBase` route | **33.6** |
| AudioEffect | **33.7** |
| Video / Audio FileSource | **33.8** |

Recipeは完成品ではなく**最初の足場**です。  
要件にない機能をRecipeから勝手に増やさないでください。

---

## Lazy Readの停止条件

必要章を読んだ時点で、以下が分かればそれ以上の章は読まなくて構いません。

- 使用するPlugin/public surface
- 必要なYMM4挙動
- S1〜S4の依存レベル
- 保存/UndoRedo/lifecycle要件
- version-sensitiveな注意
- 完成時に確認すべきacceptance

逆に、実装中に新しい依存が発生したら、その時点で該当章だけ追加で読んでください。

---

## AIへ最初に渡す指示

このファイルをAIへ添付したあと、次を渡してください。

```text
添付した「YMM4 Plugin AI Manual」を開発基準として使用してください。

最初にManual全体を通読しないでください。
まず「0. AI Lazy Read Protocol」だけを読み、要件から必要章をRoutingしてください。
実装前に「参照予定章」を短く列挙し、その章だけ読んでください。
途中で新しい依存条件が出た場合のみ、Cross-cutting Triggerに従って追加章を読んでください。

Core Rulesは常時適用してください。
Manualにないundocumented YMM4 behaviorは推測せず、必要なら最小の検証へ分離してください。
```

その後に普通に要件を書きます。

例:

```text
YMM4 4.56系向けに、選択したアイテムの開始位置へ登録済みテンプレートを配置するTool Pluginを作りたい。
できるだけ公開APIだけで実装したい。
Undo/RedoもYMM4標準挙動へ乗せたい。
```

この例なら、まず **5, 7** を読み、保存が必要になった時点で **6**、Timeline inputまで必要なら **17** を追加します。

---

# 1. 現在の開発基準

## Target Framework

YMM4は **v4.47.0.0で.NET 10へ移行**しています。

現在の公式サンプルでは:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
  <UseWPF>true</UseWPF>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

が使われています。

古いYMM4記事・サンプルから.NET 8 / .NET 9設定をそのまま持ち込まないでください。

公式参考:

- YMM4公式「プラグインを作成する」  
  https://manjubox.net/ymm4/faq/plugin/how_to_make/
- 公式サンプル  
  https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples

---

## YMM4インストール先の参照

公式サンプルは `Directory.Build.props.sample` を使い、ローカルのYMM4パスを:

```xml
<Project>
  <PropertyGroup>
    <YMM4DirPath>D:\YMM4\</YMM4DirPath>
  </PropertyGroup>
</Project>
```

のように外出ししています。

これはかなり使いやすい方法です。

プロジェクト本体へ開発者個人の絶対パスを書かず、

```text
Directory.Build.props.sample  ← 配布
Directory.Build.props         ← 各自作成
```

に分けると扱いやすくなります。

---

## 参照DLL

代表的なYMM4側DLL:

- `YukkuriMovieMaker.Plugin.dll`
- `YukkuriMovieMaker.Controls.dll`

映像/D2D系では必要に応じて:

- `Vortice.Direct2D1.dll`
- `Vortice.DirectX.dll`
- `Vortice.Mathematics.dll`
- `SharpGen.Runtime.dll`
- `SharpGen.Runtime.COM.dll`

などを参照します。

**全部のPluginが全部を参照する必要はありません。**

作る機能で実際に必要な型を基準に追加してください。

---

## `<Private>false</Private>` はYMM4必須ではない

YMM4のDLL参照に:

```xml
<Private>false</Private>
```

を付ける構成はあり得ますが、**現在の公式YMM4開発要件として必須とは確認できません**。

現行公式サンプルのcsprojは、YMM4 / Vortice / SharpGen参照に `Private=false` を設定していません。

したがってAIは、

> YMM4では必ずPrivate=falseが必要

とは書かないでください。

使用する場合は、そのリポジトリのbuild/package方針として扱います。

---

# 2. 主なPluginの種類

現在の公式サンプルで確認できる代表的な入口です。

| やりたいこと | 主な型 |
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
| Tool | `IToolPlugin` |
| Timeline Tool | Tool関連public API + Timeline Tool surface |
| カスタム設定UI | `PropertyEditorAttribute2` + `IPropertyEditorControl` |

この一覧を「YMM4に存在する全Plugin型」とは考えないでください。

---

# 3. 実装面は低リスクなものから選ぶ

YMM4へどこまで踏み込むかは、次の順で検討してください。

```text
S1  Public Plugin API
 ↓
S2  Public Host / WPF surface
 ↓
S3  Bounded Reflection
 ↓
S4  Harmony / non-public internals
```

## S1 — Public Plugin API

最優先。

例:

- `IToolPlugin`
- `ITimelineToolViewModel`
- public Timeline state
- Effect / FileSource / Voice interfaces
- `SettingsBase<T>`
- public `VoiceItem` surface
- standard command settings

公開面で要件が満たせるなら、そこで止めます。

---

## S2 — Public Host / WPF surface

Plugin APIだけでは「ユーザーが何をしたか」などの意味情報が足りない場合に検討します。

例:

- public event
- `INotifyPropertyChanged`
- routed input
- WPF command
- public DataContext member

ただしYMM4固有のView型・Visual Treeを見て意味判定する場合は、バージョン依存として扱います。

---

## S3 — Bounded Reflection

Reflectionを使う場合は:

```text
「何か使えそうなprivate APIを探す」
```

ではなく、

```text
YMM4 4.56.1.0の
この型の
このmemberを
この目的で使う
```

まで絞ります。

最低限:

- 対象YMM4版
- exact type
- exact member
- signature
- 公開APIでは不足する理由
- memberがない場合の挙動
- 再検証条件

を記録します。

期待するmemberがなければ、**似たprivate memberを探して続行しない**でください。

---

## S4 — Harmony / non-public patch

最後の手段です。

公開API、public Host/WPF、限定Reflectionのどれでも必要な挙動を作れない場合だけ検討してください。

Harmonyを使うこと自体を目標にしないでください。

---

# 4. 情報源が食い違った場合

「公式資料だから常に文章の方が正しい」という扱いもしません。

## 型名・signature

優先:

1. 現在の実装コード
2. 現在のAPI / assembly情報
3. README・説明文

### 実例: VideoSource

公式VideoSource READMEには `IVideoSourcePlugin` と記載された箇所があります。

しかし同じ公式サンプルの現在の実装コードでは:

```csharp
public class SampleVideoSourcePlugin : IVideoFileSourcePlugin
```

となっており、現在のAPI情報でも:

```text
IVideoFileSourcePlugin
IVideoFileSource
```

が確認できます。

**コード生成では `IVideoFileSourcePlugin` を使います。**

このような不一致を見つけた場合、AIは黙って辻褄を合わせず、不一致を明示してください。

---

## 開発手順・公式の意図

現在の公式ドキュメント・公式サンプルを優先します。

## undocumentedな実行時挙動

実際のYMM4で確認した結果を、確認したバージョンに限定して扱います。

---

# 5. Tool Plugin

Tool系では公開Surfaceをまず確認してください。

代表的な入口:

- `IToolPlugin`
- `IToolViewModel`
- Timeline系では `ITimelineToolViewModel`
- `TimelineToolInfo`
- public `Timeline`

## 「memberがある」と「明示実装が必須」は別

現在のinterfaceにはdefault implementationを持つmemberがあります。

たとえば `AllowMultipleInstances` やPlugin metadata系について、

> すべてのPluginが必ず全部明示実装する

と決め打ちしないでください。

現在のinterface定義を確認してください。

---

# 6. Tool状態の保存

## `ToolState.SavedState`

**YMM4 4.56.1.0で実機確認済み:**

project固有のTool状態を `ToolState.SavedState` に保存し、別Project A/Bで別状態を保持して再読込できることを確認しています。

重要な注意:

**既に存在するinner ViewModelへ、Project切替のたびに `LoadState()` が再度呼ばれるとは限りません。**

確認したProject切替同期は概ね:

```text
YMM4がToolAreaのproject stateを復元
        ↓
ProjectFilePath変更
        ↓
Pluginが現在のToolArea.SaveState()を読む
```

という経路でした。

したがってAIは:

> Projectが変わればLoadStateが必ず飛んでくる

とは仮定しないでください。

対象確認版: **YMM4 4.56.1.0**

---

# 7. YMM4標準Commandを呼ぶ

**YMM4 4.55.1.1 / 4.56.1.0で実機確認済み:**

一部の標準操作は:

```text
CommandSettings.Default[CommandType]
        ↓
RoutedUICommandEx
```

経由でnative commandとして実行できます。

確認した例:

- Undo
- Redo
- 次フレーム
- 前フレーム
- 現在位置で選択ItemをSplit
- 現在位置にKeyFrame追加

つまり、標準操作をToolボタンから呼びたい場合に、**最初からCtrl+Z等のsynthetic key inputへ行く必要はありません。**

注意:

- 全 `CommandType` の挙動を保証するものではありません
- live contextで `CanExecute` を確認してください
- WPF ButtonのIsEnabled同期まで自動保証されるとは限りません

---

# 8. Custom Property Editor

現在の公式カスタムEditorの基本形は:

```text
IPropertyEditorControl
+
PropertyEditorAttribute2
```

です。

## 重要

**`IPropertyEditorControl2` が全カスタムEditorに必須とは扱わないでください。**

現在の公式サンプルは `IPropertyEditorControl` を実装しています。

---

## BeginEdit / EndEdit

公式サンプルは:

```text
BeginEdit
 ↓
値を変更
 ↓
EndEdit
```

の順で編集境界を通知します。

Undo/Redoへ正しく乗せるため、カスタムEditorで値を書き換えるときはこの境界を意識してください。

複数選択については、公式サンプルの:

```text
ItemProperty[]
ItemPropertiesBinding.Create(...)
```

のようなmulti-edit対応パターンをまず確認してください。

---

# 9. Video Effect

現在の公式サンプルでは:

```text
VideoEffectBase
+
[VideoEffect]
+
IVideoEffectProcessor
```

が基本形です。

## `VideoEffectProcessorBase` も存在する

一方、YMM4 Communityの現在の実装では `VideoEffectProcessorBase` が多数使われています。

したがって:

```text
公式の最小route
  -> IVideoEffectProcessorを直接実装

実績ある高度route
  -> VideoEffectProcessorBaseを利用
```

の2系統として考えてください。

**VideoEffectProcessorBaseだけが唯一の正解ではありません。**

---

## 現在のCommunity実装で見られるshape

例:

```csharp
protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
protected override void setInput(ID2D1Image? input)
protected override void ClearEffectChain()
public override DrawDescription Update(EffectDescription effectDescription)
```

nullable `ID2D1Image?` を使う実装や、描画状態だけ変更して `CreateEffect()` がnullを返す実装もあります。

古いsignatureを決め打ちしないでください。

---

## constructor注意

一般的なC#の注意です。

```csharp
public Processor(..., Effect effect) : base(devices)
{
    this.effect = effect;
}
```

でbase constructorがoverride可能な `CreateEffect()` 等を呼ぶ設計の場合、overrideはderived constructor bodyより先に実行されます。

したがって `CreateEffect()` 内で、

> constructor bodyで後から代入するfieldは既に初期化済み

と仮定しないでください。

---

# 10. Audio Effect

現在の公式サンプルは:

```text
AudioEffectBase
+
[AudioEffect]
+
AudioEffectProcessorBase
```

を使います。

Processor例では:

```csharp
public override int Hz
public override long Duration
protected override void seek(long position)
protected override int read(float[] destBuffer, int offset, int count)
```

を実装しています。

`seek` / `read` が小文字なのはbase memberの現在のshapeに従ってください。

---

# 11. VoiceItemのAudioEffects

**YMM4 4.56.1.0で実機確認済み:**

public `VoiceItem.AudioEffects` にcustom `AudioEffectBase` を追加し、

- public collectionから列挙
- add/remove
- host discovery
- `IsEnabled`
- custom property notification
- Item Editorの「Audio effects」表示
- typed Property Editor
- membership Undo/Redo
- Project save/reload
- reload後のpublic enumeration

まで動作することを確認しています。

したがって、VoiceItem固有の補助機能や状態をAudioEffectとして持たせたい場合、**private collectionやHarmonyを使う前に `VoiceItem.AudioEffects` を検討してください。**

対象確認版: **YMM4 4.56.1.0**

---

# 12. VoiceItem / 音声合成

**YMM4 4.56.1.0で実機確認済み:**

実Timeline上のVoiceItemについて、public surfaceだけを使って:

```text
VoiceItem.CreateVoiceFileAsync()
 ↓
public IVoiceSpeaker
 ↓
Pronounce取得
 ↓
Pronounceを変更
 ↓
IVoiceSpeaker.CreateVoiceAsync(..., patched Pronounce, ..., VoiceItem.FilePath)
 ↓
VoiceItemの実音声ファイルを更新
 ↓
VoiceItem.ClearVoiceCache()
```

という再生成経路を確認しています。

確認したpublic surfaceには:

- `VoiceItem.CreateVoiceFileAsync()`
- `VoiceItem.Pronounce`
- `VoiceItem.VoiceParameter`
- `VoiceItem.VoiceCache`
- `VoiceItem.ClearVoiceCache()`
- `Character.Voice`
- `Character.VoiceParameter`
- `VoiceDescription(IVoiceSpeaker)`
- `VoiceDescription.SetSpeaker(IVoiceSpeaker)`

などがあります。

したがって、VOICEVOX等のPronounce補正を行いたい場合、**内部Engineへ行く前にpublic `IVoiceSpeaker` routeを検討してください。**

対象確認版: **YMM4 4.56.1.0**

---

# 13. File Source

## Video

現在の実装/APIで使われている主な型:

```text
IVideoFileSourcePlugin
IVideoFileSource
```

代表的なshape:

```csharp
IVideoFileSource? CreateVideoFileSource(
    IGraphicsDevicesAndContext devices,
    string filePath)
```

`IVideoFileSource` の公式サンプルでは:

- `Duration`
- `Output`
- `Update(TimeSpan)`
- `GetFrameIndex(TimeSpan)`
- `Dispose()`

が実装されています。

---

## Audio

```text
IAudioFileSourcePlugin
IAudioFileSource
```

公式サンプルでは:

- `Duration`
- `Hz`
- `Read(...)`
- `Seek(TimeSpan)`
- `Dispose()`

を使います。

サンプルREADMEでは、読み出し音声は常に2chである必要があると説明されています。

---

## Image

`IImageFileSourcePlugin` を使います。

公式サンプルはWICを使って `ID2D1Bitmap?` を返していますが、**WIC自体が必須という意味ではありません。**

---

# 14. Tachie

現在の公式サンプルでは:

- `ITachiePlugin`
- `ITachieSource2`
- `TachieCharacterParameterBase`
- `TachieItemParameterBase`
- `TachieFaceParameterBase`

を役割ごとに分けています。

AIは「立ち絵Pluginだから1クラス」と単純化しないでください。

---

# 15. Template / Tachie cloneの注意

**YMM4 4.55.1.1で実機確認済み:**

`TachieFaceItem.GetClone()` は独立Itemを作りますが、確認ケースではsource `Character` object referenceを保持しました。

一方:

```text
ItemTemplate.CreateItemsAsync(...)
```

では、同名Characterが登録済みの場合、destination側のcanonical Characterへ結び直される挙動を確認しています。

つまり:

```text
clone内容の複製
!=
destination Characterへのcanonical rebind
```

です。

Templateから立ち絵表情Item等を配置するPluginでは、この2つを同じ処理だと思わないでください。

対象確認版: **YMM4 4.55.1.1**

---

# 16. ItemTemplateの一意IDに注意

**YMM4 4.55.1.1で実機確認済み:**

`ItemTemplate.SceneId` はrestartを跨ぐ一意なTemplate IDとしては使えません。

同じ:

- Name
- Path
- SceneId

を持つ別Templateが共存し、restart後も別レコードとして残るケースを確認しています。

したがってPlugin側で永続的なLibrary IDが必要なら:

- Plugin独自IDを持つ
- YMM4側metadataはlocatorとして扱う
- 0件 → missing
- 1件 → resolved
- 複数件 → ambiguous

のように扱い、**曖昧時に勝手に最初のTemplateを選ばない**でください。

対象確認版: **YMM4 4.55.1.1**

---

# 17. TimelineのCurrentFrame

`Timeline.CurrentFrame` が変更されたことだけでは、

> ユーザーがTimeline上の時刻をクリックした

とは判断できません。

**YMM4 4.55.1.1で実機確認したCurrentFrame変更経路:**

- blank Timeline click
- ruler click
- ruler drag
- keyboard navigation
- playback

したがって、ユーザー操作の種類を判定したいToolでは、必要に応じてpointer origin等の別情報を組み合わせてください。

---

## Preview refreshも別問題

同じくYMM4 4.55.1.1で:

- `Timeline.CurrentFrame` write
- `PropertyChanged("CurrentFrame")`

は確認しています。

しかし、

> そのイベントが起きた = pixel-levelでPreviewが必ず再描画済み

までは同じ意味ではありません。

また確認した範囲では、Timeline Tool向けに「今すぐPreviewを強制再描画する」という専用public commandは確立できませんでした。

`TimelineViewModel.ScrollFrame(int)` をseek/refresh代わりに使わないでください。

---

# 18. VideoItemのobject identity

VideoItemを追跡するToolでかなり重要です。

## Split

**YMM4 4.56.1.0で実機確認済み:**

VideoItemをSplitすると、確認ケースでは:

- 元のVideoItem objectはTimelineから消える
- 左右は両方とも新しいobject

になりました。

したがって:

> object referenceを保存しておけばSplit後も追える

とは考えないでください。

---

## Trim

同じ4.56.1.0で:

- head trim
- tail trim

は、**同じVideoItem objectのまま** Frame / Length / ContentOffset等が変わるケースを確認しています。

つまり逆に:

> object referenceが同じ = source rangeも同じ

でもありません。

---

## Move

Split後のpieceを移動したケースでは:

- 同じobject
- source coordinatesは維持
- Timeline Frameだけ変化

を確認しています。

---

## Copy / Paste

同じ:

- FilePath
- ContentOffset
- Length
- PlaybackRate

を持つ別VideoItem occurrenceを、異なるFrame/Layerに作れることを確認しています。

したがって:

> source identity + source range

だけでもTimeline上のOccurrenceを一意に決められません。

---

## まとめ

```text
object identityだけ
→ 不十分

source identity + rangeだけ
→ copy/paste後は不十分
```

です。

長時間動画解析・レビュー・ナビゲーションTool等では、

```text
source側の解析データ
+
現在Timeline上のOccurrence解決
```

を分けて考える方が安全です。

対象確認版: **YMM4 4.56.1.0**

---

# 19. VideoItem.ContentLengthをsource rangeだと思わない

**YMM4 4.56.1.0で実機確認済み:**

30秒mediaに対して:

- PlaybackRate2 = 50%
- 100%
- 200%
- ContentOffset = 0s / 5s

を組み合わせても、確認ケースでは `ContentLength` は30秒のmedia durationのままでした。

したがって:

> ContentLengthから消費source rangeを計算する

設計は避けてください。

---

# 20. PlaybackRateMap

**YMM4 4.56.1.0で実機確認済み:**

constant positive rate 50 / 100 / 200%では、native `PlaybackRateMap` のsource-time mappingが:

```text
sourceTime = ContentOffset + itemTime * rate / 100
```

となることを確認しています。

重要:

`ContentOffset` は既にsource-media側offsetなので、さらにrateを掛けません。

ただし、この確認では `PlaybackRateMap` getter自体はnon-public側の限定Reflectionでした。

また:

- animated
- non-monotonic
- reverse

全般をこの単純式で一般化しないでください。

---

# 21. FoldされたTimelineのnavigation

Layer折り畳みのように表示rowとlogical layerがずれる機能では、**全部のnavigationを補正すればいいわけではありません。**

YMM4 4.55.1.1 / 4.56.1.0で確認したno-Harmony folded Timelineでは:

native-safeだったroute:

- `ScrollToLowerLayer`
- `ScrollToHigherLayer`
- 実foreground Down/Up layer navigation

fold-awareではなかったroute:

- bare / same-item `TimelineViewModel.ScrollToItem`

という違いがありました。

つまり:

> foldingした → 全navigationを独自補正

ではなく、**壊れているrouteだけを補正する**方が安全です。

---

# 22. YMM4同梱FFmpeg

**YMM4 4.56.1.0 Lite x64で実機確認済み:**

YMM4配布物には:

```text
Resources\bin\x64\ffmpeg\ffmpeg.exe
Resources\bin\x64\ffmpeg\ffprobe.exe
```

が存在しました。

またpublic:

```text
YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator
```

から:

- `GetFFmpegDirectory()`
- `GetFFmpegDllDirectory()`
- `GetFFmpegExePath()`
- `GetUserFFmpegDirectory()`

が利用できることを確認しています。

YMM4 Plugin内でFFmpegが必要な場合、ユーザーへ別FFmpegインストールを要求する前に、このpublic locatorを確認してください。

注意:

- future versionで同じ配置とは限りません
- public `GetFFprobeExePath()` は確認できていません
- ffprobeを使うならreturned directoryのsiblingを存在確認してください
- locatorが使えない場合にinstall pathを推測しないでください

---

# 23. Direct2D resource管理

公式VideoEffect / VideoSourceサンプルでも、D2D resourceの明示的なdisposeが行われています。

基本:

- 自分が生成/所有したresourceのownershipを明確にする
- effect input等のreferenceを必要に応じて切る
- Output等のowned COM objectをdisposeする
- effect/resource本体をdisposeする
- YMM4 Developer Modeで未解放DirectX objectを確認する

**YMM4が所有するobjectまで勝手にdisposeしない**でください。

---

## 動的resource再生成

LUTなどparameter変更で再生成する重いresourceでは:

```text
parameter change検知
 ↓
旧resourceをconsumerからdetach
 ↓
旧owned resource dispose
 ↓
新resource生成
 ↓
attach
```

という形が扱いやすいです。

ただし具体的なdetach方法はresource/APIごとに違います。

---

# 24. WPF / performance Tips

以下はYMM4 API仕様ではなく一般的な実装Tipsです。

候補:

- `DrawingVisual`
- `StreamGeometry`
- immutable `Freezable.Freeze()`
- buffer reuse
- `stackalloc`
- `MemoryMarshal.Cast`
- `CollectionsMarshal.AsSpan`
- JSON source generator

**measure first** で使ってください。

AIに:

> 高速そうだから全部入れる

をさせないでください。

特に:

- `stackalloc` に万能な安全要素数はありません
- `CollectionsMarshal.AsSpan` は通常コードの標準選択ではありません
- `DrawingVisual` は単純UIにも必須ではありません
- Freeze後に変更するresourceには使えません

---

# 25. Timerよりevent-drivenを優先

Tool Pluginで状態監視するとき、`DispatcherTimer` で常時pollingする前に:

1. public event
2. PropertyChanged / CollectionChanged
3. UndoRedo event
4. debounce/coalesced rescan

で解けないか確認してください。

Timerが必要なら:

- Dispose時に停止
- event detach
- UI threadで重い処理をしない

を守ります。

---

# 26. `.ymme` 配布

現在の公式資料では:

1. Plugin配布物をzip化
2. 拡張子を `.ymme` に変更
3. 配布

という方法が案内されています。

ただし:

```text
.ymmeでinstall可能
!=
同一Plugin update時の全ファイル保持/上書き規則が保証済み
```

です。

update/migrationに依存するPluginでは、必要な挙動を別途実YMM4で確認してください。

---

# 27. AIがやりがちな危険な推論

## 「public setterがあるからUIも完全更新される」

しない。

状態変更とvisible refreshは別問題です。

## 「同じobjectだから同じ意味のItem」

しない。

Trim等でsame objectの内部rangeが変わります。

## 「objectが変わったから別source」

しない。

Splitで新objectになります。

## 「同じsource rangeなら同じTimeline occurrence」

しない。

Copy/Pasteで複数作れます。

## 「公式READMEに書いてある型名なら実装も同じ」

必ずしも一致しません。

current code/APIを確認してください。

## 「CommunityがReflection/Harmonyを使っているから自分も使う」

しない。

まず公開routeを確認します。

## 「最適化Tipsは全部入れた方が良い」

しない。

必要なhot pathだけ測って最適化します。

---

# 28. 不明なYMM4挙動を検証するとき

良い検証は問いが狭いです。

悪い例:

> Timelineを自由に操作できるか？

良い例:

> YMM4 4.56.1.0で、Tool Pluginから選択中Itemをpublic commandで現在Frame位置にSplitできるか？

最低限:

```text
Question
Environment / YMM4 version
Host identity
Assertions
PASS boundary
NOT PROVEN
Reproduction
Evidence identity
```

を残してください。

AIが後から結果を一般化しすぎる事故を減らせます。

---

# 29. Buildだけで完成扱いしない

Pluginでは:

```text
compile PASS
!=
YMM4上で正しく動く
```

です。

機能に応じて確認してください。

例:

- Pluginが実際にloadされる
- menu/editorへ表示される
- target Itemが正しい
- Timeline selectionが正しい
- Previewが期待どおり
- Undo/Redo
- save/reload
- YMM4 restart
- Project switch
- keyboard/mouse route
- .ymme install/update
- YMM4 version difference

全部を毎回やる必要はありません。

**その機能が依存する挙動だけ**確認します。

---

# 30. AI向け要件テンプレート

Manualを添付したうえで、これを埋めると依頼しやすいです。

```text
作りたいもの:
-

対象YMM4:
- 例: 4.56系

Plugin種別:
- Tool / Timeline Tool / VideoEffect / AudioEffect / Voice / FileSource / 不明

主な操作:
-

保存が必要な状態:
-

Undo/Redo:
- 必要 / 不要 / 不明

Project save/reload:
- 必要 / 不要 / 不明

Reflection/Harmony:
- 可能なら避けたい / 必要なら可 / 制約あり

配布:
- .ymme / DLL / 未定

今回、実YMM4で確認すべき挙動:
-
```

---

# 31. 参考資料

## 公式

YMM4 プラグイン作成:
https://manjubox.net/ymm4/faq/plugin/how_to_make/

公式Plugin Samples:
https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples

YMM4 Community Plugin Source:
https://github.com/manju-summoner/YukkuriMovieMaker.Plugin.Community

## 非公式API Reference

YMM API Docs:
https://ymm-api-docs.vercel.app/

YMM4Plugin Scrapbox:
https://scrapbox.io/ymm4plugin/

YMM4 plugin template（Tool / Timeline Tool等の参考実装）:
https://github.com/leftcontroller0518/YMM4plugin_template

これらは便利ですが、非公式資料や実装例をruntime guaranteeとして扱わないでください。

---

# 32. このManualの更新方針

YMM4は更新されます。

このManualでは:

- 公開API変更
- 公式sample変更
- .NET/TargetFramework変更
- Plugin surface変更
- version-sensitiveな実機挙動

が変わったとき、影響箇所だけ更新する前提です。

確認済み挙動には対象YMM4版を残します。

将来版で再確認していないものを「現在も必ず同じ」とは書き換えません。

---

# 33. Starter Recipes — 最初の1ファイル目を迷わないための骨格

この章は**必要なRecipeだけ読む**ことを前提にしています。

目的は「完成コードを配ること」ではなく、

> 空プロジェクトから、YMM4 Pluginとして正しい方向へ最初の一歩を出す

ことです。

## Recipe共通ルール

- 現在のtargetは `net10.0-windows10.0.19041.0`
- `UseWPF=true`
- 不要なYMM4 DLLを最初から全部参照しない
- `Private=false` をYMM4必須仕様として追加しない
- Recipeの型/signatureを現在のYMM4/APIと照合する
- compile後は実YMM4 loadを確認する
- Recipeから始めても、runtime behaviorは必要に応じて確認する

---

## 33.1 最小Plugin Project

### `Directory.Build.props.sample`

```xml
<Project>
  <PropertyGroup>
    <YMM4DirPath>D:\YMM4\</YMM4DirPath>
  </PropertyGroup>
</Project>
```

これをコピーして `Directory.Build.props` を作り、自分のYMM4 install pathへ変更します。

### 最小 `.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="YukkuriMovieMaker.Plugin">
      <HintPath>$(YMM4DirPath)YukkuriMovieMaker.Plugin.dll</HintPath>
    </Reference>

    <Reference Include="YukkuriMovieMaker.Controls">
      <HintPath>$(YMM4DirPath)YukkuriMovieMaker.Controls.dll</HintPath>
    </Reference>
  </ItemGroup>

  <Target Name="PostBuild" AfterTargets="PostBuildEvent"
          Condition="Exists('$(YMM4DirPath)')">
    <Exec Command="if not exist &quot;$(YMM4DirPath)user\plugin\$(ProjectName)&quot; mkdir &quot;$(YMM4DirPath)user\plugin\$(ProjectName)&quot;" />
    <Exec Command="copy /Y &quot;$(TargetPath)&quot; &quot;$(YMM4DirPath)user\plugin\$(ProjectName)\&quot;" />
  </Target>

</Project>
```

追加のYMM4 / Vortice / SharpGen DLLが必要になったら、**実際に使う型に応じて追加**してください。

### 最初の確認

1. build
2. DLLが `user\plugin\<ProjectName>\` にコピーされたか確認
3. YMM4起動
4. 設定 → Plugin一覧でloadされたか確認

---

## 33.2 最小Tool Plugin

Toolは「Plugin定義」「ViewModel」「View」の3つに分けると始めやすいです。

### Plugin定義

```csharp
using YukkuriMovieMaker.Plugin;

namespace MyYmm4Tool;

public sealed class MyToolPlugin : IToolPlugin
{
    public string Name => "My Tool";
    public Type ViewModelType => typeof(MyToolViewModel);
    public Type ViewType => typeof(MyToolView);

    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => "My Tools";
    public int DefaultOrder => 0;
}
```

既存のYMM4組み込みUtilitiesグループへ入れたい場合は、hard-codeした日本語/英語名ではなく、確認したhost localization resourceを使う方が安全です。

### ViewModel

```csharp
using YukkuriMovieMaker.Plugin;

namespace MyYmm4Tool;

public sealed class MyToolViewModel : IToolViewModel
{
    public string Title => "My Tool";
    public bool CanSuspend => true;

#pragma warning disable CS0067
    public event EventHandler<CreateNewToolViewRequestedEventArgs>?
        CreateNewToolViewRequested;
#pragma warning restore CS0067

    public ToolState SaveState()
        => new()
        {
            Title = Title,
            SavedState = ""
        };

    public void LoadState(ToolState state)
    {
    }
}
```

### View

```xml
<UserControl
    x:Class="MyYmm4Tool.MyToolView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="12">
        <TextBlock Text="Hello YMM4 Tool" />
    </Grid>
</UserControl>
```

```csharp
using System.Windows.Controls;

namespace MyYmm4Tool;

public partial class MyToolView : UserControl
{
    public MyToolView()
    {
        InitializeComponent();
    }
}
```

### 次に足すもの

必要になったものだけ追加します。

- Projectごとの状態 → 6章
- Timeline接続 → 33.3
- Undo/Redo / YMM4 command → 7章
- polling回避 → 25章

---

## 33.3 最小Timeline Tool

Timeline Toolは通常のTool ViewModelへ `ITimelineToolViewModel` を追加し、YMM4から渡される `TimelineToolInfo` を保持します。

```csharp
using YukkuriMovieMaker.Plugin;

namespace MyTimelineTool;

public sealed class MyTimelineToolViewModel
    : IToolViewModel, ITimelineToolViewModel
{
    TimelineToolInfo? timelineInfo;

    public string Title => "My Timeline Tool";
    public bool CanSuspend => true;

#pragma warning disable CS0067
    public event EventHandler<CreateNewToolViewRequestedEventArgs>?
        CreateNewToolViewRequested;
#pragma warning restore CS0067

    public void SetTimelineToolInfo(TimelineToolInfo info)
    {
        timelineInfo = info;
    }

    public int GetCurrentFrame()
        => timelineInfo?.Timeline.CurrentFrame ?? 0;

    public int GetSelectedCount()
        => timelineInfo?.Timeline.SelectedItems.Count ?? 0;

    public ToolState SaveState()
        => new() { Title = Title };

    public void LoadState(ToolState state)
    {
    }
}
```

### ここでやらないこと

- `MainWindow.DataContext` をいきなりReflectionする
- CurrentFrame changeだけで「user clicked」と判定する
- synthetic key inputでUndo/Redoする

まずpublic Timeline / standard command surfaceを確認してください。

---

## 33.4 最小Custom Property Editor

現在の公式sample baselineは:

```text
IPropertyEditorControl
+
PropertyEditorAttribute2
```

です。

XAMLなしの最小例:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Views.Converters;

namespace MyPlugin;

public sealed class StepEditor : UserControl, IPropertyEditorControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(int),
            typeof(StepEditor),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public event EventHandler? BeginEdit;
    public event EventHandler? EndEdit;

    public StepEditor()
    {
        var button = new Button { Content = "+1" };

        button.Click += (_, _) =>
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);
            Value++;
            EndEdit?.Invoke(this, EventArgs.Empty);
        };

        Content = button;
    }
}

public sealed class StepEditorAttribute : PropertyEditorAttribute2
{
    public override FrameworkElement Create()
        => new StepEditor();

    public override void SetBindings(
        FrameworkElement control,
        ItemProperty[] itemProperties)
    {
        var editor = (StepEditor)control;

        editor.SetBinding(
            StepEditor.ValueProperty,
            ItemPropertiesBinding.Create(itemProperties));
    }

    public override void ClearBindings(FrameworkElement control)
    {
        BindingOperations.ClearBinding(
            control,
            StepEditor.ValueProperty);
    }
}
```

使うproperty側:

```csharp
[Display(Name = "回数")]
[StepEditor]
public int Count { get; set; }
```

### ポイント

- edit前に `BeginEdit`
- edit後に `EndEdit`
- 複数編集は `ItemProperty[]` を捨てずに扱う
- `IPropertyEditorControl2` を全Editor必須だと思わない

---

## 33.5 最小VideoEffect — `IVideoEffectProcessor` 直実装

「画像effectを新規生成せず、DrawDescriptionだけ変える」最小構成です。

### Effect

```csharp
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace MyPlugin;

[VideoEffect("X移動", ["サンプル"], [])]
public sealed class MoveXEffect : VideoEffectBase
{
    public override string Label => "X移動";

    [Display(Name = "X")]
    [AnimationSlider("F0", "px", -100, 100)]
    public Animation X { get; }
        = new(0, -10000, 10000);

    public override IVideoEffectProcessor CreateVideoEffect(
        IGraphicsDevicesAndContext devices)
        => new MoveXProcessor(this);

    public override IEnumerable<string> CreateExoVideoFilters(
        int keyFrameIndex,
        ExoOutputDescription exoOutputDescription)
        => [];

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => [X];
}
```

### Processor

```csharp
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace MyPlugin;

public sealed class MoveXProcessor : IVideoEffectProcessor
{
    readonly MoveXEffect item;
    ID2D1Image? input;

    public MoveXProcessor(MoveXEffect item)
    {
        this.item = item;
    }

    public ID2D1Image Output
        => input ?? throw new InvalidOperationException(
            "VideoEffect input is not set.");

    public void SetInput(ID2D1Image? input)
        => this.input = input;

    public void ClearInput()
        => input = null;

    public DrawDescription Update(
        EffectDescription effectDescription)
    {
        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;

        var x = item.X.GetValue(frame, length, fps);

        var draw = effectDescription.DrawDescription;

        return draw with
        {
            Draw = new(
                draw.Draw.X + (float)x,
                draw.Draw.Y,
                draw.Draw.Z)
        };
    }

    public void Dispose()
    {
    }
}
```

これは**現在の公式sample系統に近い最小route**です。

---

## 33.6 `VideoEffectProcessorBase` を使うRecipe

D2D effect chainを持つ場合、現在のYMM4 Communityでは `VideoEffectProcessorBase` を使う実装も多数あります。

Effect側:

```csharp
public override IVideoEffectProcessor CreateVideoEffect(
    IGraphicsDevicesAndContext devices)
    => new BlurProcessor(devices, this);
```

Processor例:

```csharp
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace MyPlugin;

public sealed class BlurProcessor(
    IGraphicsDevicesAndContext devices,
    BlurEffect item)
    : VideoEffectProcessorBase(devices)
{
    GaussianBlur? effect;

    protected override ID2D1Image? CreateEffect(
        IGraphicsDevicesAndContext devices)
    {
        effect = new GaussianBlur(devices.DeviceContext);
        disposer.Collect(effect);

        var output = effect.Output;
        disposer.Collect(output);

        return output;
    }

    protected override void setInput(ID2D1Image? input)
    {
        effect?.SetInput(0, input, true);
    }

    protected override void ClearEffectChain()
    {
        effect?.SetInput(0, null, true);
    }

    public override DrawDescription Update(
        EffectDescription effectDescription)
    {
        if (effect is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;

        effect.StandardDeviation =
            (float)item.Blur.GetValue(frame, length, fps);

        return effectDescription.DrawDescription;
    }
}
```

### 重要

`CreateEffect()` がbase constructor中に呼ばれる設計では、derived constructor bodyで後から代入するfieldへ依存しないでください。

上の例では `CreateEffect()` は `devices` だけでresourceを作り、`item` は通常の `Update()` で使っています。

---

## 33.7 最小AudioEffect

### Effect

```csharp
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Audio.Effects;
using YukkuriMovieMaker.Plugin.Effects;

namespace MyPlugin;

[AudioEffect("音量サンプル", ["サンプル"], [])]
public sealed class GainEffect : AudioEffectBase
{
    public override string Label => "音量サンプル";

    [Display(Name = "倍率")]
    [AnimationSlider("F2", "x", 0, 2)]
    public Animation Gain { get; }
        = new(1, 0, 2);

    public override IAudioEffectProcessor CreateAudioEffect(
        TimeSpan duration)
        => new GainProcessor(this, duration);

    public override IEnumerable<string> CreateExoAudioFilters(
        int keyFrameIndex,
        ExoOutputDescription exoOutputDescription)
        => [];

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => [Gain];
}
```

### Processor

```csharp
using YukkuriMovieMaker.Player.Audio.Effects;

namespace MyPlugin;

public sealed class GainProcessor : AudioEffectProcessorBase
{
    readonly GainEffect item;
    readonly TimeSpan duration;

    public GainProcessor(
        GainEffect item,
        TimeSpan duration)
    {
        this.item = item;
        this.duration = duration;
    }

    public override int Hz => Input?.Hz ?? 0;

    public override long Duration
        => (long)(duration.TotalSeconds * Hz) * 2;

    protected override void seek(long position)
    {
        Input?.Seek(position);
    }

    protected override int read(
        float[] destBuffer,
        int offset,
        int count)
    {
        var read = Input?.Read(
            destBuffer,
            offset,
            count) ?? 0;

        for (var i = 0; i + 1 < read; i += 2)
        {
            var gain = (float)item.Gain.GetValue(
                (Position + i) / 2,
                Duration / 2,
                Hz);

            destBuffer[offset + i] *= gain;
            destBuffer[offset + i + 1] *= gain;
        }

        return read;
    }
}
```

### 最初の注意

公式sampleはstereo interleaved dataを前提にしています。

別channel構成やresamplingを扱うなら、ここから先は別設計です。

---

## 33.8 最小FileSource

### Video FileSource Plugin

```csharp
using System.IO;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.FileSource;

namespace MyPlugin;

public sealed class MyVideoSourcePlugin
    : IVideoFileSourcePlugin
{
    public string Name => "My Video Source";

    public IVideoFileSource? CreateVideoFileSource(
        IGraphicsDevicesAndContext devices,
        string filePath)
    {
        if (Path.GetExtension(filePath)
            .Equals(".myvideo",
                StringComparison.OrdinalIgnoreCase))
        {
            return new MyVideoSource(devices);
        }

        return null;
    }
}
```

Sourceの最低shape:

```csharp
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.FileSource;

namespace MyPlugin;

public sealed class MyVideoSource
    : IVideoFileSource
{
    readonly ID2D1Bitmap bitmap;

    public MyVideoSource(
        IGraphicsDevicesAndContext devices)
    {
        bitmap = devices.DeviceContext
            .CreateEmptyBitmap(1920, 1080);
    }

    public TimeSpan Duration
        => TimeSpan.FromSeconds(1);

    public ID2D1Image Output => bitmap;

    public void Update(TimeSpan time)
    {
    }

    public int GetFrameIndex(TimeSpan time)
        => 0;

    public void Dispose()
    {
        bitmap.Dispose();
    }
}
```

### Audio FileSource Plugin

Plugin側の入口は:

```csharp
public sealed class MyAudioSourcePlugin
    : IAudioFileSourcePlugin
{
    public string Name => "My Audio Source";

    public IAudioFileSource? CreateAudioFileSource(
        string filePath,
        int audioTrackIndex)
    {
        // 対応fileならIAudioFileSourceを返す
        return null;
    }
}
```

`IAudioFileSource` では主に:

- `Duration`
- `Hz`
- `Read(...)`
- `Seek(TimeSpan)`
- `Dispose()`

を実装します。

### FileSource共通の考え方

```text
Plugin/factory
  -> このfileを自分が読めるか判断
  -> 読めるならSourceを返す
  -> 読めないならnull

Source
  -> 実際のmedia read / render
  -> resource ownership
  -> Dispose
```

と分けると整理しやすいです。

---

## RecipeをAIへ渡すときのおすすめ指示

```text
Manualの該当Starter Recipeを最小骨格として使ってください。

Recipeを完成仕様だとは扱わず、
今回の要件に必要な部分だけ変更してください。

不要なReflection/Harmony、独自永続化、最適化、追加dependencyは入れないでください。

まず最小構成でbuild/loadを通し、
その後に機能を1つずつ追加してください。
```

Plugin制作の最初の目標は、

> **高機能にすることではなく、正しいsurfaceでYMM4にloadされる最小Pluginを作ること**

です。


---

# 最終チェック

実装前:

- [ ] TargetFrameworkは現在のYMM4に合っている
- [ ] 作りたいPluginのpublic surfaceを確認した
- [ ] S1 → S4の順に検討した
- [ ] 似た名前のAPIを推測で作っていない
- [ ] API存在とruntime behaviorを分けた

実装中:

- [ ] undocumented behaviorを勝手に一般化していない
- [ ] Reflectionはexact targetだけ
- [ ] fail closedになっている
- [ ] host-owned / plugin-owned resourceを区別した
- [ ] Undo/Redoやsave/reloadの必要な境界を確認した
- [ ] performance optimizationを過剰適用していない

完成前:

- [ ] 実YMM4でPlugin loadを確認した
- [ ] 必要なユーザー操作routeを確認した
- [ ] save/reloadやUndo/Redoが必要なら確認した
- [ ] version-sensitiveな依存を記録した
- [ ] 確認していない挙動を「保証」と書いていない

---

## 一言でいうと

**公開APIから始める。挙動は推測しない。内部へ踏み込むほど対象を狭くする。Buildだけで終わらせず、必要なYMM4挙動を確認する。**

この4つを守れば、AI生成のYMM4 Pluginはかなり事故りにくくなります。
