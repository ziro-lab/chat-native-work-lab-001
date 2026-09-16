# Downstream outcome — Template clone fidelity

This note records how the public [`template-clone-fidelity`](../experiments/ymm4/template-clone-fidelity/) experiment informed a downstream YMM4 Template Placer fix.

It is **not additional lab evidence** and does not expand the experiment's PASS boundary. Product-native evidence remains in the downstream repository.

## Why the experiment was needed

Hands-on use of a real exported YMM4 template exposed a gap that earlier product acceptance had missed:

- an expression could be discoverable by logical Character name;
- placement could still retain a detached source `Character` object rather than the target Voice's canonical Character object;
- real templates may also contain rich plugin-owned face parameters/effects, so fixing Character identity must not rewrite already-cloned template content.

No user template, PSD path, project data, or third-party plugin payload was copied into this public lab.

## Lab observation -> downstream decision

The lab proved on exact YMM4 4.55.1.1 Lite that:

- `TachieFaceItem.GetClone()` returns an independent item but preserves the source `Character` object reference;
- `Character`, `CharacterName`, `TachieFaceParameter`, and `TachieFaceEffects` are publicly writable;
- the tested non-empty built-in/community effect is cloned to independent objects and keeps a non-default property value;
- `ItemTemplate.CreateItemsAsync(int targetFps)` exists as a public host surface;
- public `Json.GetClone<T>` exists;
- internal `Project.MainModel` exposes public `AddTemplateItemAsync(frame, layer, ItemTemplate)`.

The product therefore treated two concerns separately:

```text
logical Character grouping / candidate visibility
!=
destination Character object identity
```

The product kept `CharacterName` as the logical user-facing grouping key, but after planning a complete cloned bundle it rebinds matching character-bearing **clones only** to the Character object supplied by the selected destination context.

For `TachieFaceItem`, it snapshots the already-cloned Face parameter/effect state around the Character setter so destination rebinding cannot silently replace the action content. The live source Template is not rewritten.

## Product regression that was added

A new integrated native regression drives the actual downstream expression-list WPF path:

```text
canonical Character A on target Voice
+ detached same-name Character B on source Template Face
+ non-default effect marker
-> choose candidate in real ComboBox
-> execute real Place command
-> generated Face must bind to A
-> source Template must remain on B and unchanged
-> cloned effect objects must remain independent
-> non-default effect value must survive
-> one native Undo must restore the operation
```

The product promoted this to a mandatory `TEMPLATE_FIDELITY=PASS` release stage rather than leaving it as a one-off regression.

Final integrated downstream verification after adoption:

- product source HEAD: `eeb465438a75508a936180543ee517bdfbf292a4`
- native run: `35129231724` (#139)
- native job: `104905783505`
- native assertions: **932 PASS**
- relative acceptance: **21/21 PASS**
- dedicated UI/UX acceptance: **10/10 PASS**
- evidence guard: **1 valid + 14 invalid-evidence rejection cases PASS**
- Release/Proof compiler warnings: **0 / 0**
- exact release DLL native smoke: PASS
- package/provenance/stable-root checks: PASS

This downstream result is adoption feedback only; it is not evidence for stronger claims in the public experiment.

## Lessons to retain

### Logical identity and object identity must be tested separately

When the product intentionally groups entities by a logical key such as a visible name, a test that proves candidate visibility by that key does **not** prove that generated host objects are connected to the intended runtime object instance.

For host models with reference-bearing objects, acceptance should consider both:

```text
logical key equality
runtime object identity where identity affects behavior
```

### A successful clone is not automatically a destination-ready clone

A host clone API can correctly copy item content while deliberately retaining references to source-context objects. Destination rebinding may be a separate integration responsibility.

Do not infer destination correctness from:

- successful clone creation;
- equal display names;
- preserved geometry;
- preserved effect values.

### Fixing identity must not destroy cloned content

If setting a destination identity may refresh defaults or derived state, preserve and verify the already-cloned content that belongs to the Template action itself.

This is especially important around plugin-owned parameter/effect containers.

### Product acceptance should reproduce the user's actual route

The earlier downstream suite had proved detached same-name candidates and successful placement logic separately, but had not asserted the generated Face's final Character object through the actual expression-list ComboBox + Place path.

The corrective test therefore used the real WPF selection/command route instead of setting the selected model value directly.

## Still not proven

Neither the lab experiment nor the synthetic downstream native fixture proves arbitrary third-party fidelity, including:

- `PsdTachiePlugin` parameter fidelity;
- `CharactorMotion` percentage and enabled/disabled fidelity;
- arbitrary third-party effect serialization/clone semantics;
- visual equivalence of real PSD output.

Those remain hands-on/downstream acceptance items. If a real third-party value still differs after the Character rebind fix, the next useful evidence is an exact before/after export diff rather than assuming the host's built-in effect behavior generalizes to every plugin.
