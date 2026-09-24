# YMM4 AI P1B — Official Plugin Surface Map

This document maps the current official sample repository's plugin categories to the public types shown by its sample documentation and implementation.

It exists to prevent coding agents from mixing similarly named interfaces, old prose, and neighboring plugin categories.

## Source

- Official sample repository: [manju-summoner/YukkuriMovieMaker4PluginSamples](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples)
- Inspected commit: [`8e06e7247bc4a9d870c604927559b8be9a8e4910`](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples/commit/8e06e7247bc4a9d870c604927559b8be9a8e4910)
- Inspected: **2026-09-25**

## Surface map

| Area | Official sample entry point | Associated source/parameter shape in sample | Notes |
| --- | --- | --- | --- |
| Audio Effect | `AudioEffectBase` + `[AudioEffect]` | `AudioEffectProcessorBase` | Current sample processor overrides `Hz`, `Duration`, lower-case `seek`, lower-case `read`. |
| Video Effect | `VideoEffectBase` + `[VideoEffect]` | `IVideoEffectProcessor` | Official starter directly implements the interface; shipped/community source also provides a separate `VideoEffectProcessorBase` route. |
| Audio file source | `IAudioFileSourcePlugin` | `IAudioFileSource` | Sample output is always 2ch. |
| Video file source | `IVideoFileSourcePlugin` | `IVideoFileSource` | **Implementation code and current API index agree. The folder README contains a stale/mismatched `IVideoSourcePlugin` label.** |
| Image file source | `IImageFileSourcePlugin` | returns `ID2D1Bitmap?` in sample | Sample demonstrates WIC-backed bitmap creation. |
| Tachie | `ITachiePlugin` | `ITachieSource2`, `TachieCharacterParameterBase`, `TachieItemParameterBase`, `TachieFaceParameterBase` | The sample explicitly uses `ITachieSource2`; do not simplify this to an older similarly named source interface without checking. |
| Video writer | `IVideoFileWriterPlugin` | `IVideoFileWriter` | Output plugin surface. |
| Audio spectrum | `IAudioSpectrumPlugin` | `AudioSpectrumParameterBase`, `IAudioSpectrumSource` | Sample README heading calls the parameter section “IAudioSpectrumParameter” but the described base class is `AudioSpectrumParameterBase`; keep type/category names distinct. |
| Shape | `IShapePlugin` | `ShapeParameterBase`, `IShapeSource` | Polygon sample also demonstrates interactive point editing. |
| Text completion | `ITextCompletionPlugin` | plugin implementation | Current official sample category. |
| Transition | `ITransitionPlugin` | `TransitionParameterBase`, `ITransitionSource` | Scene-transition surface. |
| Voice | `IVoicePlugin` | `VoiceParameterBase`, `IVoiceSpeaker` | Sample uses SAPI5, but the interfaces are the reusable part. |
| Localization | sample/reference feature | resource/localization pattern | Not a separate “one plugin = one interface” category in the same sense as the rows above. |
| PropertyEditor | `PropertyEditorAttribute2` + `IPropertyEditorControl` | `ItemProperty[]`, binding helpers, BeginEdit/EndEdit | Custom editor surface rather than a top-level plugin category. |

## Important official-source inconsistency: VideoSource README vs implementation

At the inspected official sample commit:

- `YMM4SamplePlugin/VideoSource/README.md` says the plugin implements `IVideoSourcePlugin`;
- `SampleVideoSourcePlugin.cs` actually implements **`IVideoFileSourcePlugin`**;
- `SampleVideoSource.cs` implements **`IVideoFileSource`**;
- the current API index also contains `YukkuriMovieMaker.Plugin.FileSource.IVideoFileSourcePlugin` and no corresponding `IVideoSourcePlugin` result.

For exact code generation, use the implementation/API shape:

```csharp
public class SampleVideoSourcePlugin : IVideoFileSourcePlugin
{
    public IVideoFileSource? CreateVideoFileSource(
        IGraphicsDevicesAndContext devices,
        string filePath)
    {
        ...
    }
}
```

### Why this matters

“Official source” is not one indivisible authority level.

An official README can contain stale prose while the implementation in the same commit reflects the actual type used to compile.

The conflict must stay visible rather than being silently reconciled.

## File-source correction to the seed guide

The long-form seed guide currently uses the correct current implementation names:

- `IVideoFileSourcePlugin`
- `IVideoFileSource`
- `IAudioFileSourcePlugin`
- `IAudioFileSource`

That portion should **not** be “corrected” to the stale `IVideoSourcePlugin` README label.

The seed guide's media-source factory/source split is therefore useful, but it should cite the current implementation/API evidence rather than rely only on prose.

## Current video file-source sample shape

The official implementation demonstrates:

```csharp
IVideoFileSource? CreateVideoFileSource(
    IGraphicsDevicesAndContext devices,
    string filePath)
```

and an `IVideoFileSource` exposing:

- `TimeSpan Duration`;
- `ID2D1Image Output`;
- `void Update(TimeSpan time)`;
- `int GetFrameIndex(TimeSpan time)`;
- `Dispose()`.

Its D2D sample also demonstrates that an `Output` object obtained from an effect is disposed explicitly and the effect input is cleared before effect disposal.

Treat those lifetime details as official sample behavior for that implementation, not as a claim about every possible media-source implementation.

## Current audio file-source sample shape

The official implementation demonstrates:

```csharp
IAudioFileSource? CreateAudioFileSource(
    string filePath,
    int audioTrackIndex)
```

and an `IAudioFileSource` with:

- `Duration`;
- `Hz`;
- `Read(float[] destBuffer, int offset, int count)`;
- `Seek(TimeSpan time)`;
- `Dispose()`.

The sample README states that returned audio is always 2-channel.

## Current image-source sample shape

The official sample implements `IImageFileSourcePlugin` and returns an `ID2D1Bitmap?` from its bitmap-creation method.

The WIC implementation is an example, not a requirement that every image plugin use WIC.

## Tachie surface

The current official sample explicitly distinguishes:

- plugin discovery/parameter generation: `ITachiePlugin`;
- rendering/source: `ITachieSource2`;
- character-level parameters: `TachieCharacterParameterBase`;
- item-level parameters: `TachieItemParameterBase`;
- voice/face-item parameters: `TachieFaceParameterBase`.

This distinction matters for AI generation because “Tachie plugin” is not a single class.

## Shape / Transition / Spectrum pattern

These official samples share a useful high-level architecture:

```text
Plugin
  -> creates parameter object
ParameterBase-derived settings
  -> creates/feeds source
Source interface
  -> performs rendering/production
```

Use that as a sample pattern for the relevant category, not as a universal architecture for unrelated plugin types.

## Voice surface

The official Voice sample separates:

- `IVoicePlugin` — enumerates/provides voices;
- `VoiceParameterBase` — holds voice parameters;
- `IVoiceSpeaker` — performs speech synthesis.

The SAPI5 engine is sample-specific. Do not hard-code SAPI5 assumptions into generic Voice plugin guidance.

## AI rule: resolve source conflicts by claim type

When YMM4 sources disagree, do not apply one global “most authoritative source wins” rule.

Use the source appropriate to the claim:

### Exact symbol / signature / compile shape

Prefer:

1. current inspected implementation that actually uses the symbol;
2. current API/assembly index;
3. official prose/sample README as supporting explanation.

If prose and implementation disagree, record the mismatch.

### Intended setup / supported development workflow

Prefer:

1. current official documentation;
2. current official samples;
3. current ecosystem references.

### Undocumented runtime behavior

Prefer:

1. version-pinned Lab native/static/manual evidence within its PASS boundary;
2. implementation/reference source only as a hypothesis or precedent.

### Product behavior

Require downstream product acceptance when integration failure matters.

No source-class promotion should erase a conflicting lower-level fact.
