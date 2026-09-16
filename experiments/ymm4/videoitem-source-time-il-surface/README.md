# VideoItem source-time IL surface

## Question

In YMM4 v4.56.1.0, which native methods consume `VideoItem.PlaybackRate2`, `Animation.GetValue(...)`, and `VideoItem.ContentOffset` on the playback/source path?

## Method

A native-host probe walks method bodies in the loaded YukkuriMovieMaker assemblies, resolves IL metadata tokens, and records methods that reference the source-time inputs. For hit methods it also emits a compact resolved IL listing so arithmetic/constants and downstream call targets can be inspected without an external decompiler.

## PASS boundary

A PASS proves only that the relevant native consumers can be identified by static IL inspection for the exact host version.

## NOT PROVEN

Static IL references do not by themselves prove runtime frame identity. Any inferred formula still needs either a narrow behavioral check or a rendered clock-frame proof before being treated as universal.
