using System.Windows;
using System.Windows.Media;
using Ymm4NoHarmonyPanel;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;
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
        string name) =>
        CreateFromDecision(
            EvaluateCreate(clickedLayer, folderId),
            folderId,
            name);

    internal FolderCreationDecision CreateFromExplicitRange(
        int start,
        int end,
        Guid folderId,
        string name)
    {
        if (start < 0 || end < start)
            throw new ArgumentOutOfRangeException(nameof(start));

        var decision = FolderUxCommands.EvaluateCreateFromContext(
            state.Document,
            TimelineKey,
            Enumerable.Range(
                start,
                checked(end - start + 1)),
            start,
            folderId);

        return CreateFromDecision(
            decision,
            folderId,
            name);
    }

    private FolderCreationDecision CreateFromDecision(
        FolderCreationDecision decision,
        Guid folderId,
        string name)
    {
        EnsureEditable();

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

        var insertionPosition = checked(
            decision.End.Value + 1);

        if (!HandsOnHostAccess.CanExecuteTimelineCommand(
                host,
                window,
                CommandType.AddLayer,
                insertionPosition))
        {
            throw new InvalidOperationException(
                $"YMM4 cannot add the required empty layer at L{insertionPosition}.");
        }

        var before = FolderDocumentRules.NormalizeAndValidate(
            state.Document);
        var after = FolderUxCommands.CreateFolderWithInsertedLayer(
            before,
            TimelineKey,
            decision,
            folderId,
            name);

        var existingGroups = CurrentGroups();
        var groupRanges =
            StructuralConvenienceRules.PlanStandardGroupRangesBeforeHost(
                before,
                TimelineKey,
                new Ymm4NoHarmonyFolderRanges.InsertLayers(
                    insertionPosition,
                    1),
                GroupSpans(existingGroups));

        using var composite = structural.PrepareCompositeOverride(
            CommandType.AddLayer,
            insertionPosition,
            before,
            after,
            () => HandsOnHostAccess.ApplyGroupRanges(
                existingGroups,
                groupRanges));

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
                "YMM4 Add Layer executed without entering the folder creation history boundary.");
        }

        timeline.LayerSelection.Clear();
        log(
            $"folder_command create start={decision.Start} " +
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

    internal int CountItemsInFolder(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        return timeline.Items.Count(
            item => folder.Start <= item.Layer && item.Layer <= folder.End);
    }

    internal void AddStandardLayer(int position)
    {
        EnsureEditable();

        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        if (!HandsOnHostAccess.TryExecuteTimelineCommand(
                host,
                window,
                CommandType.AddLayer,
                position))
        {
            throw new InvalidOperationException(
                $"YMM4 cannot add a layer at L{position}.");
        }
    }

    internal void ToggleLayerVisibility(int layer)
    {
        EnsureEditable();

        if (layer < 0)
            throw new ArgumentOutOfRangeException(nameof(layer));

        var before = FolderProductStateRules.NormalizeAndValidate(
            state.ProductState);
        var hidden = FolderProductStateRules.HiddenLayers(
            before,
            TimelineKey);

        if (hidden.Contains(layer))
        {
            var restore = FolderProductStateRules.RestoreMap(
                    before,
                    TimelineKey)
                .ToDictionary(x => x.Key, x => x.Value);
            var desired = restore.TryGetValue(layer, out var remembered)
                ? remembered
                : timeline.LayerSettings.IsVisibles[layer];

            restore[layer] = !desired;

            // Panel eye edits the post-folder state. Keep the native row
            // suppressed while a Hidden reason still owns it.
            if (timeline.LayerSettings.IsVisibles[layer])
                timeline.LayerSettings.IsVisibles[layer] = false;

            var after = FolderProductStateRules.ReplaceRestoreMap(
                before,
                TimelineKey,
                restore);
            CommitResolvedProductState(
                before,
                after,
                "toggle_layer_visibility_hidden");
            return;
        }

        timeline.LayerSettings.IsVisibles[layer] =
            !timeline.LayerSettings.IsVisibles[layer];
        undo.Record();

        log(
            $"folder_command toggle_layer_visibility layer={layer}");
    }

    internal void AddLayerAtFolderEnd(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        InsertLayerIntoFolder(
            folderId,
            checked(folder.End + 1));
    }

    internal void AddLayerBelowInsideFolder(
        Guid folderId,
        int layer)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        if (layer < folder.Start || layer > folder.End)
            throw new ArgumentOutOfRangeException(nameof(layer));

        InsertLayerIntoFolder(
            folderId,
            checked(layer + 1));
    }

    private void InsertLayerIntoFolder(
        Guid folderId,
        int position)
    {
        EnsureEditable();

        var groups = CurrentGroups();
        var plan = StructuralConvenienceRules.PlanInsertIntoFolder(
            state.Document,
            TimelineKey,
            folderId,
            position,
            1,
            GroupSpans(groups));

        ApplyPluginStructuralPlan(
            plan,
            groups,
            newGroup: null,
            "insert_into_folder");
    }

    internal void DeleteLayers(
        IEnumerable<int> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        EnsureEditable();

        var targets = layers
            .Distinct()
            .OrderByDescending(x => x)
            .ToArray();

        if (targets.Length == 0)
            return;
        if (targets.Any(x => x < 0))
            throw new ArgumentOutOfRangeException(nameof(layers));

        var beforeProduct =
            FolderProductStateRules.NormalizeAndValidate(
                state.ProductState);
        var working = beforeProduct;

        foreach (var layer in targets)
        {
            var timelineState = FolderDocumentRules.FindTimeline(
                working.Core,
                TimelineKey);
            var edit =
                new Ymm4NoHarmonyFolderRanges.DeleteLayers(
                    layer,
                    1);

            var rangePlan =
                Ymm4NoHarmonyFolderRanges.FolderRangeTracker.Apply(
                    timelineState?.Folders.Select(
                        folder =>
                            new Ymm4NoHarmonyFolderRanges.FolderRange(
                                folder.Id,
                                folder.Start,
                                folder.End))
                    ?? [],
                    edit);

            var beforeById = (timelineState?.Folders ?? [])
                .ToDictionary(x => x.Id);
            var nextFolders = rangePlan.Ranges
                .Select(range =>
                {
                    var original = beforeById[range.Id];
                    return original with
                    {
                        Start = range.Start,
                        End = range.End
                    };
                })
                .ToArray();

            var nextCore = FolderDocumentRules.ReplaceTimeline(
                working.Core,
                TimelineKey,
                nextFolders);
            var next = FolderProductStateRules.ReplaceCore(
                working,
                nextCore);
            next = FolderProductStateRules.RemapRestoreLayers(
                next,
                TimelineKey,
                rangePlan.MapLayer);

            var groups = CurrentGroups();
            var groupRanges =
                StructuralConvenienceRules.PlanStandardGroupRangesBeforeHost(
                    working.Core,
                    TimelineKey,
                    edit,
                    GroupSpans(groups));

            HandsOnHostAccess.ApplyGroupRanges(
                groups,
                groupRanges);
            timeline.DeleteLayer(layer);

            next = visibility.ApplyStructuralChange(
                working,
                next,
                rangePlan.MapLayer);
            working = next;
        }

        state.ReplaceProductState(working);
        undo.AddCommand(new UndoRedoActionCommand(
            () => state.ReplaceProductState(beforeProduct),
            () => state.ReplaceProductState(working)));
        undo.Record();

        log(
            $"folder_command delete_layers count={targets.Length} " +
            $"layers={string.Join(",", targets)}");
    }

    internal void DeleteFolderContents(Guid folderId)
    {
        EnsureEditable();

        var groups = CurrentGroups();
        var plan = StructuralConvenienceRules.PlanDeleteFolderContents(
            state.Document,
            TimelineKey,
            folderId,
            GroupSpans(groups));

        ApplyPluginStructuralPlan(
            plan,
            groups,
            newGroup: null,
            "delete_folder_contents");
    }

    internal bool AddGroupControl(Guid folderId)
    {
        EnsureEditable();

        var groups = CurrentGroups();
        var plan = StructuralConvenienceRules.PlanAddGroupControl(
            state.Document,
            TimelineKey,
            folderId,
            GroupSpans(groups));

        var item = new GroupItem
        {
            Frame = 0,
            Length = Math.Max(1, timeline.Length),
            Layer = plan.GroupLayer,
            GroupRange = plan.GroupRange
        };

        ApplyPluginStructuralPlan(
            plan.Structural,
            groups,
            item,
            "add_group_control");

        return timeline.Items.Any(
            existing => ReferenceEquals(existing, item));
    }

    internal IReadOnlyList<GroupIssue> GetGroupIssues(
        Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        var groups = CurrentGroups();
        return StructuralConvenienceRules.FindGroupIssues(
            folder,
            GroupSpans(groups));
    }

    internal int FitGroupRanges(Guid folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        var groups = CurrentGroups();
        var spans = GroupSpans(groups);
        var ranges = StructuralConvenienceRules.PlanFitGroupRanges(
            folder,
            spans);

        var changed = HandsOnHostAccess.ApplyGroupRanges(
            groups,
            ranges);

        if (changed == 0)
            return 0;

        undo.Record();
        log(
            $"folder_command fit_groups id={folderId} changed={changed}");
        return changed;
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

        SelectItemsInLayers(
            folder.Start,
            folder.End);
    }

    internal void SelectItemsInLayers(
        int start,
        int end)
    {
        if (start < 0 || end < start)
            throw new ArgumentOutOfRangeException(nameof(start));

        timeline.SelectItems(
            timeline.Items.Where(
                item => start <= item.Layer
                    && item.Layer <= end));

        log(
            $"folder_command select_items range={start}-{end}");
    }

    internal bool CanMovePanelRows(
        PanelDragBlock block,
        PanelDropTarget drop) =>
        PanelMoveRules.CanDrop(
            state.Document,
            TimelineKey,
            block,
            drop);

    internal void MovePanelRows(
        PanelDragBlock block,
        PanelDropTarget drop)
    {
        EnsureEditable();

        var groups = CurrentGroups();
        var plan = PanelMoveRules.PlanMove(
            state.Document,
            TimelineKey,
            block,
            drop,
            GroupSpans(groups));

        ApplyPanelMovePlan(
            plan,
            groups);
    }

    private GroupItem[] CurrentGroups() =>
        timeline.Items
            .OfType<GroupItem>()
            .OrderBy(group => group.Layer)
            .ThenBy(group => group.Frame)
            .ToArray();

    private static GroupSpan[] GroupSpans(
        IReadOnlyList<GroupItem> groups) =>
        groups
            .Select(group => new GroupSpan(
                group.Layer,
                group.GroupRange))
            .ToArray();

    private void EnsureEditable()
    {
        if (state.IsRecoveryBlocked)
        {
            throw new InvalidOperationException(
                "Unreadable saved folder state is preserved; editing is blocked.");
        }
    }

    private void ApplyPluginStructuralPlan(
        StructuralConveniencePlan plan,
        IReadOnlyList<GroupItem> groups,
        GroupItem? newGroup,
        string reason)
    {
        EnsureEditable();

        var beforeProduct =
            FolderProductStateRules.NormalizeAndValidate(
                state.ProductState);

        var afterProduct = FolderProductStateRules.ReplaceCore(
            beforeProduct,
            plan.Core);
        afterProduct = FolderProductStateRules.RemapRestoreLayers(
            afterProduct,
            TimelineKey,
            plan.MapLayer);

        HandsOnHostAccess.ApplyStructuralPlan(
            timeline,
            plan,
            groups,
            newGroup);

        afterProduct = visibility.ApplyStructuralChange(
            beforeProduct,
            afterProduct,
            plan.MapLayer);

        state.ReplaceProductState(afterProduct);
        undo.AddCommand(new UndoRedoActionCommand(
            () => state.ReplaceProductState(beforeProduct),
            () => state.ReplaceProductState(afterProduct)));
        undo.Record();

        log(
            $"folder_command structural reason={reason} " +
            $"groups={groups.Count} new_group={newGroup is not null}");
    }

    private void ApplyPanelMovePlan(
        PanelMovePlan plan,
        IReadOnlyList<GroupItem> groups)
    {
        EnsureEditable();

        var beforeProduct =
            FolderProductStateRules.NormalizeAndValidate(
                state.ProductState);

        var afterProduct = FolderProductStateRules.ReplaceCore(
            beforeProduct,
            plan.Core);
        afterProduct = FolderProductStateRules.RemapRestoreLayers(
            afterProduct,
            TimelineKey,
            plan.MapLayer);

        HandsOnHostAccess.ApplyPanelMovePlan(
            timeline,
            plan,
            groups);

        afterProduct = visibility.ApplyStructuralChange(
            beforeProduct,
            afterProduct,
            plan.MapLayer);

        state.ReplaceProductState(afterProduct);
        undo.AddCommand(new UndoRedoActionCommand(
            () => state.ReplaceProductState(beforeProduct),
            () => state.ReplaceProductState(afterProduct)));
        undo.Record();

        log(
            $"folder_command panel_move start={plan.Start} " +
            $"count={plan.Count} target={plan.OriginalInsertionBoundary} " +
            $"final={plan.FinalInsertionPoint} into={plan.IntoFolderId}");
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
