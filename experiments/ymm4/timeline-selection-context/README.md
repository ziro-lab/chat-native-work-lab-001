# YMM4 Timeline Selection Context — Experiment 002

## Goal

Determine how a YMM4 Timeline Tool can observe the editor's current single-item selection well enough to support context-sensitive character palettes.

The product question is intentionally narrow:

```text
user clicks one Timeline item
-> plugin observes the selected item
-> plugin can distinguish VoiceItem / TachieFaceItem
-> plugin can read the selected item's Character
-> selection clear can be observed
```

This first phase is **runtime discovery**, not a claim that selection observation is already proven. It runs inside the real pinned YMM4 host and records the concrete `TimelineToolInfo` and active Timeline ViewModel selection-related surfaces before we turn them into hard assertions.

## Environment

- GitHub-hosted Windows runner
- YMM4 4.55.1.1 Lite
- .NET 10
- YMM4 archive SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

## Discovery assertions

Phase D passes only if:

1. The exact YMM4 archive and .NET 10 environment are verified.
2. A minimal Timeline Tool plugin builds warning-free.
3. Real `YukkuriMovieMaker.exe` loads the plugin.
4. A real project/Timeline becomes available.
5. Synthetic redistribution-safe `VoiceItem` and `TachieFaceItem` fixtures are inserted into that real Timeline.
6. The real host creates the Timeline Tool ViewModel and calls `SetTimelineToolInfo`.
7. Selection/current-item related members of `TimelineToolInfo`, the active Timeline ViewModel, and relevant YMM4 types are recorded as evidence.

## PASS means

`PASS_DISCOVERY` means the native host path and the runtime API-surface capture succeeded. It does **not** yet mean that a user click was observed.

The next revision of this same experiment should convert the discovered surface into behavioral assertions for:

- Voice selection
- Face selection
- Character extraction
- selection clear

## NOT PROVEN in discovery phase

- Physical mouse click automation.
- SelectionChanged event semantics.
- Polling requirements.
- Playhead access.
- Quick Drop.
- Layer front/back placement.
- Compatibility with other YMM4 versions.

## Evidence

The workflow artifact contains:

- `result.txt`
- `selection-surface.txt`
- `timeline-fixture.txt`
- `host-log.txt`
- `provenance.json`
- probe DLL SHA256

The workflow also prints the compact selection surface into the Actions log so the result can be audited without downloading YMM4 or private assets.
