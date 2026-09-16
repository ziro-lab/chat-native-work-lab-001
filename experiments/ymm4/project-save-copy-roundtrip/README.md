# Project save-copy path observation

## Question

On YMM4 v4.56.1.0, can the live Project use native `SaveProject(archive)` as a non-disruptive archive-copy operation while keeping the original Project active?

## Status

**PASS — the live Save-As route is unsuitable for the recording-archive product contract.**

- Date: 2026-09-16
- YMM4 version: **v4.56.1.0 Lite**
- Evidence type: native automated behavior

## Observed behavior

A disposable live Project was saved to `source.ymmp`, then saved again to `archive.ymmp` with `KeepProjectPath=true`.

Observed on the exact host:

- `archive.ymmp` is created and reloadable;
- `source.ymmp` remains byte-for-byte unchanged;
- `KeepProjectPath=true` does **not** keep the live active path on `source.ymmp`; the live path becomes `archive.ymmp`;
- calling public `MainModel.ChangeProjectPath(source)` restores the path text, but the live model becomes unsaved (`IsProjectFileSaved=false`).

Therefore live Save-As + path restore does not satisfy the desired invariant that archive generation must leave the user's open Project completely untouched.

## PASS boundary

PASS proves only the observation above for YMM4 v4.56.1.0. In particular, it proves that this live Save-As route should **not** be used as the recording-archive implementation spine.

It also proves that the archive file itself is valid/reloadable and that native SaveProject does not rewrite the original source file.

## Adopted downstream decision

Use a detached archive graph instead:

`LoadProjectFile(source) -> detached Project -> detached-only mutation -> Json.Save(archive) -> LoadProjectFile(archive) validation`

That route is separately proven by `detached-project-json-save-roundtrip` and the integrated recording-archive spine.

## Evidence

- Source head: `7450eb3d9b92cbb67c40f2ceac10f80b0ab6c5bd`
- Workflow run: `35113196362`
- Artifact: `10453940616`
- Artifact SHA256: `98ea36b07ae5be82ba52ab795a8595483a803f3f6a36a1744bbc8a55680d60a6`
