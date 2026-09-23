# YMM4 no-Harmony Full — P5 Compatibility Coverage Freeze

作成日: 2026-09-23  
状態: **COMPLETE / FROZEN**

P5の目的は、P4 Fullで凍結した共通Folder/Display/Undo経路が、YMM4の代表built-in item、実リソース付きitem、特殊item、第三者custom itemでもそのまま成立するかを確認することだった。

P5は型ごとの新しいfolder architectureを設計する工程ではない。具体的な互換性破綻が見つかった場合だけ、最小の境界追加を検討する方針で実施した。

## 1. Accepted runtime / evidence source

最後の製品runtime変更:

`9eebeda810d043d9f944e7d3427eb0358cd091fe`

P5最終受入証拠HEAD:

`9217a307ee9aed8d6e48d0fc2fec6fa49d1e6179`

P5 branch:

`work/ymm4-no-harmony-p5-compatibility`

PR:

- #115 `p5: build representative item compatibility matrix`

Pinned exact hosts:

- YMM4 4.55.1.1 Lite;
- YMM4 4.56.1.0 Lite.

No Harmony was introduced.

## 2. P5.1 — exact-host built-in item inventory

Workflow:

`35845819244`

Both hosts:

- `PASS_P5_ITEM_INVENTORY`;
- IItem-compatible types = **19**;
- concrete built-in IItem types = **13**;
- the concrete type set and public constructor surface are identical across both pinned hosts;
- all 13 expose common public BaseItem `Frame / Length / Layer` get/set.

Concrete types:

- `AudioItem`
- `EffectItem`
- `FrameBufferItem`
- `GroupItem`
- `ImageItem`
- `SceneItem`
- `ShapeItem`
- `TachieFaceItem`
- `TachieItem`
- `TextItem`
- `TransitionItem`
- `VideoItem`
- `VoiceItem`

Artifacts:

- 4.55.1.1: `10742804966`, `sha256:8e42e1843a4d592f89d7069dcb2c8a906327569137d8292fd1002f9e62ff8836`;
- 4.56.1.0: `10743795592`, `sha256:1ebfe6fed83337c00b06856c1e4ae0b8787db41ea9aa6192d0ec320cb2ebcccd`.

## 3. P5.2 — zero-resource fixture viability

Workflow:

`35845819404`

Both hosts:

- `PASS_P5_FIXTURE_VIABILITY`;
- construct = **13/13**;
- `Timeline.TryAddItems` = **13/13**;
- live Timeline items = **13/13**;
- no Harmony.

The standalone probe's realized TimelineItemViewModel count is not used as a compatibility verdict because YMM4 virtualizes the timeline viewport.

Artifacts:

- 4.55.1.1: `10743676665`, `sha256:cd2c6c67f72b76d4e568b018155e1aae25c56183620f060c05d07265708af04f`;
- 4.56.1.0: `10742768441`, `sha256:bdb38534a962820b31c44bb89dd168686cd414d5dcae20c454286c6c39504e11`.

## 4. P5.3 — common built-in product compatibility

Workflow:

`35845819269`

Both hosts:

- `PASS_P5_COMMON_BUILTINS`;
- construct / add / live = **13/13**;
- common public Timeline item VM geometry = **13/13**;
- FoldMap owner mapping = **13/13**;
- hidden timing summary = **13/13**;
- folder item selection = **13/13**;
- no item-type-specific folder adapter.

A zero-resource VoiceItem has no configured speaker and therefore is not a valid history fixture. After removing only that invalid resource fixture, the remaining **12/12** built-ins passed:

- explicit folder block move;
- shared Layer mapping;
- one-step Undo;
- one-step Redo;
- common geometry after move.

Candidate SHA256:

- 4.55.1.1: `b7a7f978875b2d9912bd97d52cd484e0d8524542f2181a69d7aa0081bee39bd4`;
- 4.56.1.0: `9e3534842ba63a036d24794ca8aa62ef0e4ed9ea06b6e297eca2a5c8c90fd1f2`.

Artifacts:

- 4.55.1.1: `10743301999`, `sha256:19f632655b9831e7fa981e61b5b48f7625d64e6ee8b2e50a265dd08ace58c974`;
- 4.56.1.0: `10743292067`, `sha256:7658c35d2932c0c55ffbed370e8b49e8438b5225fc965f53310162a6d92afc93`.

## 5. P5.4 — real media-backed compatibility

Workflow:

`35845819420`

The CI creates deterministic local fixtures:

- PNG;
- PCM WAV;
- H.264 MP4.

Both hosts:

- `PASS_P5_MEDIA_REALISM`;
- real-file constructor / add / live / geometry = **3/3**;
- FoldMap owner mapping = **3/3**;
- hidden timing summary = **3/3**;
- folder item selection = **3/3**;
- explicit block move + Undo/Redo = **3/3**;
- no media-specific folder adapter.

Fixture SHA256:

- PNG: `785090597e739d0ec824dc66223d33f26ae4be8da5fc5711f202d5a41dafffb5`;
- WAV: `3fa20276d8a4131431490ed129d0b05fafa4e9c82d4339b5cf635512103d6615`;
- MP4: `507246f89713a5f008a495371976daf4a4afdf9ed67ec9236b4257f91ffbbb9b`.

Artifacts:

- 4.55.1.1: `10743890567`, `sha256:c91b26165927e499cb14b4c300dac77f1e27772d3b74cfe67dc17a6eca2c8058`;
- 4.56.1.0: `10742814810`, `sha256:9a8aca2c006d47d3ac1e92b313e317c9e0001a9a5f42a1ce12980cb8c8bcd55a`.

## 6. P5.5 — configured Voice compatibility

Voice setup discovery confirmed matching public configuration surfaces on both hosts:

- `Character.Voice`;
- `Character.VoiceParameter`;
- bundled Community `RecordedVoiceSpeaker`;
- `RecordedVoiceParameter`;
- `VoiceDescription(IVoiceSpeaker)`.

The acceptance fixture uses YMM4's bundled Recorded Voice implementation with a deterministic local WAV, so no external TTS service/account/network dependency is required.

Workflow:

`35845819363`

Both hosts:

- `PASS_P5_CONFIGURED_VOICE`;
- construct / add / live = **1/1**;
- common public geometry = **1/1**;
- FoldMap owner mapping = **1/1**;
- hidden timing summary = **1/1**;
- folder item selection = **1/1**;
- native history Record boundary = PASS;
- explicit block move + one-step Undo/Redo = **1/1**;
- no Voice-specific folder adapter.

Voice WAV SHA256:

`3fa20276d8a4131431490ed129d0b05fafa4e9c82d4339b5cf635512103d6615`

Artifacts:

- 4.55.1.1: `10743666802`, `sha256:82cd6f10ac396f725ae5d493f1b175f004304323a88041513b46a2e760ae0ca3`;
- 4.56.1.0: `10743336836`, `sha256:4b52b127aee2ac28fc57990e7048c6d087efa36bdc844589562bf74368c7b629`.

## 7. P5.6 — third-party custom item compatibility

Representative third-party plugin:

- repo: `Dolphin-kun/YMM43D`;
- source commit: `a5fe44443d9b62912dec6861edf68cbdff9e7810`;
- license: MIT;
- representative type: `YMM43D.Project.Items.LightItem : BaseItem`.

The workflow fetches the pinned source, verifies commit/license, builds it against each exact YMM4 host, rejects Harmony in the third-party output, installs it through the normal plugin path, and exercises the custom item against the frozen folder product.

Workflow:

`35845819534`

Both hosts:

- `PASS_P5_THIRD_PARTY`;
- construct / add / live = **1/1**;
- common public geometry = **1/1**;
- FoldMap owner mapping = **1/1**;
- hidden timing summary = **1/1**;
- folder item selection = **1/1**;
- explicit block move + one-step Undo/Redo = **1/1**;
- no type-specific folder adapter.

YMM43D.dll SHA256:

- 4.55.1.1 build: `f3b3f355d4cea2179af95a84076b6886f677f5ae6761de1647786d820ef4aff5`;
- 4.56.1.0 build: `c8e108ad49dbf5bbfe7debc64d070819c3cd61a0a1c8d99cc458914f47dedcfc`.

Artifacts:

- 4.55.1.1: `10742907908`, `sha256:6c4c0f67ea0bcf151b05922e8c4c732500c83b029e7ecb922603cdb40cb6f2ce`;
- 4.56.1.0: `10743057676`, `sha256:07b962c48b38ba12b098e629148e91a021fc622a8fd9f7b1678f629aa5729cb1`.

## 8. Same-HEAD regression / package acceptance

P5 final evidence HEAD:

`9217a307ee9aed8d6e48d0fc2fec6fa49d1e6179`

At this HEAD all of the following were GREEN:

- P5 item inventory;
- P5 fixture viability;
- P5 common built-ins;
- P5 real media;
- P5 configured Voice;
- P5 third-party;
- P5 Voice setup discovery;
- S0 integration;
- S1 integration;
- S2 integration;
- S3 integration;
- S4 integration;
- S5 integration;
- S5 coordinate discovery;
- hands-on candidate;
- real .ymme package/install/startup.

Real .ymme:

- `PASS_P4_YMME_INSTALL`;
- `PASS_P4_YMME_STARTUP`;
- SHA256:
  `9e30174b283a4fdcf6705bb86e19f080439f041ea0d21f238b8e432adefd71f7`;
- package artifact `10742944613`,
  `sha256:0ca2975bc5dd556915c6ed62fdce7acaa87c8923388d2cfcbf6638793579c4a2`;
- evidence artifact `10743621732`,
  `sha256:a439bc6e2024033141685f47f2cf1842d62dc27d5fd8643a591b5e87fe6c347f`.

## 9. Compatibility result

P5 found no item family that requires:

- its own FolderDocument;
- its own FoldMap;
- a dedicated structural observer;
- a dedicated Undo stack/path;
- a type-specific timing-coordinate formula;
- a media- or Voice-specific folder state.

The common P4 product path remains authoritative for built-ins, real media, configured Voice, and the representative third-party custom BaseItem.

P5 compatibility differences are setup/resource concerns, not folder-engine ownership concerns.

## 10. Explicit P6 boundary

P5 does not make large-project or long-session performance claims.

Deferred to P6:

- large layer/item/folder counts;
- deep valid nesting;
- repeated fold/structural cycles;
- long idle;
- scroll/zoom/resize soak;
- project switching;
- detach/reconnect cleanup;
- failure injection;
- subscription/refresh/mutation budgets.

## 11. Exit decision

P5 Compatibility Coverage is **COMPLETE / FROZEN**.

The active completion path advances to:

**P6 — Hardening & Performance.**
