using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ymm4NoHarmonyPanel;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YmmGroupItem = YukkuriMovieMaker.Project.Items.GroupItem;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed partial class FolderToolViewModel
{
    private readonly Dictionary<string, FolderPanelRowViewModel> panelRowCache = [];
    private readonly DispatcherTimer panelFrameTimer;
    private Timeline? panelTimeline;
    private INotifyPropertyChanged? panelTimelineSignal;
    private INotifyPropertyChanged? panelLayerSettingsSignal;
    private bool panelDisposed;
    private bool panelRebuildPending;
    private bool followTimeline = true;
    private bool showCurrentItem = true;
    private string panelStatusText = "管理パネルを読み込んでいます。";

    public ObservableCollection<FolderPanelRowViewModel> Rows { get; } = [];

    public bool FollowTimeline
    {
        get => followTimeline;
        set
        {
            if (followTimeline == value)
                return;
            followTimeline = value;
            RaisePanelPropertyChanged();
        }
    }

    public bool ShowCurrentItem
    {
        get => showCurrentItem;
        set
        {
            if (showCurrentItem == value)
                return;
            showCurrentItem = value;
            RaisePanelPropertyChanged();
            UpdateCurrentItems();
        }
    }

    public bool HasTimeline => panelTimeline is not null;

    public FolderToolViewModel()
    {
        panelFrameTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        panelFrameTimer.Tick += (_, _) =>
        {
            panelFrameTimer.Stop();
            UpdateCurrentItems();
        };

        Store.Changed += Store_Changed;
        HandsOnRuntime.ActiveControllerChanged += ActiveControllerChanged;
    }

    public void SetTimelineToolInfo(TimelineToolInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        AttachPanelTimeline(info.Timeline);
    }

    public void Dispose()
    {
        if (panelDisposed)
            return;

        panelDisposed = true;
        panelFrameTimer.Stop();
        Store.Changed -= Store_Changed;
        HandsOnRuntime.ActiveControllerChanged -= ActiveControllerChanged;
        DetachPanelTimeline();
        Rows.Clear();
        panelRowCache.Clear();
    }

    private void AttachPanelTimeline(Timeline timeline)
    {
        if (panelDisposed)
            return;

        if (ReferenceEquals(panelTimeline, timeline))
        {
            RebuildPanel();
            return;
        }

        DetachPanelTimeline();
        panelTimeline = timeline;

        panelTimelineSignal = timeline;
        panelTimelineSignal.PropertyChanged += PanelTimeline_PropertyChanged;

        panelLayerSettingsSignal = timeline.LayerSettings;
        panelLayerSettingsSignal.PropertyChanged += PanelLayerSettings_PropertyChanged;

        RaisePanelPropertyChanged(nameof(HasTimeline));
        RebuildPanel();
    }

    private void DetachPanelTimeline()
    {
        if (panelTimelineSignal is not null)
            panelTimelineSignal.PropertyChanged -= PanelTimeline_PropertyChanged;
        if (panelLayerSettingsSignal is not null)
            panelLayerSettingsSignal.PropertyChanged -= PanelLayerSettings_PropertyChanged;

        panelTimelineSignal = null;
        panelLayerSettingsSignal = null;
        panelTimeline = null;
    }

    private void Store_Changed(object? sender, EventArgs e) =>
        SchedulePanelRebuild();

    private void ActiveControllerChanged(object? sender, EventArgs e) =>
        SchedulePanelRebuild();

    private void PanelTimeline_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (panelDisposed)
            return;

        if (e.PropertyName == nameof(Timeline.CurrentFrame))
        {
            if (ShowCurrentItem && !panelFrameTimer.IsEnabled)
                panelFrameTimer.Start();
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(Timeline.Items)
                or nameof(Timeline.MaxLayer)
                or nameof(Timeline.SelectedItems)
                or nameof(Timeline.Length))
        {
            SchedulePanelRebuild();
        }
    }

    private void PanelLayerSettings_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e) =>
        SchedulePanelRebuild();

    private void SchedulePanelRebuild()
    {
        if (panelDisposed || panelRebuildPending)
            return;

        panelRebuildPending = true;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                panelRebuildPending = false;
                RebuildPanel();
            }),
            DispatcherPriority.Background);
    }

    internal void RebuildPanel()
    {
        if (panelDisposed)
            return;

        if (panelTimeline is null)
        {
            Rows.Clear();
            panelRowCache.Clear();
            SetPanelStatus("タイムラインを開くと管理一覧を表示します。");
            return;
        }

        try
        {
            var timeline = panelTimeline;
            var key = timeline.ID.ToString("D");

            var itemsByLayer = timeline.Items
                .GroupBy(item => item.Layer)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(item => item.Frame)
                        .ToArray());

            var itemCounts = itemsByLayer
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Length);

            var groupItems = timeline.Items
                .OfType<YmmGroupItem>()
                .OrderBy(group => group.Layer)
                .ThenBy(group => group.Frame)
                .ToArray();
            var groups = groupItems
                .Select(group => new GroupSpan(
                    group.Layer,
                    group.GroupRange))
                .ToArray();

            var maxLayer = Math.Max(
                timeline.MaxLayer,
                timeline.LayerSettings.MaxLayer);

            var pureRows = PanelProjection.Build(
                Store.ProductState,
                key,
                maxLayer,
                itemCounts,
                groups);

            var nextRows = new List<FolderPanelRowViewModel>(
                pureRows.Count);

            foreach (var pure in pureRows)
            {
                if (!panelRowCache.TryGetValue(
                        pure.Key,
                        out var row))
                {
                    row = new FolderPanelRowViewModel(pure.Key);
                    panelRowCache[pure.Key] = row;
                }

                UpdatePanelRow(
                    row,
                    pure,
                    timeline,
                    itemsByLayer,
                    groups);
                nextRows.Add(row);
            }

            SyncPanelRows(nextRows);

            var alive = nextRows
                .Select(row => row.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var keyToRemove in panelRowCache.Keys
                .Where(keyToRemove => !alive.Contains(keyToRemove))
                .ToArray())
            {
                panelRowCache.Remove(keyToRemove);
            }

            UpdateCurrentItems();

            var controller = HandsOnRuntime.ControllerFor(timeline.ID);
            SetPanelStatus(
                controller is null
                    ? "一覧は表示中です。タイムライン操作の接続を待っています。"
                    : $"{Rows.Count} 行を表示中。Ctrl+G: フォルダ化 / F2: 名前変更 / Delete: 解除・削除");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "panel_rebuild_error=" + ex);
            SetPanelStatus(
                "管理パネルの更新に失敗しました: "
                + ex.GetBaseException().Message);
        }
    }

    private void UpdatePanelRow(
        FolderPanelRowViewModel row,
        PanelRow pure,
        Timeline timeline,
        IReadOnlyDictionary<int, IItem[]> itemsByLayer,
        IReadOnlyList<GroupSpan> groups)
    {
        var wasEditing = row.IsEditing;
        var editName = row.EditName;
        var wasSelected = row.IsSelected;

        row.PureRow = pure;
        row.IsSelected = wasSelected;
        row.Indent = new Thickness(
            pure.Depth * 14,
            0,
            0,
            0);

        if (pure.Kind == PanelRowKind.Folder)
        {
            row.DisplayName = pure.Name;
            if (!wasEditing)
                row.EditName = pure.Name;
            else
                row.EditName = editName;

            row.RangeText = pure.First == pure.Last
                ? $"L{pure.First:00}"
                : $"L{pure.First:00}–L{pure.Last:00}";
            row.DisclosureText = pure.IsCollapsed ? "▶" : "▼";
            row.VisibilityText = pure.IsHidden ? "◌" : "●";
            row.CountText = $"{pure.ItemCount} item";
            row.WarningText = BuildFolderWarning(
                pure,
                groups);
            row.GroupLaneText = "";
            row.ColorBrush = FolderColorBrush(
                pure.FolderId);
            row.OwnVisible = !pure.IsHidden;
        }
        else
        {
            var layer = pure.First;
            var setting = timeline.LayerSettings.Items
                .FirstOrDefault(candidate =>
                    candidate.Layer == layer);

            row.DisplayName = string.IsNullOrWhiteSpace(setting?.Label)
                ? $"L{layer:00}"
                : $"L{layer:00}  {setting.Label}";
            row.EditName = row.DisplayName;
            row.RangeText = "";
            row.DisclosureText = "";
            row.CountText = pure.ItemCount == 0
                ? "empty"
                : $"{pure.ItemCount} item";
            row.WarningText = "";
            row.GroupLaneText = BuildGroupLanes(
                layer,
                groups);

            var restore = FolderProductStateRules.RestoreMap(
                Store.ProductState,
                timeline.ID.ToString("D"));
            var actualVisible = TryLayerVisible(
                timeline,
                layer);
            var desiredVisible = restore.TryGetValue(
                layer,
                out var restored)
                ? restored
                : actualVisible;

            row.OwnVisible = desiredVisible;
            row.VisibilityText = desiredVisible ? "●" : "○";
            row.ColorBrush = LayerColorBrush(
                timeline,
                layer);
        }

        row.IsEditing = wasEditing;
        row.RefreshDerived();
    }

    private Brush? FolderColorBrush(Guid? folderId)
    {
        if (panelTimeline is null
            || folderId is not { } id)
        {
            return null;
        }

        var option = FolderProductStateRules.FindOption(
            Store.ProductState,
            panelTimeline.ID.ToString("D"),
            id);

        if (string.IsNullOrWhiteSpace(option?.Color))
            return null;

        try
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(
                    option.Color));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Brush? LayerColorBrush(
        Timeline timeline,
        int layer)
    {
        try
        {
            var color = timeline.LayerSettings.Colors[layer];
            if (color.A == 0)
                return null;

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryLayerVisible(
        Timeline timeline,
        int layer)
    {
        try
        {
            return timeline.LayerSettings.IsVisibles[layer];
        }
        catch
        {
            return true;
        }
    }

    private static string BuildGroupLanes(
        int layer,
        IReadOnlyList<GroupSpan> groups)
    {
        var spans = groups
            .Where(group =>
                group.Layer <= layer
                && layer <= group.LastControlled)
            .OrderBy(group => group.Layer)
            .ThenByDescending(group => group.Range)
            .Take(3)
            .ToArray();

        if (spans.Length == 0)
            return "";

        return string.Join(
            " ",
            spans.Select(group =>
                layer == group.Layer
                    ? "┬"
                    : layer == group.LastControlled
                        ? "└"
                        : "│"));
    }

    private static string BuildFolderWarning(
        PanelRow folder,
        IReadOnlyList<GroupSpan> groups)
    {
        if (folder.FolderId is null
            || folder.GroupIssueCount == 0)
        {
            return "";
        }

        var persisted = new Ymm4NoHarmonyPersistence.PersistedFolder
        {
            Id = folder.FolderId.Value,
            Start = folder.First,
            End = folder.Last,
            Name = folder.Name,
            IsCollapsed = folder.IsCollapsed
        };

        var issues = StructuralConvenienceRules.FindGroupIssues(
            persisted,
            groups);

        return string.Join(
            Environment.NewLine,
            issues.Select(issue =>
            {
                var group = groups[issue.GroupIndex];
                return issue.Kind == GroupIssueKind.LeaksOutOfFolder
                    ? $"L{group.Layer:00} のGroupが L{group.LastControlled:00} まで外へ伸びています"
                    : $"L{group.Layer:00} のGroupが L{group.LastControlled:00} でフォルダ途中までです";
            }));
    }

    private void SyncPanelRows(
        IReadOnlyList<FolderPanelRowViewModel> nextRows)
    {
        for (var i = 0; i < nextRows.Count; i++)
        {
            if (i < Rows.Count
                && ReferenceEquals(Rows[i], nextRows[i]))
            {
                continue;
            }

            var existingIndex = -1;
            for (var j = i + 1; j < Rows.Count; j++)
            {
                if (ReferenceEquals(
                        Rows[j],
                        nextRows[i]))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
                Rows.Move(existingIndex, i);
            else
                Rows.Insert(i, nextRows[i]);
        }

        while (Rows.Count > nextRows.Count)
            Rows.RemoveAt(Rows.Count - 1);
    }

    private void UpdateCurrentItems()
    {
        if (panelTimeline is null)
            return;

        var frame = panelTimeline.CurrentFrame;
        var activeLayers = new HashSet<int>();

        foreach (var row in Rows)
        {
            if (!ShowCurrentItem)
            {
                row.CurrentItem = "";
                row.Summary = "";
                continue;
            }

            if (row.Kind == PanelRowKind.Layer)
            {
                var item = panelTimeline.Items
                    .Where(candidate =>
                        candidate.Layer == row.First
                        && candidate.Frame <= frame
                        && frame < candidate.Frame + candidate.Length)
                    .OrderBy(candidate => candidate.Frame)
                    .FirstOrDefault();

                row.CurrentItem = item is null
                    ? ""
                    : DescribePanelItem(item);

                if (item is not null)
                    activeLayers.Add(row.First);
            }
            else
            {
                row.CurrentItem = "";
            }
        }

        if (!ShowCurrentItem)
            return;

        foreach (var item in panelTimeline.Items)
        {
            if (item.Frame <= frame
                && frame < item.Frame + item.Length)
            {
                activeLayers.Add(item.Layer);
            }
        }

        foreach (var row in Rows.Where(
            row => row.Kind == PanelRowKind.Folder))
        {
            var active = activeLayers.Count(
                layer =>
                    row.First <= layer
                    && layer <= row.Last);
            row.Summary = active == 0
                ? ""
                : $"表示中 {active}";
        }
    }

    private static string DescribePanelItem(IItem item)
    {
        var text = string.IsNullOrWhiteSpace(item.Remark)
            ? item.Label
            : item.Remark;
        text = (text ?? "").ReplaceLineEndings(" ");
        return text.Length > 36
            ? text[..36] + "…"
            : text;
    }

    private FolderCommands RequirePanelCommands()
    {
        var timeline = panelTimeline
            ?? throw new InvalidOperationException(
                "タイムラインが開かれていません。");
        var controller = HandsOnRuntime.ControllerFor(
            timeline.ID)
            ?? throw new InvalidOperationException(
                "タイムライン操作の接続を待っています。");
        return controller.PanelCommands;
    }

    internal void ActivateRow(
        FolderPanelRowViewModel row)
    {
        if (!FollowTimeline
            || panelTimeline is null)
        {
            return;
        }

        HandsOnRuntime.ControllerFor(panelTimeline.ID)
            ?.ScrollPanelToLayer(row.First);
    }

    internal void ToggleCollapsed(
        FolderPanelRowViewModel row)
    {
        if (row.FolderId is not { } id)
            return;

        RunPanelAction(
            () => RequirePanelCommands().ToggleCollapsed(id));
    }

    internal void BeginRename(
        FolderPanelRowViewModel row)
    {
        if (row.FolderId is null)
            return;

        row.EditName = row.DisplayName;
        row.IsEditing = true;
        row.RefreshDerived();
    }

    internal void CommitRename(
        FolderPanelRowViewModel row)
    {
        if (!row.IsEditing
            || row.FolderId is not { } id)
        {
            return;
        }

        var name = row.EditName.Trim();
        if (name.Length == 0)
            name = "フォルダ";

        row.IsEditing = false;
        row.RefreshDerived();

        RunPanelAction(
            () => RequirePanelCommands().Rename(
                id,
                name));
    }

    internal void CancelRename(
        FolderPanelRowViewModel row)
    {
        row.IsEditing = false;
        row.EditName = row.PureRow.Name;
        row.RefreshDerived();
    }

    internal void CreateFolderFromSelection()
    {
        if (panelTimeline is null)
            return;

        var selected = Rows
            .Where(row => row.IsSelected)
            .ToArray();

        int start;
        int end;

        if (selected.Length == 0)
        {
            var trailing = Rows
                .Where(row =>
                    row.Kind == PanelRowKind.Layer)
                .OrderByDescending(row => row.First)
                .FirstOrDefault();
            if (trailing is null)
                return;
            start = end = trailing.First;
        }
        else
        {
            start = selected.Min(row => row.First);
            end = selected.Max(row => row.Last);
        }

        var folderCount = FolderDocumentRules.FindTimeline(
                Store.Document,
                panelTimeline.ID.ToString("D"))
            ?.Folders.Count ?? 0;
        var fallback = $"フォルダ {folderCount + 1}";
        var name = TextPrompt.Show(
            "新しいフォルダ名",
            fallback);
        if (name is null)
            return;
        if (string.IsNullOrWhiteSpace(name))
            name = fallback;

        var id = Guid.NewGuid();

        RunPanelAction(() =>
            RequirePanelCommands().CreateFromExplicitRange(
                start,
                end,
                id,
                name));

        SchedulePanelRebuild();

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                var created = Rows.FirstOrDefault(
                    row => row.FolderId == id);
                if (created is null)
                    return;

                foreach (var row in Rows)
                    row.IsSelected = ReferenceEquals(row, created);

                BeginRename(created);
            }),
            DispatcherPriority.ContextIdle);
    }

    internal void AddLayerFromSelection()
    {
        var row = Rows.LastOrDefault(
            candidate => candidate.IsSelected)
            ?? Rows.LastOrDefault();

        if (row is null)
            return;

        RunPanelAction(() =>
        {
            var commands = RequirePanelCommands();

            if (row.FolderId is { } folderId)
            {
                commands.AddLayerAtFolderEnd(folderId);
                return;
            }

            if (row.ParentFolderId is { } parentId)
            {
                commands.AddLayerBelowInsideFolder(
                    parentId,
                    row.First);
                return;
            }

            commands.AddStandardLayer(
                checked(row.First + 1));
        });
    }

    internal void DeleteSelection()
    {
        if (panelTimeline is null)
            return;

        var selected = Rows
            .Where(row => row.IsSelected)
            .ToArray();
        if (selected.Length == 0)
            return;

        var folders = selected
            .Where(row => row.FolderId is not null)
            .ToArray();

        var layers = selected
            .Where(row =>
                row.Kind == PanelRowKind.Layer
                && !folders.Any(folder =>
                    folder.First <= row.First
                    && row.Last <= folder.Last))
            .Select(row => row.First)
            .Distinct()
            .OrderByDescending(layer => layer)
            .ToArray();

        if (layers.Length > 0)
        {
            var itemCount = panelTimeline.Items.Count(
                item => layers.Contains(item.Layer));
            var message =
                $"{layers.Length} 枚のレイヤーを削除します" +
                $"（アイテム {itemCount} 個も削除されます）。";

            if (MessageBox.Show(
                    message,
                    DisplayTitle,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning)
                != MessageBoxResult.OK)
            {
                return;
            }

            RunPanelAction(
                () => RequirePanelCommands().DeleteLayers(
                    layers));
        }

        foreach (var folder in folders)
        {
            if (folder.FolderId is not { } id)
                continue;

            if (FolderDocumentRules.FindTimeline(
                    Store.Document,
                    panelTimeline.ID.ToString("D"))
                ?.Folders.Any(candidate =>
                    candidate.Id == id) == true)
            {
                RunPanelAction(
                    () => RequirePanelCommands().Ungroup(id));
            }
        }
    }

    internal void UngroupSelection()
    {
        foreach (var row in Rows
            .Where(row =>
                row.IsSelected
                && row.FolderId is not null)
            .ToArray())
        {
            var id = row.FolderId!.Value;
            RunPanelAction(
                () => RequirePanelCommands().Ungroup(id));
        }
    }

    internal void ToggleVisibilitySelection()
    {
        var selected = Rows
            .Where(row => row.IsSelected)
            .ToArray();
        if (selected.Length == 0)
            return;

        foreach (var row in selected)
        {
            RunPanelAction(() =>
            {
                var commands = RequirePanelCommands();

                if (row.FolderId is { } id)
                {
                    commands.SetHidden(
                        id,
                        !row.IsHidden);
                }
                else
                {
                    commands.ToggleLayerVisibility(
                        row.First);
                }
            });
        }
    }

    private FolderPanelRowViewModel? CurrentSelectedRow() =>
        Rows.LastOrDefault(row => row.IsSelected);

    private FolderPanelRowViewModel? CurrentSelectedFolder() =>
        Rows.LastOrDefault(row =>
            row.IsSelected
            && row.FolderId is not null);

    internal void SelectItemsCurrent()
    {
        if (CurrentSelectedRow() is { } row)
            SelectItems(row);
    }

    internal void SetColorCurrent(string? color)
    {
        if (CurrentSelectedFolder()?.FolderId
            is not { } id)
        {
            return;
        }

        RunPanelAction(
            () => RequirePanelCommands().SetColor(
                id,
                color));
    }

    internal void ApplyColorCurrent()
    {
        if (CurrentSelectedFolder()?.FolderId
            is not { } id)
        {
            return;
        }

        RunPanelAction(
            () => RequirePanelCommands()
                .ApplyFolderColorToLayers(id));
    }

    internal void DeleteCurrentFolderWithLayers()
    {
        if (panelTimeline is null
            || CurrentSelectedFolder()
                is not { FolderId: { } id } row)
        {
            return;
        }

        var layerCount =
            row.Last - row.First + 1;
        var itemCount = panelTimeline.Items.Count(
            item =>
                row.First <= item.Layer
                && item.Layer <= row.Last);

        var message =
            $"フォルダ「{row.DisplayName}」と中の {layerCount} レイヤー" +
            $"（アイテム {itemCount} 個）を削除します。\n" +
            "元に戻すには YMM4 の「元に戻す」を使ってください。";

        if (MessageBox.Show(
                message,
                DisplayTitle,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning)
            != MessageBoxResult.OK)
        {
            return;
        }

        RunPanelAction(
            () => RequirePanelCommands()
                .DeleteFolderContents(id));
    }

    internal void AddGroupToCurrent()
    {
        var row = Rows.LastOrDefault(
            candidate =>
                candidate.IsSelected
                && candidate.FolderId is not null);
        if (row?.FolderId is not { } id)
            return;

        RunPanelAction(
            () => RequirePanelCommands().AddGroupControl(id));
    }

    internal void FitGroupsCurrent()
    {
        var row = Rows.LastOrDefault(
            candidate =>
                candidate.IsSelected
                && candidate.FolderId is not null);
        if (row?.FolderId is not { } id)
            return;

        RunPanelAction(
            () => RequirePanelCommands().FitGroupRanges(id));
    }

    internal void SetAllCollapsed(bool collapsed) =>
        RunPanelAction(
            () => RequirePanelCommands()
                .SetAllCollapsed(collapsed));

    internal void SelectItems(
        FolderPanelRowViewModel row)
    {
        RunPanelAction(() =>
        {
            var commands = RequirePanelCommands();

            if (row.FolderId is { } id)
                commands.SelectItems(id);
            else
                commands.SelectItemsInLayers(
                    row.First,
                    row.Last);
        });
    }

    internal IReadOnlyList<FolderPanelRowViewModel> GetDragRows(
        FolderPanelRowViewModel pressed)
    {
        var selected = pressed.IsSelected
            ? Rows.Where(row => row.IsSelected).ToArray()
            : [pressed];

        var topLevel = PanelMoveRules.TopLevelSelectedRows(
            selected
                .Select(row => row.PureRow)
                .ToArray());

        var keys = topLevel
            .Select(row => row.Key)
            .ToHashSet(StringComparer.Ordinal);

        return Rows
            .Where(row => keys.Contains(row.Key))
            .OrderBy(row => row.First)
            .ToArray();
    }

    internal bool CanDrop(
        IReadOnlyList<FolderPanelRowViewModel> rows,
        FolderPanelRowViewModel target,
        PanelDropZone zone)
    {
        if (panelTimeline is null)
            return false;

        var block = PanelMoveRules.TryGetDragBlock(
            rows.Select(row => row.PureRow).ToArray());
        if (block is null)
            return false;

        PanelDropTarget drop;
        try
        {
            drop = PanelMoveRules.ResolveDrop(
                target.PureRow,
                zone);
        }
        catch
        {
            return false;
        }

        try
        {
            return RequirePanelCommands().CanMovePanelRows(
                block,
                drop);
        }
        catch
        {
            return false;
        }
    }

    internal void Drop(
        IReadOnlyList<FolderPanelRowViewModel> rows,
        FolderPanelRowViewModel target,
        PanelDropZone zone)
    {
        var block = PanelMoveRules.TryGetDragBlock(
            rows.Select(row => row.PureRow).ToArray())
            ?? throw new InvalidOperationException(
                "同じ階層で連続した行だけをまとめて移動できます。");

        var drop = PanelMoveRules.ResolveDrop(
            target.PureRow,
            zone);

        RunPanelAction(
            () => RequirePanelCommands().MovePanelRows(
                block,
                drop));
    }

    private void RunPanelAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "panel_command_error=" + ex);
            SetPanelStatus(
                "操作に失敗しました: "
                + ex.GetBaseException().Message);
        }
    }

    private void SetPanelStatus(string value)
    {
        if (string.Equals(
                panelStatusText,
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        panelStatusText = value;
        RaisePanelPropertyChanged(nameof(StatusText));
    }

    private void RaisePanelPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class FolderPanelRowViewModel :
    INotifyPropertyChanged
{
    private PanelRow pureRow = new(
        Key: "",
        Kind: PanelRowKind.Layer,
        First: 0,
        Last: 0,
        Depth: 0,
        ParentFolderId: null,
        FolderId: null,
        Name: "",
        IsCollapsed: false,
        IsHidden: false,
        ItemCount: 0,
        GroupIssueCount: 0);
    private bool isSelected;
    private bool isEditing;
    private string editName = "";
    private string displayName = "";
    private string rangeText = "";
    private string disclosureText = "";
    private string visibilityText = "";
    private string countText = "";
    private string currentItem = "";
    private string summary = "";
    private string warningText = "";
    private string groupLaneText = "";
    private Brush? colorBrush;
    private bool ownVisible = true;
    private Thickness indent;
    private PanelDropZone? dropZone;

    internal FolderPanelRowViewModel(string key) =>
        Key = key;

    public string Key { get; }

    public PanelRow PureRow
    {
        get => pureRow;
        internal set
        {
            pureRow = value;
            RaiseAll();
        }
    }

    public PanelRowKind Kind => PureRow.Kind;
    public Guid? FolderId => PureRow.FolderId;
    public Guid? ParentFolderId => PureRow.ParentFolderId;
    public int First => PureRow.First;
    public int Last => PureRow.Last;
    public bool IsHidden => PureRow.IsHidden;
    public bool IsCollapsed => PureRow.IsCollapsed;

    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }

    public bool IsEditing
    {
        get => isEditing;
        internal set => Set(ref isEditing, value);
    }

    public string EditName
    {
        get => editName;
        set => Set(ref editName, value);
    }

    public string DisplayName
    {
        get => displayName;
        internal set => Set(ref displayName, value);
    }

    public string RangeText
    {
        get => rangeText;
        internal set => Set(ref rangeText, value);
    }

    public string DisclosureText
    {
        get => disclosureText;
        internal set => Set(ref disclosureText, value);
    }

    public string VisibilityText
    {
        get => visibilityText;
        internal set => Set(ref visibilityText, value);
    }

    public string CountText
    {
        get => countText;
        internal set => Set(ref countText, value);
    }

    public string CurrentItem
    {
        get => currentItem;
        internal set => Set(ref currentItem, value);
    }

    public string Summary
    {
        get => summary;
        internal set => Set(ref summary, value);
    }

    public string WarningText
    {
        get => warningText;
        internal set
        {
            if (Set(ref warningText, value))
                Raise(nameof(WarningVisibility));
        }
    }

    public string GroupLaneText
    {
        get => groupLaneText;
        internal set => Set(ref groupLaneText, value);
    }

    public Brush? ColorBrush
    {
        get => colorBrush;
        internal set => Set(ref colorBrush, value);
    }

    public bool OwnVisible
    {
        get => ownVisible;
        internal set => Set(ref ownVisible, value);
    }

    public Thickness Indent
    {
        get => indent;
        internal set => Set(ref indent, value);
    }

    public PanelDropZone? DropZone
    {
        get => dropZone;
        internal set
        {
            if (Set(ref dropZone, value))
                Raise(nameof(DropMarker));
        }
    }

    public string DropMarker => DropZone switch
    {
        PanelDropZone.Before => "↑",
        PanelDropZone.Into => "→",
        PanelDropZone.After => "↓",
        _ => ""
    };

    public Visibility FolderVisibility =>
        Kind == PanelRowKind.Folder
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility NameVisibility =>
        !IsEditing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility EditVisibility =>
        IsEditing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility WarningVisibility =>
        string.IsNullOrEmpty(WarningText)
            ? Visibility.Collapsed
            : Visibility.Visible;

    internal void RefreshDerived()
    {
        Raise(nameof(FolderVisibility));
        Raise(nameof(NameVisibility));
        Raise(nameof(EditVisibility));
        Raise(nameof(WarningVisibility));
        Raise(nameof(DropMarker));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(
                field,
                value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));

    private void RaiseAll()
    {
        Raise(nameof(PureRow));
        Raise(nameof(Kind));
        Raise(nameof(FolderId));
        Raise(nameof(ParentFolderId));
        Raise(nameof(First));
        Raise(nameof(Last));
        Raise(nameof(IsHidden));
        Raise(nameof(IsCollapsed));
        RefreshDerived();
    }
}

internal static class FolderPanelUi
{
    private const string DragFormat =
        "CNWL.NoHarmony.PanelRows";

    internal static void Build(
        FolderToolView view)
    {
        var root = new Grid
        {
            Margin = new Thickness(8)
        };
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(
                    1,
                    GridUnitType.Star)
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        var toolbar = BuildToolbar(view);
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        var list = BuildList(view);
        Grid.SetRow(list, 1);
        root.Children.Add(list);

        var status = new TextBlock
        {
            Margin = new Thickness(2, 6, 2, 0),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8
        };
        status.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(FolderToolViewModel.StatusText)));
        Grid.SetRow(status, 2);
        root.Children.Add(status);

        view.Content = root;
    }

    private static FrameworkElement BuildToolbar(
        FolderToolView view)
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 6)
        };

        Button AddButton(
            string text,
            Action<FolderToolViewModel> action)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 4)
            };
            button.Click += (_, _) =>
            {
                if (view.DataContext
                    is FolderToolViewModel vm)
                {
                    action(vm);
                }
            };
            return button;
        }

        panel.Children.Add(
            AddButton("＋フォルダ", vm =>
                vm.CreateFolderFromSelection()));
        panel.Children.Add(
            AddButton("＋レイヤー", vm =>
                vm.AddLayerFromSelection()));
        panel.Children.Add(
            AddButton("解除", vm =>
                vm.UngroupSelection()));
        panel.Children.Add(
            AddButton("表示/非表示", vm =>
                vm.ToggleVisibilitySelection()));
        panel.Children.Add(
            AddButton("中身選択", vm =>
                vm.SelectItemsCurrent()));

        var colorButton = AddButton("色", _ => { });
        var colorMenu = new ContextMenu();

        foreach (var choice in FolderCommands.Palette)
        {
            var item = new MenuItem
            {
                Header = choice.Name
            };
            var captured = choice.Color;
            item.Click += (_, _) =>
            {
                if (view.DataContext
                    is FolderToolViewModel vm)
                {
                    vm.SetColorCurrent(captured);
                }
            };
            colorMenu.Items.Add(item);
        }

        colorButton.Click += (_, _) =>
        {
            colorMenu.PlacementTarget = colorButton;
            colorMenu.Placement =
                PlacementMode.Bottom;
            colorMenu.IsOpen = true;
        };
        panel.Children.Add(colorButton);

        panel.Children.Add(
            AddButton("色→レイヤー", vm =>
                vm.ApplyColorCurrent()));
        panel.Children.Add(
            AddButton("Group追加", vm =>
                vm.AddGroupToCurrent()));
        panel.Children.Add(
            AddButton("Group Fit", vm =>
                vm.FitGroupsCurrent()));
        panel.Children.Add(
            AddButton("内容削除", vm =>
                vm.DeleteCurrentFolderWithLayers()));
        panel.Children.Add(
            AddButton("全開", vm =>
                vm.SetAllCollapsed(false)));
        panel.Children.Add(
            AddButton("全畳み", vm =>
                vm.SetAllCollapsed(true)));

        var follow = new CheckBox
        {
            Content = "Follow Timeline",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 4)
        };
        follow.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(FolderToolViewModel.FollowTimeline))
            {
                Mode = BindingMode.TwoWay
            });
        panel.Children.Add(follow);

        var current = new CheckBox
        {
            Content = "現在アイテム",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        current.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(FolderToolViewModel.ShowCurrentItem))
            {
                Mode = BindingMode.TwoWay
            });
        panel.Children.Add(current);

        return panel;
    }

    private static ListBox BuildList(
        FolderToolView view)
    {
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Extended,
            AllowDrop = true,
            HorizontalContentAlignment =
                HorizontalAlignment.Stretch
        };

        VirtualizingPanel.SetIsVirtualizing(
            list,
            true);
        VirtualizingPanel.SetVirtualizationMode(
            list,
            VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(
            list,
            true);

        list.SetBinding(
            ItemsControl.ItemsSourceProperty,
            new Binding(nameof(FolderToolViewModel.Rows)));

        var containerStyle =
            new System.Windows.Style(typeof(ListBoxItem));
        containerStyle.Setters.Add(
            new Setter(
                ListBoxItem.IsSelectedProperty,
                new Binding(nameof(
                    FolderPanelRowViewModel.IsSelected))
                {
                    Mode = BindingMode.TwoWay
                }));
        containerStyle.Setters.Add(
            new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));
        list.ItemContainerStyle = containerStyle;

        var factory =
            new FrameworkElementFactory(
                typeof(FolderPanelRowControl));
        list.ItemTemplate =
            new DataTemplate(
                typeof(FolderPanelRowViewModel))
            {
                VisualTree = factory
            };

        HookListInteractions(
            view,
            list);

        return list;
    }

    private static void HookListInteractions(
        FolderToolView view,
        ListBox list)
    {
        Point? pressPoint = null;
        FolderPanelRowViewModel? pressedRow = null;
        bool selectOnlyOnRelease = false;
        FolderPanelRowViewModel? dropTarget = null;

        FolderToolViewModel? Vm() =>
            view.DataContext as FolderToolViewModel;

        FolderPanelRowViewModel? RowFrom(
            object? source)
        {
            if (source is not DependencyObject dependency)
                return null;

            var container = ItemsControl.ContainerFromElement(
                list,
                dependency) as ListBoxItem;
            return container?.DataContext
                as FolderPanelRowViewModel;
        }

        void ClearDrop()
        {
            if (dropTarget is not null)
                dropTarget.DropZone = null;
            dropTarget = null;
        }

        list.SelectionChanged += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.None
                && e.AddedItems.Count == 1
                && e.AddedItems[0]
                    is FolderPanelRowViewModel row)
            {
                Vm()?.ActivateRow(row);
            }
        };

        list.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is TextBox
                || Vm() is not { } vm)
            {
                return;
            }

            var current =
                list.SelectedItem
                    as FolderPanelRowViewModel;

            if (e.Key == Key.Delete)
            {
                vm.DeleteSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2
                && current?.FolderId is not null)
            {
                vm.BeginRename(current);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.G
                && Keyboard.Modifiers
                    == ModifierKeys.Control)
            {
                vm.CreateFolderFromSelection();
                e.Handled = true;
                return;
            }

            if (current?.FolderId is not null
                && (e.Key == Key.Enter
                    || (e.Key == Key.Left
                        && !current.IsCollapsed)
                    || (e.Key == Key.Right
                        && current.IsCollapsed)))
            {
                vm.ToggleCollapsed(current);
                e.Handled = true;
            }
        };

        list.MouseDoubleClick += (_, e) =>
        {
            if (FindAncestor<ButtonBase>(
                    e.OriginalSource as DependencyObject)
                is not null
                || FindAncestor<TextBox>(
                    e.OriginalSource as DependencyObject)
                is not null
                || Vm() is not { } vm
                || RowFrom(e.OriginalSource)
                    is not { } row)
            {
                return;
            }

            if (row.FolderId is not null)
                vm.ToggleCollapsed(row);
            else
                vm.SelectItems(row);

            e.Handled = true;
        };

        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressPoint = null;
            pressedRow = null;
            selectOnlyOnRelease = false;

            var source = e.OriginalSource
                as DependencyObject;

            if (FindAncestor<ButtonBase>(source)
                    is not null
                || FindAncestor<TextBox>(source)
                    is not null
                || FindAncestor<ScrollBar>(source)
                    is not null
                || RowFrom(source)
                    is not { } row)
            {
                return;
            }

            pressPoint = e.GetPosition(list);
            pressedRow = row;

            if (row.IsSelected
                && Keyboard.Modifiers
                    == ModifierKeys.None
                && list.SelectedItems.Count > 1)
            {
                selectOnlyOnRelease = true;
                e.Handled = true;
            }
        };

        list.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (selectOnlyOnRelease
                && pressedRow is not null
                && Vm() is { } vm)
            {
                foreach (var row in vm.Rows)
                    row.IsSelected =
                        ReferenceEquals(
                            row,
                            pressedRow);
                vm.ActivateRow(pressedRow);
            }

            pressPoint = null;
            pressedRow = null;
            selectOnlyOnRelease = false;
        };

        list.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton
                    != MouseButtonState.Pressed
                || pressPoint is not { } start
                || pressedRow is null
                || Vm() is not { } vm)
            {
                return;
            }

            var delta =
                e.GetPosition(list) - start;

            if (Math.Abs(delta.X)
                    < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y)
                    < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var rows = vm.GetDragRows(
                pressedRow);
            pressPoint = null;
            selectOnlyOnRelease = false;

            try
            {
                DragDrop.DoDragDrop(
                    list,
                    new DataObject(
                        DragFormat,
                        rows.ToList()),
                    DragDropEffects.Move);
            }
            finally
            {
                ClearDrop();
                pressedRow = null;
            }
        };

        list.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            AutoScroll(list, e);

            if (Vm() is not { } vm
                || e.Data.GetData(DragFormat)
                    is not List<FolderPanelRowViewModel> rows)
            {
                ClearDrop();
                return;
            }

            var target = RowFrom(
                e.OriginalSource);
            if (target is null)
            {
                target = vm.Rows.LastOrDefault();
                if (target is null)
                    return;
            }

            var container =
                list.ItemContainerGenerator
                    .ContainerFromItem(target)
                    as ListBoxItem;
            var y = container is null
                ? 1.0
                : e.GetPosition(container).Y
                    / Math.Max(
                        1,
                        container.ActualHeight);

            var zone = target.FolderId is not null
                ? y < 0.25
                    ? PanelDropZone.Before
                    : y > 0.75
                        ? PanelDropZone.After
                        : PanelDropZone.Into
                : y < 0.5
                    ? PanelDropZone.Before
                    : PanelDropZone.After;

            var allowed = vm.CanDrop(
                rows,
                target,
                zone);

            if (!ReferenceEquals(
                    dropTarget,
                    target))
            {
                ClearDrop();
            }

            dropTarget = target;
            target.DropZone =
                allowed ? zone : null;
            e.Effects = allowed
                ? DragDropEffects.Move
                : DragDropEffects.None;
        };

        list.DragLeave += (_, e) =>
        {
            var point = e.GetPosition(list);
            if (point.X < 0
                || point.Y < 0
                || point.X > list.ActualWidth
                || point.Y > list.ActualHeight)
            {
                ClearDrop();
            }
        };

        list.Drop += (_, e) =>
        {
            e.Handled = true;

            if (Vm() is { } vm
                && dropTarget is
                    { DropZone: { } zone } target
                && e.Data.GetData(DragFormat)
                    is List<FolderPanelRowViewModel> rows)
            {
                ClearDrop();
                vm.Drop(
                    rows,
                    target,
                    zone);
            }

            ClearDrop();
        };
    }

    private static T? FindAncestor<T>(
        DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null
            && current is not T)
        {
            current =
                current is Visual
                    or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private static void AutoScroll(
        ListBox list,
        DragEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(list)
            is not { } viewer)
        {
            return;
        }

        var y = e.GetPosition(list).Y;
        const double margin = 24;

        if (y < margin)
            viewer.LineUp();
        else if (y > list.ActualHeight - margin)
            viewer.LineDown();
    }

    private static T? FindDescendant<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0;
            i < VisualTreeHelper.GetChildrenCount(root);
            i++)
        {
            var child =
                VisualTreeHelper.GetChild(root, i);
            if (child is T found)
                return found;
            if (FindDescendant<T>(child)
                is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

public sealed class FolderPanelRowControl : UserControl
{
    private readonly TextBox renameBox;

    public FolderPanelRowControl()
    {
        var row = new Grid
        {
            MinHeight = 24,
            Margin = new Thickness(1)
        };
        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });
        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });
        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });
        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(
                    1,
                    GridUnitType.Star)
            });
        row.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var indentHost = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        indentHost.SetBinding(
            FrameworkElement.MarginProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.Indent)));

        var drop = new TextBlock
        {
            Width = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        drop.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.DropMarker)));
        indentHost.Children.Add(drop);

        var lane = new TextBlock
        {
            MinWidth = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        lane.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.GroupLaneText)));
        indentHost.Children.Add(lane);

        Grid.SetColumn(indentHost, 0);
        row.Children.Add(indentHost);

        var disclosure = new Button
        {
            Width = 24,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0)
        };
        disclosure.SetBinding(
            ContentControl.ContentProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.DisclosureText)));
        disclosure.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.FolderVisibility)));
        disclosure.Click += (_, _) =>
        {
            if (DataContext
                    is FolderPanelRowViewModel model
                && FindPanelViewModel()
                    is { } vm)
            {
                vm.ToggleCollapsed(model);
            }
        };
        Grid.SetColumn(disclosure, 1);
        row.Children.Add(disclosure);

        var eye = new Button
        {
            Width = 28,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0)
        };
        eye.SetBinding(
            ContentControl.ContentProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.VisibilityText)));
        eye.Click += (_, _) =>
        {
            if (DataContext
                    is FolderPanelRowViewModel model
                && FindPanelViewModel()
                    is { } vm)
            {
                foreach (var candidate in vm.Rows)
                    candidate.IsSelected =
                        ReferenceEquals(
                            candidate,
                            model);
                vm.ToggleVisibilitySelection();
            }
        };
        Grid.SetColumn(eye, 2);
        row.Children.Add(eye);

        var center = new Grid();
        center.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });
        center.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        var nameHost = new Grid();

        var color = new Border
        {
            Width = 8,
            Margin = new Thickness(0, 2, 4, 2),
            CornerRadius = new CornerRadius(2)
        };
        color.SetBinding(
            Border.BackgroundProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.ColorBrush)));

        var nameLine = new DockPanel();
        DockPanel.SetDock(color, Dock.Left);
        nameLine.Children.Add(color);

        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        name.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.DisplayName)));
        name.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.NameVisibility)));
        name.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2
                && DataContext
                    is FolderPanelRowViewModel model
                && model.FolderId is not null
                && FindPanelViewModel()
                    is { } vm)
            {
                vm.BeginRename(model);
                e.Handled = true;
            }
        };
        nameLine.Children.Add(name);

        renameBox = new TextBox
        {
            MinWidth = 120,
            Margin = new Thickness(0)
        };
        renameBox.SetBinding(
            TextBox.TextProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.EditName))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger =
                    UpdateSourceTrigger.PropertyChanged
            });
        renameBox.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.EditVisibility)));
        renameBox.IsVisibleChanged += RenameBox_IsVisibleChanged;
        renameBox.PreviewKeyDown += RenameBox_PreviewKeyDown;
        renameBox.LostKeyboardFocus += RenameBox_LostKeyboardFocus;
        nameLine.Children.Add(renameBox);

        nameHost.Children.Add(nameLine);
        Grid.SetRow(nameHost, 0);
        center.Children.Add(nameHost);

        var detail = new TextBlock
        {
            Opacity = 0.72,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var multi = new MultiBinding
        {
            StringFormat = "{0} {1} {2}"
        };
        multi.Bindings.Add(
            new Binding(nameof(
                FolderPanelRowViewModel.RangeText)));
        multi.Bindings.Add(
            new Binding(nameof(
                FolderPanelRowViewModel.CountText)));
        multi.Bindings.Add(
            new Binding(nameof(
                FolderPanelRowViewModel.CurrentItem)));
        detail.SetBinding(
            TextBlock.TextProperty,
            multi);
        Grid.SetRow(detail, 1);
        center.Children.Add(detail);

        Grid.SetColumn(center, 3);
        row.Children.Add(center);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var summary = new TextBlock
        {
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        summary.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.Summary)));
        right.Children.Add(summary);

        var warning = new TextBlock
        {
            Text = "⚠",
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        warning.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.WarningVisibility)));
        warning.SetBinding(
            FrameworkElement.ToolTipProperty,
            new Binding(nameof(
                FolderPanelRowViewModel.WarningText)));
        right.Children.Add(warning);

        Grid.SetColumn(right, 4);
        row.Children.Add(right);

        Content = row;
    }

    private FolderToolViewModel? FindPanelViewModel()
    {
        DependencyObject? current = this;

        while (current is not null)
        {
            if (current is ListBox
                {
                    DataContext:
                        FolderToolViewModel vm
                })
            {
                return vm;
            }

            current =
                current is Visual
                    or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private void RenameBox_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!renameBox.IsVisible)
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                renameBox.Focus();
                renameBox.SelectAll();
            }),
            DispatcherPriority.Input);
    }

    private void RenameBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (DataContext
                is not FolderPanelRowViewModel row
            || FindPanelViewModel()
                is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.CommitRename(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRename(row);
            e.Handled = true;
        }
    }

    private void RenameBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (DataContext
                is FolderPanelRowViewModel row
            && row.IsEditing
            && FindPanelViewModel()
                is { } vm)
        {
            vm.CommitRename(row);
        }
    }
}
