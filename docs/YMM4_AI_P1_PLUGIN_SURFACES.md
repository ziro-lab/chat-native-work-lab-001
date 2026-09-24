# YMM4 AI P1 — Public and Reference Plugin Surfaces

This document extracts high-value plugin surfaces from the long-form seed guide while preserving the difference between:

- current official sample patterns;
- current public/API-index information;
- shipped/community implementation patterns;
- version-sensitive behavior that still belongs in the Lab.

It is intentionally not a single "one true architecture."

## Sources inspected

- Official samples commit: [`8e06e7247bc4a9d870c604927559b8be9a8e4910`](https://github.com/manju-summoner/YukkuriMovieMaker4PluginSamples/commit/8e06e7247bc4a9d870c604927559b8be9a8e4910)
- Shipped/community source commit: [`ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa`](https://github.com/manju-summoner/YukkuriMovieMaker.Plugin.Community/commit/ebb7102fe3ad36c6d90f9f4948e789fbf31dd0aa)
- Current API index: [YMM API Docs](https://ymm-api-docs.vercel.app/)
- Inspected: **2026-09-25**

## Tool Plugin surface

### Public/API-index shape

Current API-index material describes:

```text
IToolPlugin : IPlugin
```

with:

- `ViewModelType` — required public getter; points to an `IToolViewModel` implementation;
- `ViewType` — required public getter; points to a WPF `UserControl`;
- `AllowMultipleInstances` — has a default implementation of `false`.

The current `IPlugin.Details` member also has a default implementation returning `null`.

### Correction to the seed guide

Do **not** tell a coding agent that every shared plugin property must always be explicitly implemented.

The seed guide's table is useful as a list of familiar members, but "member exists" and "implementation class must explicitly implement it" are different claims.

### Lifecycle boundary

The existence of `IToolPlugin`, `IToolViewModel`, `SaveState`, `LoadState`, Timeline Tool surfaces, etc. does not by itself prove:

- exact construction timing;
- exact SaveState/LoadState invocation order;
- persistence across restart/project/layout changes;
- input ordering;
- preview synchronization.

Those remain reference or Lab behavior questions.

## Custom PropertyEditor

### Official sample baseline

The current official PropertyEditor sample says a custom control consists of:

1. a control implementing **`IPropertyEditorControl`**;
2. an attribute deriving **`PropertyEditorAttribute2`**.

The official custom attribute demonstrates:

- `Create()`;
- `SetBindings(FrameworkElement, ItemProperty[])`;
- `ClearBindings(FrameworkElement)`;
- `ItemPropertiesBinding.Create(itemProperties)` for multi-edit binding.

The official custom control demonstrates:

- `BeginEdit`;
- `EndEdit`;
- firing `BeginEdit` immediately before a value change;
- firing `EndEdit` immediately after the change.

### Correction to the seed guide

The seed guide currently says a custom editor control **must** implement `IPropertyEditorControl2`.

That is too strong for the current official sample baseline.

The official sample's basic custom editor implements `IPropertyEditorControl`.

Community notes describe `IPropertyEditorControl2` as a useful extension when `SetEditorInfo(IEditorInfo)` is needed, for example to evaluate an `Animation` at the current frame/length/FPS. Treat that as a more specialized/reference route until its exact current API shape is independently pinned.

### Safe AI rule

For a basic custom editor:

- start with `IPropertyEditorControl` + `PropertyEditorAttribute2`;
- preserve `BeginEdit` / `EndEdit` boundaries;
- use the current official binding helper/pattern when it fits;
- only add editor-info interfaces when the feature actually needs editor context and the current API is verified.

## VideoEffect

### Official sample baseline

The current official sample describes:

- effect settings class: `VideoEffectBase` + `[VideoEffect]`;
- processing class: implement **`IVideoEffectProcessor`**.

The current official D2D sample directly implements:

- `Output`;
- `SetInput(ID2D1Image?)`;
- `ClearInput()`;
- `Update(EffectDescription)`;
- `Dispose()`.

The sample explicitly disposes:

- the `Output` obtained from the D2D effect;
- the D2D effect itself;

and clears the effect input before disposal.

### Current official attribute shape in examples

Current official examples commonly use a compact attribute form such as:

```csharp
[VideoEffect("...", ["..."], [])]
```

Do not require a coding agent to emit two trailing `false` arguments merely because an older/alternate constructor form exists.

### Shipped/community advanced route

The current YMM4 Community source uses `VideoEffectProcessorBase` extensively.

Observed patterns include overrides such as:

```csharp
protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
protected override void setInput(ID2D1Image? input)
protected override void ClearEffectChain()
public override DrawDescription Update(EffectDescription effectDescription)
```

and use of the inherited `disposer`.

This is strong REFERENCE-IMPLEMENTATION evidence that the base-class route is real and actively used.

It is **not** the same thing as the official sample saying every third-party effect should inherit that base.

### Correction to the seed guide

The seed guide presents `VideoEffectProcessorBase` as the normal/mandatory processor structure and gives exact sealed/abstract member tables.

Rewrite that section as two routes:

1. **Official starter route:** implement `IVideoEffectProcessor` directly.
2. **Reference advanced route:** use `VideoEffectProcessorBase` when its current API/behavior fits the effect and the dependency is acceptable.

The seed guide's exact base signatures also need current-source wording:

- current Community examples return nullable `ID2D1Image?` from `CreateEffect`;
- current Community examples accept nullable `ID2D1Image?` in lower-case `setInput`;
- some effects legitimately return `null` from `CreateEffect` when they only alter `DrawDescription`.

Do not hard-code non-null signatures from the seed guide into generated code.

### Construction-order warning

If a base constructor calls an overridable method such as `CreateEffect`, the override may execute before assignments in a normal derived-constructor body have completed.

Therefore generated implementations must not assume a derived field assigned after `: base(devices)` is already available inside such an override.

This is C# construction behavior, not a YMM4-specific host guarantee.

## AudioEffect

### Official sample baseline

The current official sample describes:

- settings class: `AudioEffectBase` + `[AudioEffect]`;
- processor class: derive from `AudioEffectProcessorBase`.

The current official sample processor overrides:

```csharp
public override int Hz
public override long Duration
protected override void seek(long position)
protected override int read(float[] destBuffer, int offset, int count)
```

and consumes the preceding stream through `Input`.

### Safe AI rule

For ordinary AudioEffect generation, prefer the current official sample shape over inventing a custom audio pipeline.

Treat lower-case `seek` / `read` as exact base-class member names when generating against the current sampled API.

## Animation parameters

The current official AudioEffect and VideoEffect samples evaluate animation values with:

```csharp
item.SomeAnimation.GetValue(frame, length, fps)
```

This supports the seed guide's general point that `Animation` values depend on current frame, item duration and FPS.

### Boundary

Do not infer from one constructor example that every `Animation` must use a particular overload or an explicit easing argument.

Generate against the current overload needed by the selected sample/API.

## Display and editor attributes

The current official effect samples use `[Display]` together with an editor attribute such as `[AnimationSlider]`.

The current official PropertyEditor sample documents other built-in editor types including sliders, color/file/directory/enum/font/text/toggle editors.

### Safe AI rule

Before building a custom WPF editor, check whether a current YMM4-native editor already covers the needed property type.

A custom editor should be justified by interaction needs, not created by default.

## What remains Candidate / needs stronger proof

The following seed-guide claims should not yet be promoted as generic canonical facts solely from the material inspected here:

- exact `VideoEffectProcessorBase` sealed-member table;
- every internal/protected field exposed by that base;
- `IPropertyEditorControl2` being mandatory for all custom editors;
- a universal exact `VideoEffect` attribute constructor signature with five positional arguments;
- universal assembly-to-namespace ownership statements;
- generic MainWindow/DataContext reflection recipes;
- performance rules expressed as "always" / "must".

## AI choice rule

When multiple valid routes exist:

1. prefer the current official sample route for a minimal new plugin;
2. prefer a shipped/community base-class route when it materially reduces complexity and its current surface is verified;
3. prefer a Lab-pinned route when exact host behavior matters;
4. never collapse those three evidence classes into one "YMM4 requires this" statement.
