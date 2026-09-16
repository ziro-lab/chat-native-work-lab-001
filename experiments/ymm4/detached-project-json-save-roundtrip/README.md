# Detached Project Json.Save roundtrip

## Question

Can YMM4 v4.56.1.0 create an archive `.ymmp` by loading a detached `Project`, mutating only that detached graph, and saving it with `YukkuriMovieMaker.Json.Json.Save<T>` while leaving the live project and original `.ymmp` unchanged?

## Fixture

The native host:

1. creates/saves `source.ymmp` with a deterministic Timeline marker;
2. loads it with `MainModel.LoadProjectFile()` as a detached `Project`;
3. mutates only the detached Timeline marker and detached `Project.FilePath`;
4. saves the detached object with `Json.Save(detached, archive.ymmp)`;
5. verifies the live Timeline, live project path/saved state, and source file hash are unchanged;
6. validates the archive through both `Json.Load<Project>()` and `MainModel.LoadProjectFile()`.

## Observed behavior

PASS on the exact host:

- detached Project/Timeline are separate object instances from the live graph;
- `Json.Save(detached, archive)` creates a reloadable archive;
- live path, live saved state and live Timeline are unchanged;
- source `.ymmp` remains byte-for-byte unchanged;
- both `Json.Load<Project>` and `MainModel.LoadProjectFile` reload the detached archive marker.

## PASS boundary

A PASS proves a non-destructive native serialization route exists for archive-project creation on YMM4 v4.56.1.0 without using live Save As.

The later `recording-archive-integrated-spine` experiment additionally proves selected-scene pruning plus VideoItem relinking on top of this serialization spine.

## Evidence

- Source head: `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`
- Workflow run: `35113196633`
- Artifact: `10453980369`
- Artifact SHA256: `aed5da044229ea23ac1896c55bb4b96215338aa30c2bc1f6ae6269dbde525c4f`
