# VideoItem edit rebinding cannot rely on object identity or source range alone

- Status: canonical
- Knowledge class: LAB-NATIVE + NEGATIVE-FINDING
- Surface: S1
- YMM4 version: 4.56.1.0 Lite
- Revalidation trigger: YMM4 changes trim/move/copy-paste/split UndoRedo semantics

## Claim

On the tested YMM4 4.56.1.0 Lite host:

- head/tail trim mutated the same VideoItem object in-place while changing represented source coordinates/range;
- moving a split piece retained the same object and source coordinates while changing Timeline Frame;
- copy/paste created two distinct VideoItems with the same FilePath, ContentOffset, Length and playback rate at different Timeline occurrences;
- split Undo restored the original pre-split object reference, and Redo reused the previously created split references in the observed cycle.

Therefore neither:

- object identity alone, nor
- source identity + source range alone

is a universal durable occurrence locator across these edits.

## Safe use

Separate:

1. source/session analysis authority;
2. current Timeline occurrence binding.

Re-evaluate current item/source-range containment after edits, recompute current Timeline position after moves, and maintain an occurrence discriminator when duplicate source ranges can coexist.

## Do not infer

- This does not define a universal occurrence-ID solution.
- It does not cover save/restart identity.
- It does not cover variable/zero/reverse playback rates.
- It does not prove physical keyboard/menu edit routing.

## Failure behavior

When multiple current occurrences satisfy the same source identity/range, treat the binding as ambiguous rather than selecting the first match.

## Evidence

- Lab experiment: [videoitem-edit-rebinding](../../experiments/ymm4/videoitem-edit-rebinding/)
- Host ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Source/runner contract: `df92cf66d64b3beea3b7e4fc4324539a6f373d27`
- Workflow run: `35424106866`
- Artifact: `10578391490`
- Artifact SHA256: `97e51bbed4ba6d3a04e31462710ed8dd40a241a9f54e54096cd7a84018d057e7`
