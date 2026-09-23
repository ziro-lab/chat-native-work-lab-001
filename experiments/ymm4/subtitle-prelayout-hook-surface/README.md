# Subtitle pre-layout hook surface — YMM4 4.56.1.0

## Goal

Inventory the real host surface needed to keep pronunciation-control markers in stored VoiceItem.Serif while filtering them only for subtitle rendering.

This is a discovery probe, not a product implementation.

## Questions

- Are JimakuSource / TextSource public extension points?
- What information does IVideoEffectProcessor receive?
- Does EffectDescription / TimelineItemSourceDescription expose the owning VoiceItem or source text?
- Which public plugin-facing types mention text/subtitle/jimaku/source transformation?

## PASS boundary

PASS means the pinned host surface was inventoried successfully and evidence was emitted. Interpretation is recorded separately; this experiment does not manufacture a public hook if none exists.
