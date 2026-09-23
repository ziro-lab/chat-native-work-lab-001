# Official text-control-tag bridge — YMM4 4.56.1.0

## Goal

Validate whether YMM4's built-in text-control tags can be reused as the pronunciation-assist marker transport without custom subtitle hiding.

Candidate marker: `<w0>`

Official semantics: wait zero seconds. The stored Serif keeps the tag; YMM4 should interpret the tag rather than render it as visible text.

## Native questions

1. Does real YMM4 render `A<w0>B` with the same layout width as `AB`?
2. Does an invalid tag remain visible / affect layout, proving the valid tag is actually parsed rather than coincidentally ignored?
3. Can the same behavior be observed through the VoiceItem/JimakuSource path, not only TextItem/TextSource?
4. Does Serif remain unchanged after rendering?
5. What does the real `VoiceItem.SerifToHatsuonAsync` path do with `<w0>` in this clean host?

## PASS boundary

The required PASS boundary is the official rendering transport:

- valid control tag is layout-invisible;
- invalid tag is not silently removed;
- stored source text remains intact;
- VoiceItem/JimakuSource path is preferred when the clean host allows it.

The pronunciation conversion observation is recorded separately because a clean CI YMM4 may not have a configured voice provider.

No Harmony is used.


## Phase 2 — direct official parser surface

The next native acceptance slice calls YMM4's own `YukkuriMovieMaker.Commons.ControlTagParser.GetPlainText(string)` directly.

Required behavior:

- the parser type is public;
- `GetPlainText` is public;
- `A<w0>B` becomes `AB`;
- an invalid tag remains unchanged.

If these assertions pass, the pronunciation controller can obtain the clean text from YMM4 itself rather than maintaining a duplicate tag-removal parser.


## Native final result

Final run: `35857329536`  
Job: `107169063766`  
Source HEAD: `01e7baf17adbd093dc23a872e3498018f545986c`

**14/14 required assertions PASS** on real YMM4 Lite 4.56.1.0.

### Rendering

TextSource:
- `AB`: 41.4209 px
- `A<w0>B`: 41.4209 px
- invalid `A<cnwl_invalid>B`: 267.35156 px

JimakuSource / VoiceItem:
- `AB`: 45.35547 px
- `A<w0>B`: 45.35547 px
- invalid `A<cnwl_invalid>B`: 268.38086 px
- stored Serif after rendering remained exactly `A<w0>B`

Therefore the official valid control tag is removed before subtitle layout while the stored Serif remains untouched.

### Official parser API

Real host exposes:

- `YukkuriMovieMaker.Commons.ControlTagParser` — public
- assembly: `YukkuriMovieMaker.Plugin`
- `public static string GetPlainText(string)`

Observed:
- `GetPlainText("A<w0>B") == "AB"`
- `GetPlainText("A<cnwl_invalid>B") == "A<cnwl_invalid>B"`

This means product code can use YMM4's own control-tag parser rather than maintaining a duplicate tag-stripping implementation.

### Pronunciation note

`VoiceItem.SerifToHatsuonAsync()` exists and was invokable in the clean host, but the synthetic VoiceItem has no configured voice provider, so both baseline and tagged Hatsuon were empty. This experiment therefore does not claim a real VOICEVOX synthesis result.

YMM4's official 4.52.0.0 release notes separately state that the issue where control tags in Serif were included in speech was fixed.

### Product consequence

For a boundary marker such as `<w0>`:

- keep the marker in Serif as an editing cue;
- let YMM4's normal JimakuSource hide it in finished subtitles;
- use `ControlTagParser.GetPlainText` when product code needs the official clean text;
- inspect the original Serif first when the pronunciation-assist controller needs marker positions;
- no custom subtitle renderer and no Harmony are required for this boundary-marker path.
