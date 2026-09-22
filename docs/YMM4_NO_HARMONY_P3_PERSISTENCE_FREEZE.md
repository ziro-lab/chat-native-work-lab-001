# YMM4 no-Harmony Full — P3 Persistence Freeze

Status: **COMPLETE / FROZEN**.

P3 establishes project-owned folder persistence without persisting display geometry and without introducing Harmony.

## Frozen persisted model

Schema v1 stores only:

- schema version;
- opaque Timeline key;
- folder Id;
- logical Start / End;
- Name;
- collapsed state.

Not persisted:

- display row;
- viewport;
- item/view geometry;
- derived parent/child pointers;
- reflected/private YMM4 state.

Hierarchy remains derived from validated logical ranges.

## P3.1 — FolderDocument / codec

PR #86.

Pure model source: `b1392b6fd0af02601433b643ea1b496535428aa3`.

Run `35705650993`:

- `PASS_P3_FOLDER_DOCUMENT`;
- 29/29 PASS;
- plain net10.0;
- no YMM4/WPF dependency.

Frozen codec outcomes:

- Empty;
- Malformed;
- UnsupportedVersion;
- Invalid;
- Loaded.

Serialization is canonical/deterministic.

## P3.2 — project ToolState roundtrip

PR #87.

Accepted source: `0600506b55b379880e4f30c5ef3c958bf6869491`.

Primary host YMM4 4.56.1.0:

- run `35710875053`;
- job `106691172892`;
- `PASS_P3_TOOLSTATE_ROUNDTRIP`;
- artifact `10686861509`;
- digest `sha256:93c68003558eb70383905a222bc07655a0d2a2ac8208054d48d9ab81793c97a6`.

Frozen host behavior:

- public `Timeline.ID : Guid` is usable as the per-Timeline persisted key;
- FolderDocument is stored in project-owned `ToolState.SavedState`;
- independent project files can hold independent FolderDocuments;
- public `OpenProject(string)` restores the corresponding ToolArea state;
- an already-live inner plugin ViewModel is not guaranteed a second `LoadState()` callback during project switch;
- public `ProjectFilePath` change notification is the accepted switch signal;
- after that signal, the plugin reads the host-restored public ToolArea `SaveState()` value to synchronize current project state.

## P3.3 — unreadable / newer state preservation

PR #88.

Run `35708831971`:

- pure persistence suite: 40/40 PASS.

Frozen safety policy:

- malformed JSON, unsupported future schema, and structurally invalid state do not become writable empty state;
- runtime may degrade to an empty FolderDocument for usability;
- the exact unreadable raw SavedState is quarantined;
- while quarantined, unrelated project saves return the exact raw string;
- only explicit Replace or Reset clears quarantine.

This protects data when the plugin is present but cannot understand its stored state.

## P3.4 — new project initialization

PR #89.

Accepted source: `66d97f4b310d1de83c5f46ece47b0344ce54e2c0`.

Primary host:

- run `35723295973`;
- job `106731187081`;
- `PASS_P3_NEW_PROJECT`;
- artifact `10691789141`;
- digest `sha256:8078ecff24acf6d95ea2be846a75f6a09b39d5140ccb7cf22ad368da656324b1`.

Observed raw host behavior:

- `CreateProject()` clears ProjectFilePath;
- creates a fresh Timeline.ID;
- reports IsEmptyProject=true;
- host ToolArea does not automatically clear the previous project SavedState.

Frozen correction:

- observe public ProjectFilePath change;
- when path is empty, IsEmptyProject=true, and Timeline.ID is fresh, clear this plugin's SavedState through public ToolArea `LoadState(...SavedState=null)`;
- normal OpenProject is not affected.

## P3.5 — plugin unavailable

PR #90.

Primary host observation:

- run `35722562456`;
- job `106728589678`;
- artifact `10692372073`;
- digest `sha256:393dbfc6805b547b02e288489ede3c3836f5d09a1fffbdfc05e42d54043d9606`.

Observed:

- project with folder ToolState opens when the subject plugin is physically absent;
- project can be re-saved;
- Timeline/project data remains usable;
- absent plugin ToolState is **not preserved** across that re-save.

Frozen limitation:

> If the folder plugin is completely unavailable and the project is re-saved, folder metadata may be lost.

P3 does not add a sidecar/dual-store solely for this rare unavailable-plugin case. Doing so would add a second ownership/conflict/migration system and weaken portability.

Recovery guidance belongs in P7 release documentation: keep the prior project/backup if a project was re-saved while the plugin was unavailable.

## P3.6 — structural semantics after reload

PR #91.

Accepted source: `bfacff67e400eebab1a105be5cdc183b9d83cf3c`.

Primary host:

- run `35722605807`;
- job `106728731624`;
- `PASS_P3_RELOAD_STRUCTURAL`;
- artifact `10691977455`;
- digest `sha256:93cba48d7ee249c7165ae3e22a5f42e5c35dfcfd3a2a498568f413c1e61233d4`.

Representative P3 -> P1 composition:

- reload FolderDocument A=L2..L6;
- execute standard Add Layer L3;
- native history creates one record;
- one plugin folder Undo command joins that native transaction;
- items shift and A becomes L2..L7;
- updated FolderDocument remains serializable;
- one Ctrl+Z restores Timeline + folder state;
- one Ctrl+Y reapplies both;
- Name/collapsed metadata survives.

P1 golden suites were not replayed.

## P3 V2 — both pinned hosts

PR #92.

Secondary host YMM4 4.55.1.1:

- control source `d5211107c8b5e0ed4f3fcdbd522254fb02f9dbeb`;
- run `35723950438`;
- job `106733022303`;
- final marker `PASS_P3_V2_SECONDARY`;
- artifact `10692695546`;
- digest `sha256:55c66c21ea5cdaa0f3ca263cc3b2014ebe865484ffb50aaabab070c98500fec3`;
- independent artifact SHA256 matched.

One Windows job reused one 4.55.1.1 host acquisition and ran the frozen:

- P3.2 project roundtrip;
- P3.4 new-project reset;
- P3.6 reload-structural path.

All three passed.

Pure P3.3 recovery was not redundantly rerun on YMM4.

## Frozen product persistence boundary

```text
FolderDocument / codec / recovery session
          ↕
project-owned ToolState.SavedState
          ↕
host ToolArea state
          ↕
ProjectFilePath lifecycle signal + Timeline.ID
          ↕
YMM4 project lifecycle
```

No display geometry is the persistence source of truth.

## P3 exit

The P3 exit requirements are satisfied:

- save / reopen preserves valid structure — green;
- project switching isolates FolderDocuments — green;
- new project does not inherit prior folder state after the accepted correction — green;
- malformed/future/invalid state fails safely without silent overwrite — green;
- migration/version boundary is explicit and deterministic for schema v1 — green;
- structural edits after reload continue to use frozen P1 semantics — green;
- both pinned hosts agree on supported lifecycle mechanisms — green;
- unavailable-plugin re-save limitation is documented and does not corrupt the YMM4 project.

**P3 Folder Data Model & Persistence is COMPLETE / FROZEN.**
