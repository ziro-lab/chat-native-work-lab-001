# YMM4 FFmpeg bundle surface

## Question

On YMM4 v4.56.1.0 Lite, are `ffmpeg.exe` and `ffprobe.exe` physically included in the official distribution, and can an ordinary Plugin resolve them through YMM4's own public FFmpeg locator?

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

`GetBundledFFmpegDirectory()` and `IsValidFFmpegDirectory(string)` are non-public. There is no separately discovered public `GetFFprobeExePath()` method.

## In-host behavior

A dedicated `ILocalizePlugin` called the public locator from inside the real YMM4 v4.56.1.0 process. The host returned:

```text
base_directory=<YMM4 root>\
ffmpeg_directory=<YMM4 root>\Resources\bin\x64\ffmpeg
ffmpeg_directory_exists=True
ffmpeg_exe=<YMM4 root>\Resources\bin\x64\ffmpeg\ffmpeg.exe
ffmpeg_exe_exists=True
derived_ffprobe=<YMM4 root>\Resources\bin\x64\ffmpeg\ffprobe.exe
derived_ffprobe_exists=True
```

Therefore a Plugin running inside this pinned YMM4 build can use `GetFFmpegDirectory()` / `GetFFmpegExePath()` and derive the verified sibling `ffprobe.exe` without asking the user for an external tool directory.

For comparison, invoking the same locator from an unrelated console process resolved relative to that console process's `AppContext.BaseDirectory`; those paths did not exist. This is expected and is why the product-relevant proof is the real in-host invocation.

## Evidence

Final in-host proof:

- Source head: `21090626faeb7985f964a26c4a57b8f301255a88`
- Workflow run: `35123682432`
- Job: `104887398283`
- Artifact: `10458179237` (`ymm4-ffmpeg-bundle-surface`)
- Artifact SHA256: `c3b8acfc064388731ad33a6493ed07bf53f43abed5ec1b03d97116b54b9559cb`

## Method

The workflow downloads the exact official Lite ZIP, verifies its SHA256, expands it, records every path containing `ffmpeg` or `ffprobe`, reflects the YMM4 assemblies for FFmpeg-related surfaces, then builds and installs a small probe Plugin into the temporary YMM4 host. The Plugin writes the actual locator return values and existence checks from inside YMM4.

## Boundary

This proves the bundled paths and public locator behavior for YMM4 v4.56.1.0 Lite in the tested x64 host. It does not claim future YMM4 versions keep the same files or locator behavior. Downstream products should revalidate when raising their pinned YMM4 compatibility version and should stop rather than silently guessing if the locator or sibling `ffprobe.exe` is unavailable.
