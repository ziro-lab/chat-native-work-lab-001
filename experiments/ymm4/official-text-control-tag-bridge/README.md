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
