# Archive-copy scene roundtrip

## Question

Can a recording archive keep the active YMM4 Project untouched by copying an already-saved `.ymmp`, pruning only the copied Project's unrelated Timeline records, and validating the result through YMM4's native `LoadProjectFile()`?

## Scope

Pinned host: YMM4 v4.56.1.0 Lite.

The disposable fixture contains three scenes:

- `Main` — retained;
- `Dependency` — retained because Main has a SceneItem pointing to it;
- `Unused` — removed from the archive copy.

## Procedure

1. Create the scenes in the real YMM4 host and add a native SceneItem in Main referencing Dependency.
2. Save `source.ymmp` normally.
3. Record its SHA256.
4. Copy it byte-for-byte to `archive.ymmp`.
5. Modify only `archive.ymmp`: remove the Unused Timeline JSON object while preserving Main and Dependency.
6. Load `archive.ymmp` through native `MainModel.LoadProjectFile()`.
7. Require exactly Main + Dependency to remain and require the Main SceneItem reference to resolve to Dependency.
8. Require source SHA256 unchanged.

## PASS boundary

PASS proves the selected-scene/dependency archive can be produced as an offline transformation of a saved Project copy without changing the live/original Project file.

It does not yet prove VideoItem media-path/ContentOffset rewriting; that is a later archive-copy roundtrip stage.
