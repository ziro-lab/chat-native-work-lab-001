# YMM4 no-Harmony Full — S5 Visual Parity Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

S5の目的は、S0-S4で完成した no-Harmony の状態・構造・操作系を変えずに、折りたたみで失われる時間軸情報と Group 範囲情報をアイテム領域へ補助描画することだった。

Accepted runtime source:

`4672ffdd06f490a3ce4f9b6f21e51ad2278ae60c`

PR:

- #114 `s5: add timing-band and Group visual parity`

## 1. Frozen visual architecture

S5の表示は次の境界で固定する。

```text
YMM4 TimelineItemViewModel public Left / Width
                    +
existing DirectDisplay.Layout / FoldMap
                    +
live IItem Frame / Layer / GroupRange
                    ↓
      pure VisualSummaryRules
                    ↓
  plugin-owned Adorner drawing
```

重要な点:

- 独自 Frame -> X 変換を持たない;
- X / Width は YMM4 自身の `TimelineItemViewModel.Left / Width` を読む;
- Y / owner row は既存 `DirectDisplay.Layout` だけを使う;
- visible native item を複製しない;
- hidden logical layer の item だけ collapsed owner row に timing band として描く;
- Group 補助背景は fold の影響を受ける GroupRange のみ描く;
- native item の Top / Height を summary のために書き換えない;
- 新しい永続状態、Undo、structural observer、timer は追加しない;
- 通常の unfolded 状態では visual-summary item 走査を即時スキップする。

## 2. Pure visual projection

Run:

`35808863868`

Result:

- `PASS_S5_VISUAL_SUMMARY`
- **17/17 PASS**

Covered:

- hidden item only -> timing band;
- visible owner-row item is not duplicated;
- nested collapsed folder -> outer visible owner;
- inner-only collapse -> inner visible owner;
- zero-length item ignored;
- GroupRange -> unique folded visual rows;
- Group entirely inside a collapsed folder -> owner row only;
- nested folds do not duplicate Group visual rows;
- invalid layer/frame/range/owner rejected.

## 3. Exact-host X / zoom / overlay surface

Run:

`35808863785`

Both YMM4 **4.55.1.1 Lite** and **4.56.1.0 Lite**:

- `PASS_S5_COORDINATE_DISCOVERY`;
- `TimelineItemViewModel.Left` = public `double` getter;
- `TimelineItemViewModel.Width` = public `double` getter;
- `TimelineZoom.Value` = public get/set surface;
- `host.Source` has an AdornerLayer;
- Frame 120 / Length 80 at zoom 100 -> Left 120 / Width 80;
- zoom 150 -> Left 180 / Width 120;
- Frame 360 / Length 120 at zoom 100 -> Left 360 / Width 120;
- horizontal scroll to 120 changes viewport/scroll offset but not content-space Left/Width;
- unrealized offscreen TimelineItemView is not required because geometry is read from the Timeline item VM collection.

Artifacts:

- 4.55.1.1: `10728274700`, `sha256:cc3e254ebc6e80d0f76be4f87d49548883469763fa158641b89becca9b3fd6f0`;
- 4.56.1.0: `10728778990`, `sha256:cbd4ff7e0784785505493f7654785531a12bf0cad0c5d762e8cd6d7dc125c310`.

## 4. Dual-host visual integration

Run:

`35808863883`

### YMM4 4.55.1.1 Lite

Job:

`107015696269`

Result:

- `PASS_S5_INTEGRATION`;
- timing bands = 2;
- folded Group visual segments = 3;
- timing X / Width / owner-row Y exact;
- zoom follow = true;
- horizontal-scroll content alignment = true.

Evidence artifact:

- `10728379446`
- `sha256:21ed7ddf0cad0bba35979bb2f428b4a3b0a90e1ee775602402aa7e94f880b2d3`

### YMM4 4.56.1.0 Lite

Job:

`107015696414`

Result:

- `PASS_S5_INTEGRATION`;
- timing bands = 2;
- folded Group visual segments = 3;
- timing X / Width / owner-row Y exact;
- zoom follow = true;
- horizontal-scroll content alignment = true.

Evidence artifact:

- `10729006922`
- `sha256:297ccd93fdc3113d406a203ef18eba6b38aa886e3065a9b773c5fb138e4f1c5a`

The evidence artifact includes `smoke/s5-view.png`.

Visual inspection of the accepted 4.56.1.0 image confirmed:

- hidden item timing bands are visible inside the collapsed owner row;
- Group compatibility overlay is confined to folded visual rows;
- no large vertical Group background remains visible in the representative case;
- normal visible item geometry is not replaced by the plugin summary.

The inspected native owner `TimelineItemView` had height 32 px in both pinned hosts. No additional native Group-background suppression was justified by this representative case.

## 5. Regression at the accepted S5 source

The same source also passed:

- S0 integration: run `35808863846`;
- S1 integration: run `35808863920`;
- S2 integration: run `35808863996`;
- S3 integration: run `35808863821`;
- S4 integration: run `35808863894`;
- hands-on candidate: run `35808863922`;
- integrated Track C: run `35808863980`, **134/134 on both hosts**;
- Track C structural integration: run `35808863872`, **353/353 on both hosts**;
- real .ymme package/install/startup: run `35808863869`.

Real .ymme:

- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`;
- SHA256:
  `d3090cad37d42ff3c3b3ba542dab79f92dfea74fbc67df2624449c3084c8af08`;
- package artifact `10728484404`,
  `sha256:9ac551b72a85d17bc6f40a36aad5622c749d7faedb919447282683072c2915cd`;
- evidence artifact `10729206468`,
  `sha256:fd75f6e3f588d129df9875e97e9c0a2bcfb57fef392295afed13e00c1b6e36cc`.

No Harmony assembly is packaged or loaded by the candidate.

## 6. Explicit limitations carried into P5/P6

S5 proves the visual mechanism on the representative GroupItem fixture and pinned hosts. It does not by itself prove every YMM4 item/plugin implementation uses identical visual behavior.

Deferred:

- representative compatibility across Voice / Text / Image / Video / Audio / Shape / special items;
- third-party item implementations;
- larger-project redraw/performance budgets;
- long-session subscription/lifecycle soak;
- additional cosmetic styling beyond the functional timing/group summary.

These are P5 compatibility / P6 hardening concerns, not reasons to reopen S5 visual ownership.

## 7. Exit decision

S5 is **COMPLETE / FROZEN**.

The S0-S5 convenience/visual implementation satisfies the P4 Full feature gate and advances to the P4 Full freeze record and then P5 Compatibility Coverage.
