# Recording Archive Unicode and long absolute path

## Question

Can YMM4 v4.56.1.0 load/save a normal VideoItem whose absolute media/project paths contain Japanese text, emoji, spaces, brackets, and a long directory segment?

## PASS boundary

PASS requires real-host ContentLength loading plus SaveProject/LoadProjectFile roundtrip with the exact absolute media path preserved.

## Result

The standard Unicode test passed with media/project absolute paths around 200 characters. A second test intentionally exceeded classic MAX_PATH and also passed:

- media path: **402 characters**
- project path: **407 characters**
- exact media path preserved
- VideoItem content loaded successfully

No product-side 260-character rejection is justified by this v4.56.1.0 evidence.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
