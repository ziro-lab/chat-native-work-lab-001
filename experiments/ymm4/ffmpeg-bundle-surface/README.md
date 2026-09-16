# YMM4 FFmpeg bundle surface

## Question

On YMM4 v4.56.1.0 Lite, are `ffmpeg.exe` and `ffprobe.exe` physically included in the official distribution, and does the loaded YMM4 assembly expose an obvious Plugin-usable FFmpeg/FFprobe path or manager surface?

## Method

The workflow downloads the exact official Lite ZIP, verifies its SHA256, expands it, records every path containing `ffmpeg` or `ffprobe`, then compiles a small reflection probe against the extracted host. The reflection probe lists YMM4 types and members whose names/signatures mention FFmpeg/FFprobe.

## Boundary

A file-list result answers only what is present in the official ZIP. A reflection hit is discovery, not yet proof that invoking it is a supported Plugin API. If YMM4 downloads FFmpeg on demand, a missing executable in the ZIP does not mean YMM4 cannot manage its own FFmpeg later.
