# YMM4 .ymme Update Preservation

## Goal

Observe what the real YMM4 `.ymme` installer does to pre-existing files when the same plugin folder is updated.

This experiment answers one concrete portability question needed by downstream plugins:

> If plugin-owned settings live inside the plugin folder, are files that are not present in the new `.ymme` package preserved across an actual YMM4 plugin update?

## Environment

- GitHub-hosted Windows runner
- YMM4 4.55.1.1 Lite
- .NET 10
- exact official YMM4 archive SHA256:
  `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

## Procedure

1. Build a minimal probe plugin as v1 and v2.
2. Package v1 as a real `.ymme` with:
   - the probe DLL;
   - `package-version.txt = v1`;
   - `obsolete-v1.txt`.
3. Install v1 through the real YMM4 `.ymme` path.
4. After v1 is installed, create files that are **not** in either package:
   - `<plugin>/Data/settings-probe.json`;
   - `<plugin>/user-root-probe.txt`;
   - `<YMM4>/user/Ymm4PortableSettingsProbe/settings-probe.json`.
5. Package/install v2 with:
   - a different probe DLL;
   - `package-version.txt = v2`;
   - `v2-only.txt`;
   - no `obsolete-v1.txt`.
6. Require the installed DLL and package marker to become v2.
7. Record whether each pre-existing file survived and whether the removed v1 package file survived.

## Assertions

PASS requires:

- v1 was installed through the real host installer route;
- v2 was installed through the same route;
- installed `package-version.txt` reads `v2`;
- installed probe DLL SHA256 matches the v2 package DLL;
- `v2-only.txt` exists;
- preservation/deletion state is recorded for all probe files.

The experiment does **not** require the preservation result itself to be true. PASS means the update behavior was actually exercised and classified.

## PASS means

A PASS proves the observed file-preservation semantics of the YMM4 4.55.1.1 Lite `.ymme` update path for this synthetic plugin package on the tested Windows runner.

## NOT PROVEN

This does not prove:

- the same semantics on every future YMM4 version;
- YMM4 application self-update behavior;
- behavior when the user manually deletes/reinstalls the plugin;
- behavior of third-party installers;
- that storing settings in the plugin folder is automatically the best product design;
- Template Placer migration logic or settings-store integration.

## Evidence

The workflow uploads:

- `result.json`;
- installer/window observation log;
- package file inventories and hashes;
- provenance tying source HEAD, workflow run and exact YMM4 version together.

YMM4 itself is never uploaded.
