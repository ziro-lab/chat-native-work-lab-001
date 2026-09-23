# TextDecoration zero-scale marker probe — YMM4 4.56.1.0

## Goal

Test a low-intrusion subtitle-hiding mechanism for pronunciation control markers:

> Keep the marker character in the stored text, but apply a TextDecoration with Scale=0 to only that character so it disappears from the rendered subtitle and ideally consumes no horizontal width.

The probe renders real YMM4 TextItem/TextSource output and compares local image bounds.

## Cases

- baseline: AB
- visible marker: A§B
- hidden marker: A§B with a TextDecoration on § only, Scale=0

## PASS boundary

PASS requires:

1. all three cases render successfully through the real YMM4 TextSource;
2. the visible-marker case is wider than baseline;
3. the zero-scale marker case is materially narrower than the visible-marker case;
4. the zero-scale marker case is approximately the same width as baseline.

This proves a viable rendering primitive, not yet the final VoiceItem subtitle integration.
