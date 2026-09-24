# YMM4 bundled FFmpeg has a public in-host locator on the tested host

- Status: evidence-qualified
- Repository state: main
- Knowledge class: LAB-NATIVE + LAB-STATIC
- Surface: S1
- YMM4 version: 4.56.1.0 Lite x64
- Revalidation trigger: YMM4 changes bundled FFmpeg layout or FFmpegResourceLocator

## Claim

On the tested official YMM4 4.56.1.0 Lite x64 distribution:

- `ffmpeg.exe` and `ffprobe.exe` were bundled under `Resources\bin\x64\ffmpeg`;
- public `YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator` exposed:
  - `GetFFmpegDirectory()`;
  - `GetFFmpegDllDirectory()`;
  - `GetFFmpegExePath()`;
  - `GetUserFFmpegDirectory()`;
- an actual Plugin running inside YMM4 resolved the real bundled FFmpeg path through the public locator;
- no separate public `GetFFprobeExePath()` was found, but the verified sibling `ffprobe.exe` existed in the returned directory.

## Safe use

A Plugin targeting this verified host can prefer YMM4's public FFmpeg locator over requiring the user to configure a second external FFmpeg installation.

Derive/check the sibling ffprobe path only with existence validation.

## Do not infer

- The same files/paths are guaranteed in future YMM4 versions.
- The locator behaves correctly when invoked from an unrelated console process; the product-relevant proof is in-host.
- A public ffprobe-specific locator exists.

## Failure behavior

If the locator or expected executable is unavailable, stop/fall back explicitly. Do not silently guess YMM4 installation-relative paths.

## Evidence

- Lab experiment: [ffmpeg-bundle-surface](../../experiments/ymm4/ffmpeg-bundle-surface/)
- Host ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Source head: `21090626faeb7985f964a26c4a57b8f301255a88`
- Workflow run: `35123682432`
- Artifact: `10458179237`
- Artifact SHA256: `c3b8acfc064388731ad33a6493ed07bf53f43abed5ec1b03d97116b54b9559cb`
