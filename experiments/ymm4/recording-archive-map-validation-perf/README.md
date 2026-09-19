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
