using System.Windows;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyState;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Shared typed product command surface. S2 history snapshots always use the
/// single FolderSessionDocument so Core and visual metadata cannot diverge.
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
        FolderDocumentRules.FindTimeline(
            state.Document,
            TimelineKey)
            ?.Folders.FirstOrDefault(x => x.Id == folderId);

    internal FolderCreationDecision CreateFromContext(
        int clickedLayer,
        Guid folderId,
        string name)
    {
        EnsureEditable();

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
            CommitCore(
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

        var beforeState =
            FolderSessionDocumentRules.NormalizeAndValidate(state.State);

        var afterCore = FolderUxCommands.CreateFolderWithInsertedLayer(
            beforeState.Core,
            TimelineKey,
            decision,
            folderId,
            name);

        var sourceTimeline = FolderDocumentRules.FindTimeline(
            beforeState.Core,
            TimelineKey);

        var structuralPlan = FolderRangeTracker.Apply(
            sourceTimeline?.Folders.Select(
                x => new FolderRange(x.Id, x.Start, x.End))
                ?? Array.Empty<FolderRange>(),
            new InsertLayers(insertionPosition, 1));

        var afterState =
            FolderSessionDocumentRules.ReplaceCoreAfterStructuralEdit(
                beforeState,
                afterCore,
                TimelineKey,
                structuralPlan);

        using var composite = structural.PrepareCompositeOverride(
            CommandType.AddLayer,
            insertionPosition,
            beforeState,
            afterState);

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
        CommitCore(
            document => FolderUxCommands.RenameFolder(
                document,
                TimelineKey,
                folderId,
                name),
            "rename");

    internal void ToggleCollapsed(Guid folderId) =>
        CommitCore(
            document => FolderUxCommands.ToggleCollapsed(
                document,
                TimelineKey,
                folderId),
            "toggle");

    internal void SetAllCollapsed(bool collapsed) =>
        CommitCore(
            document => FolderUxCommands.SetAllCollapsed(
                document,
                TimelineKey,
                collapsed),
            collapsed ? "collapse_all" : "expand_all");

    internal void Ungroup(Guid folderId) =>
        CommitCore(
            document => FolderUxCommands.Ungroup(
                document,
                TimelineKey,
                folderId),
            "ungroup");

    internal void SetColor(Guid folderId, string? color)
    {
        var option =
            FolderSessionDocumentRules.FindOption(
                state.State,
                TimelineKey,
                folderId);

        CommitState(
            current => FolderSessionDocumentRules.SetFolderOption(
                current,
                TimelineKey,
                folderId,
                color,
                option?.Hidden ?? false),
            "set_color");
    }

    internal void SelectItems(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        timeline.SelectItems(
            timeline.Items.Where(
                item =>
                    folder.Start <= item.Layer
                    && item.Layer <= folder.End));

        log(
            $"folder_command select_items id={folderId} " +
            $"range={folder.Start}-{folder.End}");
    }

    private void CommitCore(
        Func<FolderDocument, FolderDocument> change,
        string reason) =>
        CommitState(
            current => FolderSessionDocumentRules.ReplaceCore(
                current,
                FolderDocumentRules.NormalizeAndValidate(
                    change(current.Core))),
            reason);

    private void CommitState(
        Func<FolderSessionDocument, FolderSessionDocument> change,
        string reason)
    {
        EnsureEditable();

        var before =
            FolderSessionDocumentRules.NormalizeAndValidate(state.State);
        var after =
            FolderSessionDocumentRules.NormalizeAndValidate(change(before));

        if (FolderSessionDocumentCodec.Save(before)
            == FolderSessionDocumentCodec.Save(after))
            return;

        state.ReplaceState(after);
        undo.AddCommand(new UndoRedoActionCommand(
            () => state.ReplaceState(before),
            () => state.ReplaceState(after)));
        undo.Record();

        log(
            $"folder_command metadata reason={reason} timeline={TimelineKey}");
    }

    private void EnsureEditable()
    {
        if (state.IsRecoveryBlocked)
        {
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");
        }
    }
}
