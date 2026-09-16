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

## PASS boundary

A PASS proves a non-destructive native serialization route exists for archive-project creation on YMM4 v4.56.1.0 without using live Save As.

## NOT PROVEN

This experiment does not by itself prove every product mutation (scene pruning, VideoItem relinking, third-party plugin payloads). Those remain product integration tests, but they can use this proven serialization spine.
