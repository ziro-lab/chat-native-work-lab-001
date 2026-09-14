# YMM4 Playhead Quick Drop — Experiment 003

## Goal

Determine whether a real YMM4 Timeline Tool can obtain the current playhead frame through a public host surface and place a registered Item Template clone at that frame while preserving the Template's intrinsic Length.

Target product flow:

```text
Palette entry double-click
-> read current playhead frame
-> clone live YMM4 Template item
-> Frame = playhead
-> Length = Template intrinsic Length
-> add to Timeline
```

This first run performs runtime discovery and, if a usable public frame surface exists, immediately performs the behavioral Quick Drop proof.

## Environment

- GitHub-hosted Windows runner
- YMM4 4.55.1.1 Lite
- .NET 10
- pinned YMM4 SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`
- synthetic Character / TachieFaceItem / ItemTemplate only

## PASS target

`PASS_PLAYHEAD_QUICK_DROP` requires:

1. real Timeline Tool receives `TimelineToolInfo`
2. a public playhead/current-frame surface is found and can be moved to a known frame through a public API
3. reading that surface returns the known frame
4. a registered synthetic YMM4 Template is cloned independently
5. clone Frame equals playhead
6. clone Length equals the Template source Length
7. Template source remains unchanged
8. clone is present in the real Timeline

## NOT PROVEN

- physical mouse movement of the playhead
- Palette double-click UI event itself
- Front/Base/Back layer resolution
- real PSD rendering
- other YMM4 versions
