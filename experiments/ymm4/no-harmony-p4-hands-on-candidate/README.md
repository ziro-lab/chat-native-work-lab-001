# P4.4 no-Harmony folder Hands-on Candidate

This is a **manual UX candidate**, not a release build.

It composes the already-green P0-P4 mechanisms into one plugin that can be installed into YMM4 and used without Lab fixture injection.

## Supported hands-on workflow

Normal work is done from the Timeline layer-name column.

1. Select **2 or more contiguous layers** with normal YMM4 layer selection.
2. Right-click a visible layer in the layer-name column.
3. Open **レイヤーフォルダ**.
4. Choose `Lx–Ly をフォルダにまとめる...`.
5. Enter a name.
6. Click the visible folder tag to collapse / expand.
7. Right-click the folder range for:
   - open / close;
   - rename;
   - **フォルダを解除（レイヤーは残す）**.

A Tool entry named **レイヤーフォルダ (no-Harmony)** exists only so YMM4 owns the project ToolState. The Tool panel does not need to be open for normal editing.

## Candidate behavior

- no Harmony;
- FoldMap/DirectDisplay owns folded geometry;
- right-click maps folded display Y -> logical Layer through the frozen common mapping;
- current realized YMM4 ContextMenu is extended, not replaced;
- multiple folders and valid nested folders are displayed;
- folder metadata commands use YMM4 UndoRedoManager history;
- folder state is stored in project ToolState.SavedState;
- malformed/newer unreadable state is preserved raw and editing is blocked rather than silently overwriting it;
- project switch synchronizes from the host-restored ToolArea state;
- a new project clears inherited folder metadata.

## Creation candidate

For P4 hands-on:

- at least 2 selected layers;
- selection must be contiguous;
- nested/disjoint valid ranges are allowed;
- crossing ranges and duplicate heads are rejected;
- a single selected layer does **not** auto-insert another layer.

This is intentionally still a UX candidate. Hands-on feedback can change the convenience policy without reopening the frozen FolderDocument model.

## Known P3 limitation

If this plugin is **completely unavailable** and a project containing folder metadata is opened and re-saved, YMM4 does not preserve the absent plugin's ToolState entry. The YMM4 project remains usable, but folder metadata can be lost.

Do not test uninstall/re-save on an important project. Keep the previous project file / backup.

## Manual install

Place the candidate folder under:

```text
<YMM4 folder>\user\plugin\Ymm4NoHarmonyFolderHandsOn\
```

with:

```text
Ymm4NoHarmonyFolderHandsOn.dll
```

then restart YMM4.

## Hands-on script

Use:

`docs/YMM4_NO_HARMONY_P4_HANDS_ON.md`

The manual run is for discoverability, comfort, wording and visual continuity. It should not manually re-prove the automated mechanism assertions.

## Not yet a release claim

Deferred from this candidate:

- final styling / color system;
- folder-wide hide/show;
- group-control conveniences;
- broad special-item compatibility;
- large-project performance;
- final packaging / installer / release docs.
