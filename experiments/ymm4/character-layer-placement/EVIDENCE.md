# Evidence — YMM4 Character Front / Back Layer Placement

Audited native run:

- workflow run: `34833832472`
- source head: `a6e03a3a4f3a4a91010a34a6e66d4e1e4a92627d`
- host: YMM4 4.55.1.1 Lite
- host ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- result: `PASS_CHARACTER_LAYER_PLACEMENT`
- artifact ID: `10342967418`
- artifact ZIP SHA256: `162a0cc7efedb62d396fe47834d9ceab7034f65f63aa16601faf24484e431146`

Assertions:

```text
same_character_min=10
same_character_max=22
front_layer=24
back_layer=8
front_length=30
back_length=30
front_late_blocker_test=True
back_late_blocker_test=True
other_character_layer40_ignored_for_baseline=True
```

The blockers at Layers 23 and 9 begin after the requested item's start frame, so the proof specifically demonstrates whole-span collision checking rather than start-frame-only checking.
