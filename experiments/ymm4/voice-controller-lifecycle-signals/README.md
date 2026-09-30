# Voice controller lifecycle signals — YMM4 4.56.1.0

## Question

Can the Local Pronunciation Assist controller follow VoiceItem structural lifecycle without polling?

Candidate public controller pattern:

```text
ITimelineToolViewModel.SetTimelineToolInfo(info)
  -> initial scan of info.Timeline.Items
  -> subscribe info.UndoRedoManager.Recorded
  -> subscribe info.UndoRedoManager.Undoed / Redoed
  -> subscribe each tracked VoiceItem PropertyChanged
```

The item-level notification half is already proven by Lab PR #118.
This experiment focuses on structural add/delete/Undo/Redo.

## Native gate

With a real host-supplied Timeline and UndoRedoManager:

1. add one probe VoiceItem;
2. commit the host history record;
3. observe Recorded and a rescan sees the item;
4. UndoAsync removes it and Undoed fires;
5. RedoAsync restores it and Redoed fires;
6. delete the item and commit;
7. observe Recorded and rescan sees it absent;
8. UndoAsync restores it.

Timeline PropertyChanged and Timeline.UndoRedoCommandCreated traffic are recorded as diagnostics.

## Product consequence

If GREEN, the controller can remain event-driven:

- SetTimelineToolInfo => bind/rebind + initial scan
- Recorded/Undoed/Redoed => coalesced structural rescan
- VoiceItem PropertyChanged => per-item correction reevaluation

No timer/polling loop is required.
