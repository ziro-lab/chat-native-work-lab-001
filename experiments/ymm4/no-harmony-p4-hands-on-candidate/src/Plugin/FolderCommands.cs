using System.Windows;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Shared product command surface. Timeline menu/tag interactions use this now;
/// the later management panel can call the same typed methods without owning a
/// second FolderDocument, Undo path, or host mutation implementation.
/// </summary>
internal sealed class FolderCommands
{
    private readonly Window window;
    private readonly Host host;
    private readonly Timeline timeline;
    private readonly UndoRedoManager undo;
    private readonly FolderStateStore state;
    private readonly StructuralFolderBridge structural;
    private readonly Action<string> log;

    internal FolderCommands(
        Window window,
        Host host,
        UndoRedoManager undo,
        FolderStateStore state,
        StructuralFolderBridge structural,
        Action<string> log)
    {
        this.window = window;
        this.host = host;
        this.undo = undo;
        this.state = state;
        this.structural = structural;
        this.log = log;
        timeline = host.Timeline;
    }

    private string TimelineKey => timeline.ID.ToString("D");

    internal FolderCreationDecision EvaluateCreate(
        int clickedLayer,
        Guid candidateId) =>
        FolderUxCommands.EvaluateCreateFromContext(
            state.Document,
            TimelineKey,
            timeline.LayerSelection.SelectedLayers,
            clickedLayer,
            candidateId);

    internal PersistedFolder? FindFolder(Guid folderId) =>
        FolderDocumentRules.FindTimeline(state.Document, TimelineKey)
            ?.Folders.FirstOrDefault(x => x.Id == folderId);

    internal FolderCreationDecision CreateFromContext(
        int clickedLayer,
        Guid folderId,
        string name)
    {
        if (state.IsRecoveryBlocked)
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");

        // Re-evaluate after the name prompt. The menu header is only a preview;
        // the actual edit must use the current project/timeline state.
        var decision = EvaluateCreate(clickedLayer, folderId);
        if (!decision.Allowed
            || decision.Start is null
            || decision.End is null)
        {
            throw new InvalidOperationException(
                $"Folder creation rejected: {decision.Status}");
        }

        if (!decision.NeedsAdditionalLayer)
        {
            CommitMetadata(
                document => FolderUxCommands.CreateFolder(
                    document,
                    TimelineKey,
                    decision,
                    folderId,
                    name),
                "create");
            timeline.LayerSelection.Clear();
            return decision;
        }

        var insertionPosition = checked(decision.End.Value + 1);
        if (!HandsOnHostAccess.CanExecuteTimelineCommand(
                host,
                window,
                CommandType.AddLayer,
                insertionPosition))
        {
            throw new InvalidOperationException(
                $"YMM4 cannot add the required empty layer at L{insertionPosition}.");
        }

        var before = FolderDocumentRules.NormalizeAndValidate(state.Document);
        var after = FolderUxCommands.CreateFolderWithInsertedLayer(
            before,
            TimelineKey,
            decision,
            folderId,
            name);

        using var composite = structural.PrepareCompositeOverride(
            CommandType.AddLayer,
            insertionPosition,
            before,
            after);

        if (!HandsOnHostAccess.TryExecuteTimelineCommand(
                host,
                window,
                CommandType.AddLayer,
                insertionPosition))
        {
            throw new InvalidOperationException(
                "The preflighted YMM4 Add Layer command was not executable.");
        }

        if (!composite.Applied)
        {
            throw new InvalidOperationException(
                "YMM4 Add Layer executed without entering the S1 composite history boundary.");
        }

        timeline.LayerSelection.Clear();
        log(
            $"folder_command create_with_insert start={decision.Start} " +
            $"end={decision.End} inserted={insertionPosition} id={folderId}");
        return decision;
    }

    internal void Rename(Guid folderId, string name) =>
        CommitMetadata(
            document => FolderUxCommands.RenameFolder(
                document,
                TimelineKey,
                folderId,
                name),
            "rename");

    internal void ToggleCollapsed(Guid folderId) =>
        CommitMetadata(
            document => FolderUxCommands.ToggleCollapsed(
                document,
                TimelineKey,
                folderId),
            "toggle");

    internal void SetAllCollapsed(bool collapsed) =>
        CommitMetadata(
            document => FolderUxCommands.SetAllCollapsed(
                document,
                TimelineKey,
                collapsed),
            collapsed ? "collapse_all" : "expand_all");

    internal void Ungroup(Guid folderId) =>
        CommitMetadata(
            document => FolderUxCommands.Ungroup(
                document,
                TimelineKey,
                folderId),
            "ungroup");

    internal void SelectItems(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        timeline.SelectItems(
            timeline.Items.Where(
                item => folder.Start <= item.Layer && item.Layer <= folder.End));

        log(
            $"folder_command select_items id={folderId} " +
            $"range={folder.Start}-{folder.End}");
    }

    private void CommitMetadata(
        Func<FolderDocument, FolderDocument> change,
        string reason)
    {
        if (state.IsRecoveryBlocked)
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");

        var before = state.Document;
        var after = FolderDocumentRules.NormalizeAndValidate(change(before));

        if (FolderDocumentCodec.Save(before) == FolderDocumentCodec.Save(after))
            return;

        state.ReplaceDocument(after);
        undo.AddCommand(new UndoRedoActionCommand(
            () => state.ReplaceDocument(before),
            () => state.ReplaceDocument(after)));
        undo.Record();

        log(
            $"folder_command metadata reason={reason} timeline={TimelineKey}");
    }
}
