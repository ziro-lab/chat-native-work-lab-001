# no-Harmony レイヤーフォルダ — 便利機能追加設計 v0.1

作成日: 2026-09-23
状態: **設計候補。追加機能の実装・実機PASSを意味しない。**

目的は、LayerPatanの便利機能を再現しながら、既存Coreを置換せず、保守対象を増やしすぎないこと。
本書はPR #100の部品棚卸しを材料にした再設計であり、そこで提案したサービス分割を必須構造としては採用しない。

## 1. 採用する方針

- Harmony、メソッド差替え、ILパッチは使わない。
- 機能は削らない。管理パネル、フォルダ移動、可視性、Group Control、タイミング表示まで再現対象に残す。
- 既存Coreの計算・保護機構を維持する。追加機能は明示的な編集操作と表示情報として外側へ載せる。
- 状態の正本は一つ、編集確定の入口は一つ、YMM4固有知識の置き場は一つ。
- 単一の配布DLLを維持する。新しい常駐プロセス、データベース、DIコンテナ、イベントバス、汎用ワークフローエンジンは導入しない。
- サービス名の数を品質指標にしない。純粋な計算は関数でよく、ライフサイクルのない機能に常駐サービスを作らない。

再現基準はLayerPatan commit `aa58e7e772deb829853742afc65e42036cf09d47`に固定する。今後の本家更新は差分として別途判断し、開発途中で完成条件を自動拡張しない。

## 2. 維持するCoreと、追加側が担当すること

| 維持するもの | 変更しない契約 |
| --- | --- |
| FolderRangeTracker | 標準挿入・削除・上下移動に対する位置範囲の変換、交差拒否、先頭重複の扱い |
| StructuralDeltaDetector | 既存の観測・位置ヒント・Ambiguous判定 |
| FoldMap / FolderLayout | 論理Layerと表示行の唯一の対応 |
| DirectDisplayと既存入力補正 | ドラッグ中の表示書換え停止、Viewport lease、補正・解除の順序 |
| P1 native history契約 | ホスト所有操作へ参加するとき、追加側は勝手に二度目のRecordを呼ばない |
| P3 Core v1 codec | 既存FolderDocumentの意味と検証、読めない状態を黙って破棄しない方針 |

追加側で変えてよいものは、組立てコード、便利機能の明示的操作、UI、保存用の外側の形式、機能別能力判定。
既存Coreのファイルを一切変えないことと、接続コードまで固定することは区別する。証拠用の旧ファイルは維持し、製品側の組立て・重複を整理する。

### 現物確認で分かった前提差

棚卸し基準 `459d74bf468530244b05459cf47c9c0e332e8040` の試用版では、Plugin.csprojがリンクするのはHost、FolderLayout、GestureDisplay、FolderDocument/codec、FolderRangeTracker、FolderUxCommandsである。
HandsOnControllerの生成処理ではDirectDisplayとレイヤー列用マウス処理は接続される一方、P1 StructuralFolderBridge/StructuralDeltaDetector、P2 NavigationBridge、既存InputMap/FileDrop adapterを生成する経路は確認できなかった。

**Labで実証済みの機構と、配布候補へ接続済みの機構は同一ではない。** 追加前に接続棚卸しを行う。Coreの再開発ではなく、実証済み部品の接続を確認・補完する工程とする。Startup PASSを編集全体のPASSへ読み替えない。

## 3. 製品構成を4つの責務へ絞る

### FolderSession — プロジェクト単位の状態と寿命

既存FolderStateStoreとProjectStateCoordinatorの責務をここへ収束する。全シーンのFolderDocument、追加情報、保存アダプター、編集世代を所有する。Toolパネルの開閉には依存しない。
Coreの計算クラスはこのSessionから呼ばれる部品であり、別の状態管理サービスにしない。

### FolderCommands — すべての編集入口

Create / Rename / SetColor / SetHidden / InsertIntoFolder / DeleteFolderContents / MoveFolder / AddGroup / FitGroupsを通常の型付きメソッドとして公開する。
メニュー、札、パネル、ショートカットは同じメソッドを呼ぶ。共通化するのは対象検証と編集確定であり、文字列コマンドや汎用CommandBusは作らない。
複雑な範囲計算だけをFolderEditingRules、GroupRules、VisibilityRulesへ純粋関数として分ける。SelectionServiceやTimingSummaryServiceを独立した常駐オブジェクトとしては作らない。

### YmmHostAccess — ホスト境界

既存のホスト取得・入力・履歴アダプターを収容する。版依存のメンバー名、reflection、native操作の呼び出し、現在のTimeline参照の検証はここだけが知る。
便利機能が直接VisualTreeを探索したり、独自Undo履歴を持ったりしない。

### Folder UI — 2つの表示先、1つの操作系

タイムライン側表示と任意表示の管理パネル。どちらもSessionの読み取り用状態から描画し、Commandsへ操作を返す。
UIの選択・編集中の文字列・スクロール位置はViewの状態でよいが、フォルダ範囲や非表示状態の正本を複製しない。

これは4ファイルに詰め込む指定ではない。独立した検証や読みやすさのためのファイル分割は許容するが、機能数に比例するサービス・interface・登録処理は作らない。

## 4. Coreを変えない追加状態と保存

現行Core v1にはId、Start/End、Name、IsCollapsedがある。色や非表示を追加するために、範囲計算へ新しい責務を混ぜない。

Sessionの保存対象を一つの値として扱う:

- Core: 既存FolderDocumentそのもの。
- FolderOptions: `(TimelineKey, FolderId)`ごとのColorとHidden。
- VisibilityRestore: Timelineごとのレイヤー可視性復元情報。

FolderOptionsにStart/End、親子参照、表示座標を再保存しない。復元情報は変更済みの可視性を戻すための履歴であり、第二のフォルダモデルではない。削除されたIDの追加情報を整理するのも同じ編集確定処理が担当する。

### 保存形式の候補

ToolState.SavedState内にschemaVersion=2の外側の文書を置き、そのcoreフィールドへ既存v1文書を格納する。別ファイルや別ToolState項目へ分散しない。

- 読込: 旧v1は既存codecで読み、追加情報を既定値として補う。
- 読込: v2は外側を検証し、core部分を既存v1 codecへ渡す。
- 保存: 新形式の一つの文字列として保存する。
- 未知版・不正データ: 文書全体の元の文字列を隔離保持し、通常保存で上書きしない。
- 旧版プラグインへの降格: 外側のschemaVersion=2を旧codecが拒否し、既存のraw保全経路へ入ることを検証する。

v1へ黙ってColorを追加するだけでは、旧版が読めたと判断して追加情報を落として保存するおそれがあるため採用しない。新形式が動くことはまだ実機実証ではなく、追加受入条件である。

フォルダ構造・色・非表示はProject所有。既定色、確認表示、パネル追従設定はSettingsBase等の全体設定。選択、hover、編集中文字列、表示キャッシュは保存しない。

## 5. 編集確定を一箇所にする

単純操作は新しい状態を作って共通Commitへ渡すだけ。複合操作では短命な計画値を作る。計画は処理のための値であり、永続キュー、DSL、汎用処理グラフにはしない。

流れ:

1. 操作対象のProject世代、Timeline、FolderIdと現在の状態を確認。
2. 純粋関数で最終的な範囲・プロパティ変更を計算し、CoreのValidateを通す。
3. 名前入力や削除確認を先に終える。確認中に状況が変わったら再検証し、別Projectへ誤適用しない。
4. UIスレッド上でホスト操作を実行する。編集中の区間にawaitや追加ダイアログを入れない。
5. 実際の変化と計画を照合し、Core状態と追加情報をまとめて確定。
6. 所有者に応じたnative履歴確定を行い、表示更新を一度だけ通知。

| 操作の所有者 | 履歴の扱い |
| --- | --- |
| YMM4標準操作 | 既存P1経路へ参加。追加側から二度目のRecordを呼ばない |
| プラグインのメタデータ/複合操作 | 確認済みのpublic経路でnative変更と追加状態を一つの履歴単位へまとめる |
| Undo/Redo再生 | 既存履歴を復元するだけ。再推定、追加Record、追加の可視性操作を起こさない |
| 選択・追従・hover | ホスト本来の動作を使い、フォルダ文書のdirty状態を変更しない |

public Timeline.AddLayer/DeleteLayer/MoveLayer等を使うこと自体は、native所有の方針と矛盾しない。UI用RoutedCommandの反復で履歴を分割するのではなく、ホストのpublic操作とその履歴生成契約を優先して確認する。

ただしpublicメソッドが複合操作で必ず一回のUndoになるという保証は未取得。まず一つの代表複合操作で、Record境界と失敗時の扱いを確認する。存在未確認のBeginTransaction APIや、例外をcatchするだけの原子性は設計の前提にしない。

変更通知の再入防止は、以前の値へ戻すscopeまたは深さカウンターにする。内側のfinallyで外側の抑止を解除しない。Undo callbackは対象Sessionへ結び付け、グローバルな現在Projectへ無条件に書き込まない。

### 失敗時

事前拒否とキャンセルは状態・dirty・Undoとも不変。実行途中の失敗はそれと区別する。
履歴一単位で安全に回復できることを確認するまで破壊操作を有効化しない。無条件のUndo呼び出しで回復しようとせず、部分適用なら明示し、確認済みの回復経路と該当機能の停止を使う。別操作の履歴を勝手に巻き戻さない。

## 6. 作成・挿入・削除・移動の統一

### 作成範囲を本家へ合わせる

本家LayerMenuは、右クリック先が選択内なら選択の最小～最大、選択外ならクリック先だけを対象にする。飛び飛び選択も、その間を含む連続区間になる。
この動作を採用し、「間の未選択レイヤーも含む」「先頭を一段下げる」「空レイヤーを1枚追加する」をメニューに明示する。現行試用版の連続選択必須ルールを完成仕様とはしない。

一行作成、末尾追加、フォルダ内追加、Group Control用の先頭挿入は、同じ挿入計画の用途違いとして実装する。

### 標準挿入と『このフォルダへ追加』を区別する

A=L2..L5の直後へ標準Addを行う場合、既存CoreはAを伸ばさない。その仕様は維持する。
『Aの末尾へ追加』では、既存Coreで通常の挿入結果を計算した後、Aと必要な祖先に対してだけ、挿入先を所属させる明示的な編集を適用する。
隣のフォルダや子の範囲まで一律に伸ばさない。親と末尾を共有する入れ子、フォルダ先頭への挿入も同じ所有先の指定で扱う。
この処理は標準操作の自動追従を置換するものではなく、Rename/Createと同じくユーザーが要求した編集である。

### フォルダ解除と内容削除

Ungroupはメタデータだけを外す。内容削除はレイヤー・アイテム・子フォルダを含む別コマンドとする。削除する範囲と件数を確認へ表示する。
どちらも非表示の解除・復元情報整理を共通の可視性計算へ渡し、UI側に後始末を散らさない。

### フォルダごとの移動

標準MoveUp/Downは位置フォルダのまま。フォルダ全体移動は、選択したフォルダIDと子孫を同時に移す明示的操作とする。

- Drop位置はBefore / Into / Afterを最初に一つの論理挿入境界へ正規化。
- 自分自身/子孫へのIntoは拒否。変化しない境界へのDropはno-op。
- 親と子を同時選択しても二重移動しない。
- 移動する範囲内のアイテム・設定・GroupRange・可視性復元情報を同じ対応で扱う。
- 自動observerに同じ移動を二重適用させない。操作中のoriginを既存接続層で管理する。
- 単純な上下移動の反復や、item.Layerの一括直書きを未検証の代替として採用しない。

LayerPatan PlanMoveは参考にするが、外部操作の第二の推定器・履歴stackは輸入しない。追加の純粋な移動計画は、この明示的操作のためだけに使い、最後は既存Coreの不変条件を満たす。

## 7. 可視性 — 折りたたみとは別の機能

折りたたみは編集画面の行を詰めるだけ。非表示はYMM4のレイヤー可視性を変える操作。片方を実行して他方へ副作用を出さない。

非表示フォルダの区間の和から、隠す対象を毎回導出する。永続的な参照カウンターや、親子ごとの重複した有効/無効状態は持たない。
レイヤーごとの復元情報は一つだけ:

- 最初にプラグインが隠すとき、元の可視性を記録。
- 他の非表示フォルダも同じレイヤーを覆っても上書きしない。
- 一つ開いても、別の非表示フォルダに覆われていれば隠したまま。
- 最後の非表示理由がなくなったときだけ、記録した可視性へ戻す。
- もともと非表示だったレイヤーは非表示へ戻す。

本家同様、パネル上で隠れているレイヤーの目を操作した場合は『解除後の可視性』を変更する。YMM4側で利用者が明示的に表示へ戻した場合は、自動的に奪い返さないため、復元情報に利用者の上書き状態を保持する。再度明示的にフォルダを隠す操作まで尊重する。
内部書込みとUndo再生を利用者の上書きと誤判定しないことが受入条件。手動変更の通知を識別できない状態では推測して復元情報を捨てない。

構造変更時の復元情報のLayer番号は、その操作で既に得た対応表だけで更新する。別の可視性専用構造observerは作らない。

## 8. Group Control — 計算と適用だけを分ける

GroupRulesはGroupItemへの参照を保持しない。レイヤー位置と範囲の値から、警告・補正案を返す。
同じ計算を、Fit、構造変更追従、追加、パネルの警告表示で使う。診断結果に別の永続モデルを作らない。

- Fitは利用者が指定したフォルダの末尾へ合わせる。
- 自動補正は構造変更前の制御範囲を保つための補正。警告があるだけで全GroupRangeをフォルダ末尾へ強制しない。
- 入れ子で複数の適合候補があっても、勝手に一番内側へ結び付けない。
- Group追加は既存の挿入計画＋public GroupItem追加として実装する。
- native操作後に既にホストが目的値へ補正していたら二重に足さない。
- 外部操作の補正は確定済みの構造変化だけを対象にする。Ambiguousから推測してGroupRangeを書き換えない。
- GroupRange変更とフォルダ側状態を一回のUndoで戻せる接続位置を確認する。Recorded後に第二のRecordを追加する構成は採用しない。

本家の範囲式を一般化しすぎない。Group Controlが連続した下方向範囲であることに合わせた小さな区間計算で十分。

## 9. UI — 同じ機能を二重実装しない

管理パネルを作る。単なる将来候補へ追い出さず、全便利機能の再現対象に含める。ただし第二のタイムラインエンジンにはしない。

パネルは深さ付きの平坦な行一覧をListBoxへbindする。範囲の親子構造はCoreから導出し、同時に編集可能なTreeView用モデルを保存しない。
Rowの識別子で選択・編集中状態を保持し、WPFコンテナをフォルダidentityとして扱わない。標準仮想化・リサイクルを利用するが、実際に有効なItemsPanel/テンプレート構成を確認する。

同じCommandsを次の入口で使う:

- タイムラインの右クリック。
- フォルダ札の操作。
- パネルのボタン、F2、Ctrl+G、Delete、左右キー。
- パネルのBefore/Into/Afterドラッグ。

TextBox編集中はパネルのDelete等を横取りしない。OS全体のショートカット登録は不要。
ダブルクリック改名と開閉の干渉は、矢印と名前のhit領域を分ける案を推奨する。矢印は即時開閉、名前のダブルクリックは改名。元の『札全体シングルクリック』との細部差はhands-onで確認し、黙って完全同一と扱わない。

色は本家のパレット＋既定色をまず再現する。独自フルカラーピッカーやUIライブラリを新規依存にしない。YMM4側の既存ColorPickerは追加の任意色入力が必要になった場合の候補。
FolderColor変更と『YMM4レイヤー色へ適用』は別操作。札の色を変えただけで利用者のレイヤー色を上書きしない。

Follow Timelineは既存NavigationBridgeへ合流する。空レイヤーへの追従も同じFoldMapを使う。再生位置の現在アイテム表示は読み取りだけで、選択変更やseekを暗黙に発生させない。

## 10. タイミング帯とGroup背景の表示

前の棚卸しの『LayerLabelSurfaceへtiming summaryを描く』は訂正する。時間軸方向の帯は、狭いレイヤー名列ではなく、**アイテム領域の時間軸上**に描く必要がある。

- レイヤー名列: 札、色、階層線、目の表示。
- アイテム領域: 折りたたみowner行の内部アイテム帯、必要なGroup範囲の補助表示。
- 両方で同じFoldMapと同じフォルダsnapshotを利用する。
- Xは検証済みのFrame→表示位置変換、Yは既存FoldMap、clipは現在viewportを使う。
- native itemのTop/Heightを6pxへ変える方式は採用しない。元アイテムと別のプラグイン描画として実現する。
- 初期実装は小さいOnRender/DrawingVisualで、見えているowner行と時間区間だけを描く。独自描画エンジンは不要。
- 再生ごとに全アイテムを再解析しない。項目時間・レイヤー変更時に区間を更新し、スクロール/zoomでは表示座標だけ更新する。

native Group背景が誤った大きさのまま残る場合、正しい帯を追加するだけでは修正にならない。既存の表示境界で対象背景だけを安全に補正/置換できるか、局所的な取り付け・解除を確認する必要がある。できていない状態を見た目同等と呼ばない。この点と時間軸X座標の取得は未確認のホスト接点として残す。

## 11. ライフサイクルと更新範囲

ProjectのSessionと、表示中のTimelineは別の寿命を持つ。未保存Project内でシーンを切り替えただけで、全フォルダ状態を消してはいけない。
Path変更は一つのsignalであり、Project identityそのものではない。同じパスの再読込、Save As、未保存シーン切替を区別する接続を確認する。
不明な遷移で推測してResetしない。Sessionの世代を更新して保留操作を取り消し、確認できるまで旧状態を新Projectへ書き出さない。

表示更新は単純な変更種別で足りる:

- 名前/色: 関連札と関連パネル行。
- 開閉/構造: 既存FoldMap/表示と行一覧。
- アイテム時間: タイミング帯と現在アイテム情報。
- CurrentFrame: 現在アイテム表示のみ。

一つのDispatcher更新要求へまとめる。新しいイベントバスや一般的なリアクティブ処理基盤は不要。全開閉で一つずつ再描画・JSON化しない。
UI探索が必要なら一つの共有再接続処理へ限定し、機能別タイマーを増やさない。
Dispose時はイベント、保留Dispatcher処理、追加メニュー、Adorner、Viewport/gesture leaseを確実に解除する。テスト用fixture生成は配布runtimeの通常経路から分離する。

## 12. 機能の漏れを防ぐ対応表

| 本家の機能群 | 今回の実現方法 | 主な未確認点 |
| --- | --- | --- |
| 一行作成、飛び飛び選択、先頭補正 | 対象範囲正規化＋共通挿入計画 | 複合操作の履歴 |
| 全開閉、改名、ダブルクリック/F2 | 既存状態操作をbatch/共通Command化 | 実際の操作感 |
| 色、レイヤー色反映 | ID別追加情報＋明示的なhost色変更 | host setter/history |
| 非表示、元の可視性復元 | 区間の和＋一つの復元情報map | native目操作/Undo通知 |
| フォルダ内アイテム選択 | public selectionを共通入口から呼ぶ | 隠れた複数対象との共存 |
| 末尾/途中レイヤー追加 | 所有先付きの共通挿入 | 境界・履歴 |
| フォルダ解除、内容ごと削除 | 別コマンド＋共通後始末 | 複数削除の回復 |
| Group追加/Fit/自動補正 | 純粋GroupRules＋共通host編集 | 同一Record・二重補正防止 |
| パネル一覧、キー、Follow、現在項目 | 標準ListBox＋読取projection＋既存Navigation | シーン/選択/scroll |
| パネル複数選択、Before/Into/After移動 | 明示的移動計画＋native block操作 | 最重要のnative確認 |
| 階層線、tooltip、警告、Group lanes | 状態から計算する表示 | データ量に応じた局所更新 |
| 折りたたみ時間帯、Group背景 | アイテム領域の追加描画 | 座標・元背景・解除 |

保存・自動バックアップとの整合は既存ToolState経路へ新文書を載せて受入確認する。本家のHarmony保存注入や外部JSON fallbackを構造ごと輸入しない。
裸のScrollToItem等、既存Coreがサポート境界を明示した外部経路まで、この追加設計で自動解消したとは扱わない。

## 13. 実装と確認の順序

### S0: 既存候補へのCore接続をそろえる

既存の実証済み部品と実際に生成する部品を対照する。作成したフォルダに対して構造編集、Undo、folded inputが接続された一本の代表経路を確認する。全過去probeの再実行は不要。

### S1: 共通操作入口と作成・全開閉・選択

既存の挙動を変えず入口を集める。名前入力キャンセル、選択外右クリック、飛び飛び選択、フォルダ先頭補正をpureで確認。一行作成は複合履歴の確認後に有効化する。

### S2: 追加文書と色・可視性

外側のv2保存、旧v1移行、未知raw保持をpure中心で実装。色・可視性の代表native setter/historyと保存再読込を一緒に確認する。

### S3: 挿入・削除とGroup Control

共通のnative編集確定を先に確認してから、末尾追加、内容削除、Group追加/Fit/追従を同じ経路へ乗せる。

### S4: 管理パネルと明示的なブロック移動

一覧・キーバインドは既存CommandsへのView。Before/Into/Afterの計算はpure。実際のmulti-layer moveとnative historyだけを狭いLab課題とする。

### S5: タイミング帯・Group表示と全体受入

新しい表示面を局所確認し、全便利機能のhands-onを行う。必要なhost-sensitive経路のみ、二つのpinned hostで受け入れる。

既存の軽量検証方針を維持する。通常はV0＋必要なV1一ホスト。Core契約を変えない追加でP1の全353項目を毎回回さない。比較用のassertion数を増やす目的のテストを作らない。

## 14. 少数でも外せない破綻ケース

- A=L2..L5の末尾へ追加: Aと必要な祖先だけが伸び、隣接フォルダは正しく移る。
- 親と子の二重非表示: 親だけ開いても子は隠れ、最後に元の可視性へ戻る。
- 色/可視性変更後のUndo: フォルダ追加情報とYMM4プロパティが同時に戻る。
- 一行フォルダ作成/Group追加: キャンセルは無変更、確定は一つの履歴単位。
- フォルダ移動: ID・子孫・アイテム・レイヤー設定・GroupRangeが同じ計画へ従う。
- 未保存Projectのシーン切替: 他シーンのフォルダを初期化しない。
- 同じパスを再度開く: 古いView/履歴callback/保留メニュー操作を新Sessionへ適用しない。
- v2を理解しない版: 追加情報を失ったv1へ黙って保存し直さない。
- 拡大/スクロール後のタイミング帯: nativeアイテム位置を変更せず、実際の時間軸に一致する。

## 15. 採用しないもの

機能ごとのreflection、独自Undo stack、別の自動範囲推定器、Toolパネル専用FolderDocument、親子と範囲の二重正本、JSON差分による毎回の変更判定、常駐worker、DB、汎用イベントバス、動的プラグイン式Feature登録、必要性未確認の外部UIライブラリ。
『安全なnative複合操作が未確認』を、仕組みを大きくする理由にも機能を削る理由にもせず、次の具体的な確認課題として扱う。

## 根拠と参照先

ここまでのnative PASSを拡張する文書ではない。設計判断は本書の提案である。

- [P1 convergence](YMM4_NO_HARMONY_P1_ARCHITECTURE_CONVERGENCE.md): Coreの所有権と、製品組立ての分離。
- [FolderRangeTracker](../experiments/ymm4/no-harmony-folder-range-tracker/src/FolderRangeTracker.cs): 標準操作の位置フォルダ契約。
- [P3 FolderDocument](../experiments/ymm4/no-harmony-p3-persistence/src/FolderDocument.cs): 現行v1の保存対象。
- [現行試用版](../experiments/ymm4/no-harmony-p4-hands-on-candidate/src/Plugin/Plugin.csproj): 組込対象と実証済み部品の照合起点。
- [Reference registry](YMM4_REFERENCE_SOURCES.md)、[部品棚卸し](YMM4_LAYERPATAN_REPRODUCTION_COMPONENT_INVENTORY.md)、[Harmony監査](YMM4_LAYERPATAN_CONVENIENCE_HARMONY_AUDIT.md)。
- [本家LayerMenu](https://github.com/bluemistel/YMM4-LayerPatan/blob/aa58e7e772deb829853742afc65e42036cf09d47/src/LayerPatan/Integration/LayerMenu.cs): 選択範囲・クリック先・作成補助の意味。
- [本家LayerOps](https://github.com/bluemistel/YMM4-LayerPatan/blob/aa58e7e772deb829853742afc65e42036cf09d47/src/LayerPatan/Core/LayerOps.cs): 所有先付き挿入、明示的移動、Group範囲計算。
- [本家TimelineTracker](https://github.com/bluemistel/YMM4-LayerPatan/blob/aa58e7e772deb829853742afc65e42036cf09d47/src/LayerPatan/Services/TimelineTracker.cs): 可視性復元・hostへの適用。本書は実行機構ごとのコピーを提案しない。
- [WPF Commanding](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/commanding-overview): 複数UI入口で操作を共有する標準機構。
- [WPF Controls performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls): 仮想化・コンテナ再利用とView状態の注意点。
- [WPF DrawingVisual](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/using-drawingvisual-objects): レイアウト/入力を持たない描画部品の候補。YMM4上の座標・性能の実証ではない。
