using System.Windows;
using System.Windows.Media;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyS3;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
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
    private readonly FolderVisibilityCoordinator visibility;
    private readonly Action<string> log;

    internal FolderCommands(
        Window window,
        Host host,
        UndoRedoManager undo,
        FolderStateStore state,
        StructuralFolderBridge structural,
        FolderVisibilityCoordinator visibility,
        Action<string> log)
    {
        this.window = window;
        this.host = host;
        this.undo = undo;
        this.state = state;
        this.structural = structural;
        this.visibility = visibility;
        this.log = log;
        timeline = host.Timeline;
    }

    private string TimelineKey => timeline.ID.ToString("D");

    internal static IReadOnlyList<(string Name, string? Color)> Palette { get; } =
    [
        ("赤", "#FFE05A5A"),
        ("橙", "#FFE89A3C"),
        ("黄", "#FFD9C23A"),
        ("緑", "#FF5CB85C"),
        ("水", "#FF4CB5C9"),
        ("青", "#FF4F7FE0"),
        ("紫", "#FF9B6BD6"),
        ("灰", "#FF8A8A8A"),
        ("既定", null)
    ];

    internal FolderOptionState? FindOption(Guid folderId) =>
        FolderProductStateRules.FindOption(
            state.ProductState,
            TimelineKey,
            folderId);


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

    internal void SetColor(Guid folderId, string? color) =>
        CommitProductState(
            product => FolderProductStateRules.SetFolderColor(
                product,
                TimelineKey,
                folderId,
                color),
            "set_color");

    internal void SetHidden(Guid folderId, bool hidden)
    {
        if (state.IsRecoveryBlocked)
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");

        var before = FolderProductStateRules.NormalizeAndValidate(
            state.ProductState);
        var requested = FolderProductStateRules.SetFolderHidden(
            before,
            TimelineKey,
            folderId,
            hidden);
        var after = visibility.ApplyHiddenChange(
            before,
            requested);

        CommitResolvedProductState(
            before,
            after,
            hidden ? "hide" : "show");
    }

    internal void ApplyFolderColorToLayers(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");
        var option = FindOption(folderId);

        var color = option?.Color is { } text
            ? (Color)ColorConverter.ConvertFromString(text)
            : Colors.Transparent;

        var changed = false;
        for (var layer = folder.Start; layer <= folder.End; layer++)
        {
            if (timeline.LayerSettings.Colors[layer] == color)
                continue;
            timeline.LayerSettings.Colors[layer] = color;
            changed = true;
        }

        if (!changed)
            return;

        undo.Record();
        log(
            $"folder_command apply_color id={folderId} " +
            $"range={folder.Start}-{folder.End} color={option?.Color ?? "<default>"}");
    }

    internal void InsertLayerAtFolderEnd(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        InsertLayerIntoFolder(
            folderId,
            checked(folder.End + 1));
    }

    internal void InsertLayerAfterInFolder(
        Guid folderId,
        int logicalLayer)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        if (logicalLayer < folder.Start
            || logicalLayer > folder.End)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalLayer));
        }

        InsertLayerIntoFolder(
            folderId,
            checked(logicalLayer + 1));
    }

    internal IReadOnlyList<GroupIssue> GetGroupIssues(
        Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");
        var groups = GroupItems();

        return StructuralConvenienceRules.FindGroupIssues(
            folder,
            groups
                .Select(x => new GroupSpan(
                    x.Layer,
                    x.GroupRange))
                .ToArray());
    }

    internal int FitGroupsToFolder(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");
        var groups = GroupItems();
        var planned = StructuralConvenienceRules.FitGroupRanges(
            folder,
            groups
                .Select(x => new GroupSpan(
                    x.Layer,
                    x.GroupRange))
                .ToArray());

        var changed = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].GroupRange == planned[i])
                continue;

            groups[i].GroupRange = planned[i];
            changed++;
        }

        if (changed == 0)
            return 0;

        undo.Record();
        log(
            $"folder_command fit_groups id={folderId} changed={changed}");
        return changed;
    }

    private List<GroupItem> GroupItems() =>
        timeline.Items
            .OfType<GroupItem>()
            .OrderBy(x => x.Layer)
            .ThenBy(x => x.Frame)
            .ToList();

    private void InsertLayerIntoFolder(
        Guid folderId,
        int position)
    {
        if (state.IsRecoveryBlocked)
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");

        if (!HandsOnHostAccess.CanExecuteTimelineCommand(
                host,
                window,
                CommandType.AddLayer,
                position))
        {
            throw new InvalidOperationException(
                $"YMM4 cannot add a layer at L{position}.");
        }

        var before = FolderProductStateRules.NormalizeAndValidate(
            state.ProductState);
        var groups = GroupItems();
        var plan = StructuralConvenienceRules.PlanInsertIntoFolder(
            before,
            TimelineKey,
            folderId,
            position,
            1,
            groups
                .Select(x => new GroupSpan(
                    x.Layer,
                    x.GroupRange))
                .ToArray());

        Action nativeCompanion = () =>
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].GroupRange != plan.GroupRanges[i])
                    groups[i].GroupRange = plan.GroupRanges[i];
            }
        };

        using var composite =
            structural.PrepareProductCompositeOverride(
                CommandType.AddLayer,
                position,
                before,
                plan.State,
                nativeCompanion);

        if (!HandsOnHostAccess.TryExecuteTimelineCommand(
                host,
                window,
                CommandType.AddLayer,
                position))
        {
            throw new InvalidOperationException(
                "The preflighted YMM4 Add Layer command was not executable.");
        }

        if (!composite.Applied)
        {
            throw new InvalidOperationException(
                "Owned layer insertion did not enter the shared structural history boundary.");
        }

        timeline.LayerSelection.Clear();

        log(
            $"folder_command insert_owned id={folderId} " +
            $"position={position} groups={groups.Count}");
    }

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

    private void CommitProductState(
        Func<FolderProductState, FolderProductState> change,
        string reason)
    {
        if (state.IsRecoveryBlocked)
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");

        var before = FolderProductStateRules.NormalizeAndValidate(
            state.ProductState);
        var after = FolderProductStateRules.NormalizeAndValidate(
            change(before));
        CommitResolvedProductState(before, after, reason);
    }

    private void CommitResolvedProductState(
        FolderProductState before,
        FolderProductState after,
        string reason)
    {
        if (FolderProductStateCodec.Save(before)
            == FolderProductStateCodec.Save(after))
            return;

        state.ReplaceProductState(after);
        undo.AddCommand(new UndoRedoActionCommand(
            () => state.ReplaceProductState(before),
            () => state.ReplaceProductState(after)));
        undo.Record();

        log(
            $"folder_command product reason={reason} timeline={TimelineKey}");
    }

    private void CommitMetadata(
        Func<FolderDocument, FolderDocument> change,
        string reason)
    {
        if (state.IsRecoveryBlocked)
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");

        var beforeProduct = FolderProductStateRules.NormalizeAndValidate(
            state.ProductState);
        var before = beforeProduct.Core;
        var afterCore = FolderDocumentRules.NormalizeAndValidate(change(before));
        var afterProduct = FolderProductStateRules.ReplaceCore(
            beforeProduct,
            afterCore);
        afterProduct = visibility.ApplyHiddenChange(
            beforeProduct,
            afterProduct);

        CommitResolvedProductState(
            beforeProduct,
            afterProduct,
            reason);
    }
}
