# YMM4 Plugin AI Prompt

このファイルは、**YMM4 Plugin AI Manualを添付したAIへそのまま渡す初期指示**です。

このPrompt自体は補助用です。Manual本文にも同等の指示が入っているため、Manual単体でも利用できます。

---

## 標準版

```text
添付した「YMM4 Plugin AI Manual」を、このYMM4プラグイン開発の基準資料として使用してください。

開発時は以下を守ってください。

1. 現在のYMM4公開API・公式サンプルを優先する。
2. 実装面は Public Plugin API → Public Host/WPF → 限定Reflection → Harmony/非公開内部 の順に検討する。
3. APIが存在することと、期待するruntime behaviorが保証されることを混同しない。
4. Manualに確認済みと書かれた挙動は、記載されたYMM4版・条件の範囲だけで使う。
5. undocumentedなYMM4挙動は推測で固定しない。必要なら小さい検証に分離する。
6. 公式READMEと公式実装/APIが食い違う場合は、不一致を明示する。型名/signatureはcurrent implementation/APIを優先する。
7. Reflectionを使う場合はtarget YMM4 version・exact type・exact memberを固定し、見つからなければfail closedする。
8. 似たprivate memberを推測で探索して実行しない。
9. Harmony/non-public patchは公開面で実現できない理由がある場合だけ使う。
10. performance optimizationはmeasure-firstとし、一般的なTipsをYMM4必須仕様として扱わない。
11. build成功だけで完了にせず、機能に必要な実YMM4上の挙動、Undo/Redo、save/reload、ユーザー操作を確認する。

Manualに載っていないYMM4内部挙動については、一般知識だけで断定しないでください。
不明な場合は「未確認」と明示し、必要なら検証方法を設計してください。
```

---

## 短縮版

```text
添付のYMM4 Plugin AI Manualを基準に実装してください。
公開API優先で、Public Plugin API → Public Host/WPF → 限定Reflection → Harmony/internalの順に検討してください。
APIの存在とruntime semanticsを混同せず、Manualにないundocumented behaviorは推測しないでください。
Reflectionはexact targetのみ・fail closed、Harmonyは最後の手段です。
最後にbuildだけでなく、機能に必要な実YMM4 acceptanceまで確認してください。
```

---

## 新規Plugin 要件テンプレート

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

## 既存Plugin改修用

```text
添付のYMM4 Plugin AI Manualを基準に、既存YMM4 Pluginを改修してください。

まず現在のコードがどの依存レベルにあるか分類してください:
Public Plugin API / Public Host-WPF / Reflection / Harmony-internal。

次に、より低リスクな現在のpublic routeへ置き換えられないか確認してください。

既に動いているReflection/Harmonyを理由なく削除・全面書き換えしないでください。
置き換える場合は、現在のrouteと新routeのbehavior equivalenceを確認してください。

内部memberへ依存する場合は、target YMM4 version・exact type/member・fail-safeを残してください。
Manualにないruntime behaviorは推測せず、必要なら検証へ分離してください。
```
