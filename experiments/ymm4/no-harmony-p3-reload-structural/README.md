# P3.6 — structural edit after persisted-state reload

One representative composition test, not a replay of P1.

- save FolderDocument A2..6 with real Timeline items;
- Save As another project with different ToolState;
- reopen project A and decode the host-restored FolderDocument;
- attach the already-proven P1 same-transaction pattern to the restored state;
- execute the standard Add Layer at L3;
- require native item shift + FolderRangeTracker A2..7 in one native record;
- Ctrl+Z restores both; Ctrl+Y reapplies both;
- persisted name/collapse metadata survives history.

P1 golden suites remain frozen and are not replayed.
