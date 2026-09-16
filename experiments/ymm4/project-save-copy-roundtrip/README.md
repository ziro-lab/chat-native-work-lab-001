# Project archive save-copy roundtrip

## Question

On YMM4 v4.56.1.0, can a loaded Plugin create a second `.ymmp` through the native save path while keeping the original Project path active and leaving the original file byte-for-byte unchanged?

## Status

- Date: 2026-09-16
- YMM4 version: **v4.56.1.0 Lite**
- Evidence type: native automated behavior
- Current state: prepared; PASS requires the native Actions run.

## Preconditions from discovery

The earlier `project-save-archive-surface` experiment proved that the pinned host exposes:

- `MainViewModel.SaveProject(string)`
- `MainViewModel.KeepProjectPath`
- `MainModel.ProjectFilePath`
- `MainModel.LoadProjectFile(string)`

This experiment tests one exact route rather than doing more reflection discovery.

## Procedure

Inside the real YMM4 host on a disposable new Project:

1. Rename the active Timeline to a deterministic marker.
2. Call native `MainViewModel.SaveProject(source.ymmp)`.
3. Record active `MainModel.ProjectFilePath` and SHA256 of `source.ymmp`.
4. Set `MainViewModel.KeepProjectPath = true`.
5. Call native `MainViewModel.SaveProject(archive.ymmp)`.
6. Restore the previous `KeepProjectPath` value.
7. Assert `archive.ymmp` exists.
8. Assert active `MainModel.ProjectFilePath` still identifies `source.ymmp`.
9. Assert SHA256 of `source.ymmp` is unchanged by the archive save.
10. Call public `MainModel.LoadProjectFile(archive.ymmp)` and assert it returns a Project.
11. Verify the loaded archive contains the deterministic Timeline marker.

## PASS boundary

PASS proves that this exact native route can create a reloadable archive Project copy without switching away from or rewriting the original Project file on YMM4 v4.56.1.0.

It does **not** prove safe scene removal or VideoItem relinking; those remain separate transformations to perform on a disposable archive model/copy before final save.

## Safety

Only files under the workflow's temporary evidence directory are created. No user Project or persistent YMM4 state is used.

## Downstream impact

A PASS lets the recording-archive plugin prefer native `SaveProject(path)` with `KeepProjectPath=true` for final archive output rather than directly serializing the live original `.ymmp`.
