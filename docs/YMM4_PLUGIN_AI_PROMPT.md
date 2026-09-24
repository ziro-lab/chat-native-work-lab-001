# YMM4 Plugin AI Prompt

このファイルは、ChatGPT / Codex / Claude等にYMM4プラグイン開発を依頼するときの **コピー用初期指示** です。

必要に応じて、この後ろに作りたいPluginの要件を書いてください。

---

## 標準版

```text
このリポジトリの YMM4 Plugin AI Manual と関連Referenceを、YMM4プラグイン開発の基準資料として使用してください。

開発時は以下を守ってください。

1. まず現在の公式YMM4開発baselineとofficial sample/API surfaceを確認する。
2. 実装面は S1 public Plugin API -> S2 public Host/WPF -> S3 bounded Reflection -> S4 Harmony/non-public internals の順に検討する。
3. APIの存在とruntime behaviorを混同しない。
4. undocumented YMM4 behaviorは推測しない。既存Knowledge/Evidenceを検索し、なければ必要最小限の検証に分離する。
5. Lab Evidenceはmainへのmerge有無ではなくEvidence品質で判断する。Draft/stacked resultを使う場合はexact tested source SHA / YMM4 version / PASS boundaryを確認する。
6. 公式READMEと公式実装/APIが食い違う場合は不一致を明示し、型名/signatureはcurrent implementation/API evidenceを優先する。
7. Reflectionを使う場合はexact type/member/versionを固定し、見つからない場合はfail closedする。似たprivate memberを推測で探索して実行しない。
8. Harmony/non-public patchはS1-S3で実現できない理由を説明できる場合だけ使う。
9. Performance optimizationはmeasure-first。一般的なTipsをYMM4必須仕様として扱わない。
10. 実装完了後は、build成功だけでなく実YMM4でのhost behavior / save-load / UndoRedo / user interactionなど、機能に必要なacceptanceを確認する。

特に関連するKnowledge Cardがある場合は、それを参照し、カードの Do not infer / Failure behavior / Evidence boundary を守ってください。
```

---

## 短縮版

```text
YMM4 Plugin AI Manualを基準に実装してください。
公開API優先、S1→S4の順で依存を選んでください。
undocumented behaviorは推測せずLab Evidenceを探してください。
Draft/stacked branchでもEvidenceが強ければtested SHA固定で利用可です。
API存在とruntime semanticsを混同しないでください。
Reflectionはexact targetのみ・fail closed、Harmonyは最後の手段です。
最後に実YMM4で必要なacceptanceまで確認してください。
```

---

## 要件テンプレート

この部分を埋めて標準版の後ろに追加できます。

```text
作りたいもの:
- 

対象YMM4:
- 例: 4.56系

Plugin種別（分かれば）:
- Tool / Timeline Tool / VideoEffect / AudioEffect / Voice / 不明

必要な操作:
- 

保存が必要な状態:
- 

Undo/Redoが必要:
- yes / no / 不明

実YMM4で検証したいこと:
- 

Reflection/Harmony:
- 可能なら避けたい / 必要なら可 / 制約あり

配布:
- .ymme / DLL / 未定
```

---

## 既存Plugin改修用

```text
既存YMM4 Pluginを改修してください。

まず現在のコードがどのYMM4 surfaceに依存しているかを S1-S4 で分類してください。
次に、より低リスクなpublic routeが現在存在しないか、Manual / P1 / Knowledge Index / Lab Evidenceを確認してください。

動いているReflection/Harmonyを、理由なく書き換えたり削除したりしないでください。
置き換える場合は、現行routeと新routeのbehavior equivalenceを確認してください。

YMM4 updateに依存する内部memberがある場合はexact target/versionとfail-safeを残してください。
```
