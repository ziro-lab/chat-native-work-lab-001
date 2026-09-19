# Recording Archive all-frame PlaybackRateMap validation cost

## Question

How expensive is the product-style all-frame PlaybackRateMap reflection loop on the YMM4 v4.56.1.0 host, and how much of that cost comes from resolving MethodInfo on every frame?

## Method

On the real YMM4 UI thread, the probe performs source-time mapping with the same reflection shape used by Recording Archive:

- 36,000 calls: one 10-minute 60 fps item, one validation pass;
- 360,000 calls: ten such items;
- cached-MethodInfo comparison for the same 360,000 calls.

It records elapsed time and calls/second. Timing is observational and runner-specific; the workflow does not claim a universal latency budget.

## PASS boundary

PASS means the benchmark completed and reported host-specific timings. Product UX conclusions must treat the numbers as directional, not guaranteed user-PC performance.

## Result

Final-run host timing:

- 36,000 uncached reflective calls: approximately **0.033 s**
- 360,000 uncached reflective calls: approximately **0.209 s**
- 360,000 cached-MethodInfo calls: approximately **0.087 s**
- 7.2 million uncached calls extrapolate to roughly **4.2 s** on the hosted runner (individual successful runs varied roughly 3.4–5.4 s)

The mapping loop itself is therefore not currently a primary performance blocker. Caching MethodInfo remains an inexpensive optimization if needed.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
