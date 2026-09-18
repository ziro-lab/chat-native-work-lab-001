# Music Fit ローカル実機テスト 001

目的は、公開サンプルを評価することではなく、**普段YMM4で使っている手元のBGMをMusic Fitへ通して、完成音をそのまま耳で確認すること**です。

音源はローカルPC内だけで処理します。実行時に音源・評価結果をGitHubや外部サービスへ送る処理はありません。

## 必要なPython

Music Fitローカル実機テストは **Python 3.11 / 3.12 / 3.13** が必要です。

**Python 3.12 x64を推奨**します。Python 3.10以下では NumPy 2.3.5 をインストールできません。

v0.1.1以降の `setup_windows.bat` は、対応Pythonがない場合に古い既定Pythonへフォールバックしません。また、既存の `.venv` がPython 3.10以下なら自動で削除して作り直します。

### v0.1.0でセットアップに失敗した場合

`.venv` フォルダを削除し、Python 3.12をインストールしてから新しい `setup_windows.bat` を実行してください。

## 使い方

1. ZIPを展開する。
2. `setup_windows.bat` を1回だけ実行する。
3. `input` フォルダへ手元のBGMを入れる。
4. `settings.json` の目標尺を必要なら変更する。
5. `run_windows.bat` を実行する。
6. 完了後、自動で表示される出力フォルダ内の `review.html` を開く。
7. A / B / C を聞き、簡単な評価を付ける。
8. 「評価JSONを保存」で `local-ratings.json` を保存する。

評価JSONをチャットへ渡せば、Music Fit由来の問題を集計できます。

## 評価項目

細かい音楽理論の判定は不要です。

各候補について、

- **全体**
  - 違和感なし
  - 少し違和感
  - はっきり違和感
- **Ending**
  - 違和感なし
  - 少し違和感
  - はっきり違和感
- **元曲にもある**
  - 気になった要素が元曲にも元から存在する場合にチェック

だけです。

「元曲にもある」を付けた違和感は、Music Fitの失敗と同じ重みでは扱いません。

## settings.json

標準:

```json
{
  "targets_seconds": [60, 120],
  "per_file_targets": {},
  "phase": 4,
  "budget_seconds_per_fit": 180,
  "max_files": 20
}
```

全曲に60秒・120秒を試します。

曲ごとに変えたい場合:

```json
{
  "targets_seconds": [60],
  "per_file_targets": {
    "battle_bgm.mp3": [95, 180],
    "ending_bgm.wav": [72.5]
  },
  "phase": 4,
  "budget_seconds_per_fit": 180,
  "max_files": 20
}
```

`per_file_targets` にあるファイルは、その値を優先します。

## 対応音源

Python SoundFile / libsndfile が読み込める形式を対象にします。

主対象:

- WAV
- FLAC
- OGG
- MP3
- AIFF

環境やコーデックによって読み込めない場合は、その曲だけエラーとして結果に残し、他の曲は続行します。

## 出力

`output/run-YYYYMMDD-HHMMSS/` に生成します。

各ケースには、

- 元曲
- 元曲Ending 8秒
- Music Fit候補 A / B / C
- 各候補のEnding 8秒

が入ります。

候補A/B/Cはアルゴリズム順位を隠した並びです。

## プライバシー境界

ランナー本体はネットワーク通信をしません。

ただし `setup_windows.bat` は最初の環境構築時だけ、Pythonパッケージをpipから取得します。セットアップ後の実機テストはローカル音源だけで完結します。

## このテストで確認したいこと

主に次の2点です。

1. 曲全体を聞いて、不自然な繰り返し・飛び・流れの違和感があるか。
2. 最後の終わり方に違和感があるか。

ここで問題が多い部分だけを次のCore改善対象にします。公開ベンチの数値を上げるためだけに複雑化することは避けます。
