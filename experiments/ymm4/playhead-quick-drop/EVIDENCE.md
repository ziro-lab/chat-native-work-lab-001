# Evidence — YMM4 Playhead Quick Drop

Audited native run:

- workflow run: `34833809202`
- source head: `d1a9ecf53ba7feabc9ddcb4742bf0113dc0271c9`
- host: YMM4 4.55.1.1 Lite
- host ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- result: `PASS_PLAYHEAD_QUICK_DROP`
- artifact ID: `10342743206`
- artifact ZIP SHA256: `0b062155d523d9069f9dbb34afdbc479053fca7c16541a421083b2bc10017dbd`

Assertions:

```text
public_frame_property=CurrentFrame
before_frame=0
playhead_frame=321
clone_frame=321
clone_length=37
clone_layer=12
independent_clone=True
placed=True
frame_matches=True
length_preserved=True
source_unchanged=True
```

This proves the Plugin-facing public `Timeline.CurrentFrame` route and live Template clone placement semantics. It does not prove physical mouse input.
