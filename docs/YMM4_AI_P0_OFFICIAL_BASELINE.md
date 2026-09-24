# YMM4 AI P0 — Current Official Development Baseline

This document records the small set of plugin-development facts that are safe to present to a coding agent as the current official/public baseline.

It intentionally does **not** include undocumented host lifecycle behavior or broad implementation advice.

## Status

- Inspected: **2026-09-25**
- Official documentation: [プラグインを作成する](https://manjubox.net/ymm4/faq/plugin/how_to_make/)
- Official samples: [manju-summoner/YukkuriMovieMaker4PluginSamples](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples)
- Official sample commit inspected: [`8e06e7247bc4a9d870c604927559b8be9a8e4910`](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples/commit/8e06e7247bc4a9d870c604927559b8be9a8e4910)

## Project baseline — OFFICIAL-CONTRACT

For YMM4 v4.47.0.0 and later, the official development page/sample instruct plugin projects to use:

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

The official migration/sample material states that YMM4 moved to .NET 10 at v4.47.0.0.

### Safe AI rule

When generating a new plugin project for current YMM4, start from the current official .NET 10 target above unless the user explicitly targets an older YMM4 release.

Do not silently generate a net8/net9 project from older examples.

## Core references — OFFICIAL-CONTRACT

The official development page tells developers to add references to the YMM4 installation's:

- `YukkuriMovieMaker.Plugin.dll`
- `YukkuriMovieMaker.Controls.dll`

and to add further YMM4-folder assemblies when compilation reports a missing assembly/reference.

### Safe AI rule

Do not assume every plugin needs every YMM4/Vortice/SharpGen assembly.

Start from the assemblies required by the chosen public/sample surface and add dependencies when the actual types require them.

## Official sample reference pattern — OFFICIAL-SAMPLE

The current official sample project references host assemblies through `HintPath` rooted at `$(YMM4DirPath)`.

Its broad sample project currently references, among others:

- `YukkuriMovieMaker.Controls`
- `YukkuriMovieMaker.Plugin`
- `SharpGen.Runtime`
- `SharpGen.Runtime.COM`
- `Vortice.Direct2D1`
- `Vortice.DirectX`
- `Vortice.Mathematics`

This reflects the needs of the combined sample project. It is **not** a statement that every plugin must reference all of them.

## Local YMM4 path — OFFICIAL-SAMPLE

The official sample repository includes `Directory.Build.props.sample`:

```xml
<Project>
  <PropertyGroup>
    <YMM4DirPath>D:\YMM4\</YMM4DirPath>
  </PropertyGroup>
</Project>
```

Its README tells developers to copy this to `Directory.Build.props` and set `YMM4DirPath` to the local YMM4 installation.

### Safe AI rule

Treat `Directory.Build.props` + `YMM4DirPath` as a strong official sample pattern for keeping local installation paths out of the project file.

Do not claim that this exact file layout is the only supported way to build a plugin.

## Build and load — OFFICIAL-CONTRACT / OFFICIAL-SAMPLE

The official documentation states:

1. build the plugin;
2. place the plugin DLL under the YMM4 plugin folder;
3. if the plugin references a DLL that is not present in the YMM4 folder, copy that dependency as well.

The official sample's post-build target copies the plugin DLL into:

`user\plugin\<ProjectName>\`

A successfully loaded plugin appears in YMM4's plugin list.

## Developer mode — OFFICIAL-CONTRACT

For YMM4 v4.33.0.0 and later, the official page/sample documents Developer Mode as able to detect unreleased DirectX objects.

### Safe AI rule

For DirectX/D2D plugin work, recommend Developer Mode as a verification aid. Do not treat it as a substitute for explicit resource-lifetime review.

## Distribution package — OFFICIAL-CONTRACT

The official page/sample documents `.ymme` packaging as:

1. zip the plugin package;
2. rename the zip extension to `.ymme`;
3. distribute the `.ymme` file.

YMM4 can install an `.ymme` package through its installer/associated file handling.

### Boundary

This contract describes package creation/install entry points.

It does **not** by itself prove same-plugin update semantics, preservation of files absent from the new package, overwrite order, migration behavior or rollback. Those belong in version-pinned Lab observations.

## Current official sample coverage — OFFICIAL-SAMPLE

The official sample repository currently lists examples for:

- AudioEffect
- VideoEffect
- AudioSource
- VideoSource
- ImageSource
- Tachie source
- FileWriter
- AudioSpectrum
- Shape
- TextCompletion
- Transition
- Voice
- Localization
- PropertyEditor

### Boundary

This is a sample-repository coverage list, not a complete ontology of every plugin type that YMM4 can host.

For example, Tool / Timeline Tool development is represented elsewhere in the ecosystem/reference material and must not be declared nonexistent merely because the official sample list above does not include it.

## Important non-contract: `Private=false`

The long-form seed guide currently says YMM4 host assembly references **must** use:

```xml
<Private>false</Private>
```

That must **not** be promoted into the current official baseline.

Reason:

- the current official development page does not require it;
- the current official sample `.csproj` does not set `Private=false` on its YMM4/Vortice/SharpGen references.

This does not prove that `Private=false` is bad. It means only that it is not justified as an official YMM4 requirement by the inspected sources.

### Safe AI rule

If a product repository chooses `Private=false` to control output/package contents, document it as repository/build guidance and verify the resulting package/dependency behavior.

Do not tell users that YMM4 requires it unless a current authoritative source or a dedicated compatibility test establishes that requirement.

## Candidate claim not yet promoted: assembly namespace ownership

The seed guide also states that there is no standalone `YukkuriMovieMaker.Commons.dll` and that several YMM4 namespaces live inside `YukkuriMovieMaker.Plugin.dll`.

This may be true for the inspected/current host, but the official baseline sources above do not establish the full namespace-to-assembly mapping.

Keep this as **CANDIDATE / needs current assembly inspection** before making it a canonical AI fact.

## AI baseline checklist

For a new current-YMM4 plugin project:

- [ ] target `net10.0-windows10.0.19041.0`
- [ ] set `UseWPF=true`
- [ ] reference the actual YMM4 installation assemblies required by the selected public surface
- [ ] use a local-path pattern such as the official sample's `YMM4DirPath`
- [ ] copy/install the plugin under `user/plugin/`
- [ ] include only non-host dependencies that are actually needed
- [ ] use Developer Mode for DirectX leak verification when relevant
- [ ] package distribution as `.ymme` when desired
- [ ] do not assume `Private=false` is an official requirement
- [ ] do not infer undocumented update/lifecycle behavior from the packaging instructions
