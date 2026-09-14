# YMM4 Plugin Host Validation

## Goal

Prove that GitHub Actions can build a minimal .NET 10 YMM4 plugin and that the **real YukkuriMovieMaker4 4.55.1.1 Lite process** loads it and executes a plugin callback on a GitHub-hosted Windows runner.

## Environment

- GitHub-hosted Windows runner
- YMM4 4.55.1.1 Lite
- .NET 10
- YMM4 archive SHA256:
  `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

YMM4 is downloaded from its official GitHub release during the workflow and is not committed or uploaded as an artifact.

## Assertions

PASS requires all of the following:

1. The exact YMM4 archive downloads successfully.
2. Its SHA256 matches the pinned value.
3. The probe plugin builds with warnings treated as errors.
4. The plugin DLL is placed under the temporary YMM4 `user/plugin` directory.
5. The real `YukkuriMovieMaker.exe` process is launched.
6. YMM4 invokes the plugin's `ILocalizePlugin.SetCulture()` callback.
7. The callback writes a marker containing the expected probe name and the SHA256 of the loaded assembly.
8. Evidence/provenance is uploaded without including the YMM4 distribution.

## PASS means

A PASS proves that this GitHub Actions environment can compile a plugin against the exact YMM4 release, launch the real host process, have that host discover/load the plugin, and observe a callback executed inside the host process.

## NOT PROVEN

This experiment does **not** prove:

- Tool Plugin UI rendering or interaction.
- Timeline read/write access.
- Item placement, Undo/Redo, template access or project semantics.
- `.ymme` installer UI behavior.
- Compatibility with other YMM4 versions.
- Real user assets / PSD rendering correctness.
- Physical mouse/keyboard automation.

Those require separate experiments.

## Reproduction

Run the `YMM4 plugin host validation` workflow manually or change this experiment's source/workflow files.

## Evidence

`EVIDENCE.md` records the latest audited successful run once available. The workflow artifact also contains `result.txt`, `plugin-marker.txt`, `provenance.json`, the compiled probe DLL and supporting logs.
