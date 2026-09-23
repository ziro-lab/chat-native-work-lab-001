# Filtered JimakuSource composition — YMM4 4.56.1.0

## Goal

Test a Harmony-free render-time marker hiding design.

Stored VoiceItem.Serif remains unchanged. A custom Jimaku video effect ignores the already-rendered marker-bearing input and re-renders a temporary VoiceItem with control markers removed through YMM4's own internal JimakuSource, reached only through reflection.

## Cases

- baseline: VoiceItem Serif = AB
- visible: VoiceItem Serif = A§B
- filtered: VoiceItem Serif = A§B + custom subtitle effect; temporary render clone Serif = AB

## PASS boundary

PASS requires the custom effect processor to execute, the original stored Serif to remain A§B, and the filtered output width to approximately match baseline while being materially narrower than the visible-marker output.

No Harmony patching is used.
