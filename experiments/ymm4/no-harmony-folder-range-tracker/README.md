# P1.2 — FolderRangeTracker

This is the host-independent structural core for the no-Harmony Full roadmap.

It is deliberately **not** a YMM4 plugin and deliberately **not** a WPF component.

## Boundary

The tracker knows only:

- folder identity;
- contiguous logical layer ranges;
- Insert / Delete / adjacent Swap structural edits.

It does not reference:

- YukkuriMovieMaker assemblies;
- WPF;
- TimelineView / TimelineViewModel;
- RoutedCommand;
- DirectDisplay;
- reflection / nonpublic setters.

That keeps structural folder policy testable without launching YMM4.

## P1.2 policy candidate

Folders are treated as **positional contiguous ranges**, not as ownership attached to a particular YMM4 row object.

### Insert

For standard insertion at position P:

- P before or exactly at the folder head -> the whole folder shifts down;
- Start < P <= End -> the new row becomes part of the folder and End expands;
- P > End -> unchanged.

This matches the useful behavior already present in the MIT-licensed LayerPatan reference: a standard YMM4 insertion inside a folder joins the folder.

### Delete

Deletion removes the overlapping positional rows.

- deleting before a folder shifts it upward;
- deleting inside shrinks it;
- deleting the head promotes the next surviving row at the same numeric head;
- deleting the entire range removes the folder;
- if a nested folder would acquire the same head as its parent, the nested head moves down until heads are unique; an empty nested folder is pruned.

A one-row folder may remain after deletion. Minimum folder size is a creation-UX concern, not a structural-transform invariant.

### Standard Move Up / Move Down

YMM4 standard layer reordering is represented as an adjacent row swap.

The folder ranges stay at their numeric positions. Therefore a row moving across a folder boundary changes membership. Moving a **whole folder as a unit** is a separate explicit product operation and is not inferred from the standard single-layer reorder command.

This is intentionally simple and matches the positional-range model used by the original LayerPatan structural core. The LayerPatan README also notes that layer-name drag reordering across collapsed folders was not fully verified there, so P1.5 must still prove the behavior under the new Track C architecture.

## Invariants

- folder IDs are unique and non-empty;
- Start >= 0;
- End >= Start;
- ranges may be nested or disjoint;
- crossing ranges are rejected;
- two folders may not share the same head layer.

## Reference

Behavior was compared with the public MIT-licensed project:

- bluemistel/YMM4-LayerPatan
- MIT License, Copyright (c) 2026 bluemistel
- relevant reference files: Core/LayerOps.cs, Core/ShiftDetector.cs, Services/TimelineTracker.cs

The implementation in this directory is a reduced independent tracker for the current no-Harmony architecture; it does not import YMM4 integration, persistence, group-range repair, UI, or Harmony patch code from LayerPatan.

## Gate

Run:

```bash
dotnet run --project experiments/ymm4/no-harmony-folder-range-tracker/tests/TrackerTests.csproj -c Release
```

The P1.2 gate is green only when the pure Linux runner reports:

- `status=PASS_FOLDER_RANGE_TRACKER`;
- all policy/boundary assertions pass;
- the project remains non-Windows and has no YMM4 references.

After this gate freezes, P1.3 translates observed host behavior into these structural edit values.
