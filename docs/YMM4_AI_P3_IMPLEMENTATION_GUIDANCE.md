# YMM4 AI P3 — Implementation Guidance

This document preserves useful implementation advice from the long-form YMM4 plugin-development guide without mislabeling general C# / WPF / Direct2D techniques as YMM4 API requirements.

Use this layer **after** choosing a supported/public/reference YMM4 surface.

## Guidance authority

Nothing in this document becomes an undocumented YMM4 host guarantee merely because it is a good implementation technique.

Labels used here:

- **Official-sample-backed** — the current YMM4 sample demonstrates the pattern.
- **General implementation guidance** — ordinary C# / WPF / Direct2D / .NET engineering advice.
- **Version-sensitive integration guidance** — depends on a particular YMM4 host/member and must follow Lab evidence rules.
- **Measure-first optimization** — only adopt when complexity/hot-path evidence justifies it.

## Direct2D resource lifetime

### Official-sample-backed baseline

The current official VideoEffect and VideoSource samples demonstrate an important lifetime pattern:

1. create a D2D effect/resource;
2. keep the `Output` obtained from the effect;
3. before final disposal, clear D2D input references;
4. dispose the output object;
5. dispose the effect/resource.

The current official D2D VideoEffect sample explicitly comments that the `Output` obtained from the effect must be disposed separately.

### Safe guidance

For resources a plugin creates/owns:

- make ownership explicit;
- disconnect graph/input references before disposing referenced resources when the API requires that relationship;
- dispose every owned COM/D2D object exactly once;
- use YMM4 Developer Mode as a leak-detection aid.

Do not blindly dispose objects whose ownership belongs to YMM4 or another component.

### Dynamic/rebuilt resources

The seed guide contains a useful pattern for things such as LUT resources that are rebuilt when parameters change:

```text
detect meaningful parameter change
        ↓
detach old resource from consumer/effect
        ↓
dispose old owned resource
        ↓
create new resource
        ↓
attach new resource
```

Treat this as implementation guidance.

Do not turn a specific `lutEffect.LUT = null` sequence into a universal rule for unrelated D2D objects. The detach operation depends on the actual API/object graph.

## Avoid rebuilding expensive state every frame

The seed guide recommends caching the last relevant parameter state and rebuilding expensive resources only when that state changes.

That is generally sound for real-time preview code.

Prefer:

```text
cheap per-frame value evaluation
+
conditional resource rebuild
```

over:

```text
reparse/reallocate/rebuild everything every frame
```

when profiling or obvious resource cost justifies the distinction.

### Identity/no-op fast path

If a heavy resource represents an identity/no-op transformation, skipping or releasing that resource can be a useful optimization.

This is an optimization policy, not a YMM4 correctness requirement.

Correctness comes first: a no-op path must preserve the same externally required rendering semantics.

## WPF Tool/UI update model

### Prefer events/state notifications when available

The seed guide shows `DispatcherTimer` as one possible way to update a Tool UI periodically.

Do **not** make polling the default merely because it is easy to generate.

Prefer, in order:

1. public host/plugin events;
2. property/collection change notifications;
3. narrowly scoped coalesced/debounced rescans;
4. periodic polling only when no reliable event-driven surface exists or the feature is inherently periodic.

The Lab already contains multiple examples where event/source classification matters more than merely reading state on a timer.

### If a timer is used

General WPF guidance:

- keep UI work on the Dispatcher/UI thread;
- stop the timer during disposal;
- detach event handlers where necessary;
- avoid doing heavy I/O/CPU work synchronously on the UI tick.

This is ordinary WPF lifecycle guidance, not a special YMM4 guarantee.

## Custom PropertyEditor edit boundaries

This one is stronger than generic guidance because the current official sample demonstrates it directly.

For a custom editor using `IPropertyEditorControl`:

```text
BeginEdit
  -> mutate bound value
EndEdit
```

The official sample fires the events immediately before/after button-driven value mutation.

For continuous drag interactions, group the user gesture into an appropriate edit unit rather than creating a separate history boundary for every tiny movement. The exact integration should follow the current editor API/sample and product acceptance.

## Binding multiple selected items

The current official custom PropertyEditor sample demonstrates `SetBindings(... ItemProperty[] ...)` together with `ItemPropertiesBinding.Create(itemProperties)`.

Prefer the official multi-edit binding helper/pattern when it covers the desired editor behavior.

Do not manually reflect/set only the first `ItemProperty` and then claim multiple-selection support.

## Serialization of editor/plugin data

The seed guide proposes System.Text.Json source generation for complex state stored in a serializable property.

This is **general .NET guidance**, useful when:

- a plugin owns a structured payload;
- trim/AOT warnings or reflection-free serialization matter;
- the payload has an explicit schema/version strategy.

Do not introduce JSON indirection for simple typed properties merely because the technique is available.

### Fail closed on malformed durable state

For product-owned durable structured state:

- version the schema when evolution is expected;
- reject or safely default malformed input;
- avoid partial mutation before validation completes;
- preserve enough information for migration/recovery when the product needs it.

These are product persistence rules, not YMM4 host guarantees.

## WPF drawing optimization

The seed guide includes:

- `DrawingVisual`;
- `StreamGeometry`;
- frozen brushes/pens;
- visual reuse.

These can reduce WPF visual-tree/layout overhead in some hot drawing paths.

### Measure-first rule

Do not automatically replace ordinary controls/shapes with custom retained drawing code.

Use the more complex route when:

- profiling or scale shows the ordinary visual tree is a bottleneck;
- the UI contains many frequently redrawn primitives;
- the complexity cost is justified.

### Freeze

Freezing immutable WPF `Freezable` resources can reduce change-tracking overhead and allow safer sharing.

Do it when the resource is actually immutable after creation.

Do not freeze objects that must later be modified.

### StreamGeometry

Use `StreamGeometry` when its compact forward-built geometry model matches the workload.

Do not claim it is universally faster or simpler than every alternative for every editor.

## Allocation/memory micro-optimizations

The seed guide includes `stackalloc`, `MemoryMarshal.Cast` and `CollectionsMarshal.AsSpan`.

Keep these behind a **hot-path / profiling gate**.

### stackalloc

Use for small bounded temporary buffers only when the size is controlled.

Do not encode a magic “always safe up to N elements” rule into the AI reference. Safe stack usage depends on element size, call depth, platform/runtime context and repetition.

### MemoryMarshal.Cast

Useful for explicit zero-copy reinterpretation when:

- representation compatibility is understood;
- endianness/layout assumptions are appropriate;
- the code benefits materially from avoiding a copy.

Prefer readable ordinary conversion when this is not a hot path.

### CollectionsMarshal.AsSpan

This exposes a `List<T>`'s backing storage semantics more directly.

Use only in carefully controlled hot paths.

Do not mutate the underlying list in ways that invalidate assumptions while a span is being used.

Do not prefer this to normal list access in ordinary plugin code.

## Reflection / internal-host adaptation

Replace generic “search MainWindow.DataContext for a method” recipes with the Lab's bounded dependency policy.

### Required escalation

```text
S1 public plugin API
  -> S2 public host/WPF surface
  -> S3 bounded reflection/adaptation
  -> S4 Harmony/non-public patching
```

Move upward only when the lower level cannot express the actual requirement.

### For an S3 adapter

Record:

- exact YMM4 version(s) proven;
- exact type/member being resolved;
- why S1/S2 are insufficient;
- expected signature/semantics;
- cache strategy if repeated lookup matters;
- missing/ambiguous-member behavior;
- revalidation trigger.

### Failure rule

If the exact validated member is missing or ambiguous:

- disable/skip the feature;
- fall back to a separately supported route;
- report unsupported host behavior when appropriate.

Do **not** broad-search for a vaguely similar private method and invoke it.

### Reflection caching

Caching a validated `MethodInfo` / `PropertyInfo` can be reasonable when repeated reflection would otherwise be wasteful.

Cache the exact adapter resolution, not a broad “whatever matched last time” heuristic.

If invocation proves the member is no longer compatible, invalidate/disable the adapter rather than silently searching for another private member.

## Base-constructor / overridable-method hazard

The seed guide's `VideoEffectProcessorBase` example highlights a general C# construction hazard:

```csharp
Derived(...) : base(devices)
{
    this.item = item;
}
```

If the base constructor invokes an overridable member such as `CreateEffect`, that override runs before the derived constructor body assignment above.

Therefore an override must not assume fields assigned only in the derived constructor body are already initialized.

This is general C# object-construction behavior.

When using current Community-style primary constructors, inspect the actual initialization/use pattern rather than mechanically copying an older constructor template.

## Error handling

Avoid empty broad `catch { }` blocks in integration code unless the operation is intentionally best-effort and failure is safely represented elsewhere.

For version-sensitive host integrations, distinguish:

- unsupported host shape;
- malformed user/plugin state;
- transient I/O failure;
- programmer bug.

Failing closed is preferable to silently continuing with guessed host semantics.

## Guidance that should stay out of the core contract

Do not promote these into unconditional YMM4 requirements:

- `DispatcherTimer` for all Tool updates;
- `DrawingVisual` for all custom drawing;
- freezing every WPF brush/pen;
- `StreamGeometry` for every path;
- a fixed universal `stackalloc` size limit;
- `CollectionsMarshal.AsSpan` for ordinary collection access;
- JSON source generation for every plugin state object;
- reflection from `Application.Current.MainWindow.DataContext` as a standard plugin entry point.

They are techniques. The core AI reference should first answer **which YMM4 surface is supported and what host behavior is proven**.
