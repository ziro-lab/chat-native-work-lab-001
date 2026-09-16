# YMM4 FFmpeg bundle surface

## Question

On YMM4 v4.56.1.0 Lite, are `ffmpeg.exe` and `ffprobe.exe` physically included in the official distribution, and does the loaded YMM4 assembly expose an obvious Plugin-usable FFmpeg/FFprobe path or manager surface?

## Result

Verified on the exact official YMM4 v4.56.1.0 Lite ZIP (SHA256 `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`).

Both executables are bundled exactly once:

```text
Resources\bin\x64\ffmpeg\ffmpeg.exe
Resources\bin\x64\ffmpeg\ffprobe.exe
```

The extracted host also contains `YukkuriMovieMaker.Plugin.FileSource.FFmpeg.dll` and `YukkuriMovieMaker.Interop.FFmpeg.dll`.

Reflection discovery found the public type:

```text
YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator
```

with public static methods:

```text
GetFFmpegDirectory()
GetFFmpegDllDirectory()
GetFFmpegExePath()
GetUserFFmpegDirectory()
```

`GetBundledFFmpegDirectory()` and `IsValidFFmpegDirectory(string)` are non-public.

There is no separately discovered public `GetFFprobeExePath()` method in this scan. Because `ffprobe.exe` is physically beside `ffmpeg.exe` in the verified bundle, a downstream Plugin can potentially derive it from the directory returned by the public locator, but invocation/return-value behavior should be verified in-host before product adoption.

## Evidence

- Workflow run: `35122995526`
- Job: `104885123255`
- Artifact: `10458023169` (`ymm4-ffmpeg-bundle-surface`)
- Artifact SHA256: `215e6ace8edfa27add9d0beb32c925f05fab923b74e4f013d557e14531e8f338`

## Method

The workflow downloads the exact official Lite ZIP, verifies its SHA256, expands it, records every path containing `ffmpeg` or `ffprobe`, then compiles a small reflection probe against the extracted host. The reflection probe lists YMM4 types and members whose names/signatures mention FFmpeg/FFprobe.

## Boundary

This proves the files are present in the pinned official distribution and that the named locator surface exists. The reflection process hit some unrelated WPF assembly-load mismatches outside the FFmpeg plugin assembly; the FFmpeg resource-locator type and methods above were still discovered successfully. It does not yet prove that calling the locator from an ordinary third-party Plugin returns the desired paths under every install mode or future YMM4 version.
