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


## Native slice 1 result — zero is not hidden

Run 35844795943 on real YMM4 Lite 4.56.1.0 showed:

- AB: 41.4209 px
- A§B: 56.6611 px
- A§B with § Scale=0: 57.1094 px

Therefore Scale=0 is not a width-collapse operation in YMM4's text renderer. The next slice tests small positive scales (0.01 / 0.001) with transparent foreground; zero remains a negative control.


## Native slice 2 result — tiny/transparent scale also does not collapse layout

Run `35845054587`, real YMM4 Lite 4.56.1.0:

- baseline `AB`: 41.4209 px
- visible `A§B`: 56.6611 px
- transparent marker, Scale=0: 57.1094 px
- transparent marker, Scale=0.01: 57.1094 px
- transparent marker, Scale=0.001: 57.1094 px

The marker's layout advance is unchanged. `TextDecoration.Scale` is therefore rejected as a hidden-control-marker mechanism.

Artifact `10743325493`, SHA256 `7705b2d08c7828d82a8a3ade2774fe9f9cea632538e0cb88e8db186ebf5c45f3`.

### Design consequence

Keep `TextDecoration` out of the product path for marker removal. The next route must filter/replace the text before layout, or use another zero-advance representation proven independently.
