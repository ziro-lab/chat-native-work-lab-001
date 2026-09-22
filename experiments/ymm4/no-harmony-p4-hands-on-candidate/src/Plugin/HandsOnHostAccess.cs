using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using Ymm4NoHarmonyStructuralConvenience;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal static class HandsOnHostAccess
{
    internal static object? PublicProperty(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    internal static Window? MainWindow() =>
        Application.Current?.Windows.Cast<Window>()
            .FirstOrDefault(x => x.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");

    internal static object? ActiveTimelineViewModel(object root) =>
        PublicProperty(root, "ActiveTimelineViewModel");

    internal static Timeline? TimelineOf(object? active)
    {
        if (active is null) return null;
        return PublicProperty(active, "Timeline") as Timeline
            ?? active.GetType()
                .GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(active) as Timeline;
    }

    internal static INotifyPropertyChanged ProjectFilePathSignal(object root) =>
        PublicProperty(root, "ProjectFilePath") as INotifyPropertyChanged
        ?? throw new InvalidOperationException("ProjectFilePath does not expose public change notification.");

    internal static string? ProjectFilePath(object root)
    {
        var reactive = PublicProperty(root, "ProjectFilePath");
        return reactive?.GetType()
            .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(reactive) as string;
    }

    internal static bool IsEmptyProject(object root) =>
        PublicProperty(root, "IsEmptyProject") is true;

    internal static object? FindToolArea(object root)
    {
        if (PublicProperty(root, "AnchorableAreaViewModels") is not IEnumerable areas)
            return null;

        foreach (var area in areas.Cast<object>())
        {
            var viewModelType = area.GetType()
                .GetProperty("ViewModelType", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(area) as Type;
            if (viewModelType == typeof(FolderToolViewModel))
                return area;
        }
        return null;
    }

    internal static string? ReadToolAreaSavedState(object root)
    {
        var area = FindToolArea(root)
            ?? throw new InvalidOperationException("Folder ToolArea is not available.");
        var save = area.GetType().GetMethod(
            "SaveState",
            BindingFlags.Instance | BindingFlags.Public,
            Type.EmptyTypes)
            ?? throw new MissingMethodException(area.GetType().FullName, "SaveState()");
        var state = save.Invoke(area, null)
            ?? throw new InvalidOperationException("ToolArea SaveState returned null.");
        return state.GetType()
            .GetProperty("SavedState", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(state) as string;
    }

    internal static void WriteToolAreaSavedState(object root, string? savedState)
    {
        var area = FindToolArea(root)
            ?? throw new InvalidOperationException("Folder ToolArea is not available.");
        var load = area.GetType().GetMethod(
            "LoadState",
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(ToolState)])
            ?? throw new MissingMethodException(area.GetType().FullName, "LoadState(ToolState)");
        load.Invoke(area, [new ToolState
        {
            Title = FolderToolViewModel.DisplayTitle,
            SavedState = savedState
        }]);
    }

    internal static UndoRedoManager UndoManager(object root)
    {
        var model = root.GetType()
            .GetField("model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(root)
            ?? throw new MissingMemberException(root.GetType().FullName, "model");
        return model.GetType()
            .GetProperty("UndoRedoManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(model) as UndoRedoManager
            ?? throw new MissingMemberException(model.GetType().FullName, "UndoRedoManager");
    }

    internal static Host CreateHost(Window window, object active)
    {
        var vm = active as TimelineViewModel
            ?? throw new InvalidOperationException("Active TimelineViewModel has an unexpected type.");
        var timeline = TimelineOf(vm)
            ?? throw new InvalidOperationException("Active Timeline is missing.");

        var view = Host.Elements(window)
            .Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Active TimelineView is not realized.");

        var viewVm = view.DataContext as TimelineViewModel
            ?? throw new InvalidOperationException("Visible TimelineView has no TimelineViewModel.");
        var viewTimeline = TimelineOf(viewVm)
            ?? throw new InvalidOperationException("Visible TimelineView has no Timeline.");
        if (viewTimeline.ID != timeline.ID)
            throw new InvalidOperationException("Visible TimelineView does not match active Timeline yet.");

        vm = viewVm;

        var scroll = Host.Elements(view).OfType<ScrollViewer>()
            .Where(x => x.IsVisible && x.ActualHeight > 50)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Timeline ScrollViewer is missing.");

        return new Host(
            window,
            timeline,
            vm,
            view,
            scroll.Content as FrameworkElement
                ?? throw new InvalidOperationException("Timeline content is missing."),
            scroll);
    }

    internal static string? ItemsBindingPath(FrameworkElement element)
    {
        if (element.GetType().Name != "FastCanvasItemsControl")
            return null;
        var property = element.GetType()
            .GetField("ItemsProperty", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as DependencyProperty;
        return property is null
            ? null
            : BindingOperations.GetBinding(element, property)?.Path?.Path;
    }

    internal static FrameworkElement FindLabels(Window window)
    {
        return Host.Elements(window)
            .Where(x => x.IsVisible && ItemsBindingPath(x) == "LayerLabels")
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("LayerLabels FastCanvasItemsControl is not realized.");
    }

    internal static int? LayerId(object? dataContext)
    {
        if (dataContext is null
            || dataContext.GetType().Name != "TimelineLayerLabelItemViewModel")
            return null;
        return dataContext.GetType()
            .GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(dataContext) as int?;
    }

    internal static DependencyObject? Parent(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private static IEnumerable<IInputElement> CommandTargets(
        Host host,
        Window window)
    {
        if (Keyboard.FocusedElement is IInputElement focused)
            yield return focused;
        yield return host.Source;
        yield return host.View;
        yield return window;
    }

    internal static bool CanExecuteTimelineCommand(
        Host host,
        Window window,
        CommandType type,
        object? parameter)
    {
        ICommand command = CommandSettings.Default[type]
            ?? throw new InvalidOperationException("Command missing: " + type);

        foreach (var target in CommandTargets(host, window))
        {
            if (command is RoutedCommand routed)
            {
                try
                {
                    if (routed.CanExecute(parameter, target))
                        return true;
                }
                catch
                {
                }

                continue;
            }

            try
            {
                if (command.CanExecute(parameter))
                    return true;
            }
            catch
            {
            }
        }

        return false;
    }

    internal static bool TryExecuteTimelineCommand(
        Host host,
        Window window,
        CommandType type,
        object? parameter)
    {
        ICommand command = CommandSettings.Default[type]
            ?? throw new InvalidOperationException("Command missing: " + type);

        foreach (var target in CommandTargets(host, window))
        {
            if (command is RoutedCommand routed)
            {
                bool can;
                try
                {
                    can = routed.CanExecute(parameter, target);
                }
                catch
                {
                    continue;
                }

                if (!can)
                    continue;

                routed.Execute(parameter, target);
                HandsOnRuntime.Diagnostic(
                    $"host_command type={type} target={target.GetType().Name} parameter={parameter ?? "<null>"}");
                return true;
            }

            if (!command.CanExecute(parameter))
                continue;

            command.Execute(parameter);
            HandsOnRuntime.Diagnostic(
                $"host_command type={type} target=<direct> parameter={parameter ?? "<null>"}");
            return true;
        }

        return false;
    }

    internal static int ApplyGroupRanges(
        IReadOnlyList<GroupItem> groups,
        IReadOnlyList<int> ranges)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(ranges);

        if (groups.Count != ranges.Count)
            throw new ArgumentException(
                "Group item count does not match the range plan.",
                nameof(ranges));

        var changed = 0;

        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].GroupRange == ranges[i])
                continue;

            groups[i].GroupRange = ranges[i];
            changed++;
        }

        return changed;
    }

    internal static void ApplyStructuralPlan(
        Timeline timeline,
        StructuralConveniencePlan plan,
        IReadOnlyList<GroupItem> groups,
        GroupItem? newGroup = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(groups);

        if (groups.Count != plan.GroupRanges.Count)
            throw new ArgumentException(
                "Group item count does not match the structural plan.",
                nameof(groups));

        var beforeItems = timeline.Items.ToArray();
        var removed = beforeItems
            .Where(item => plan.MapLayer(item.Layer) < 0)
            .ToArray();

        if (removed.Length > 0)
            timeline.DeleteItems(removed);

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];

            if (!timeline.Items.Any(
                    item => ReferenceEquals(item, group)))
                continue;

            var nextRange = plan.GroupRanges[i];
            if (group.GroupRange != nextRange)
                group.GroupRange = nextRange;
        }

        foreach (var item in timeline.Items.ToArray())
        {
            var mapped = plan.MapLayer(item.Layer);
            if (mapped < 0)
            {
                throw new InvalidOperationException(
                    "A removed item survived the structural delete plan.");
            }

            if (item.Layer != mapped)
                item.Layer = mapped;
        }

        var settings = timeline.LayerSettings.Items
            .Select(setting => setting with
            {
                Layer = plan.MapLayer(setting.Layer)
            })
            .Where(setting => setting.Layer >= 0)
            .ToImmutableList();

        if (!settings.SequenceEqual(timeline.LayerSettings.Items))
            timeline.LayerSettings.Items = settings;

        if (newGroup is not null)
        {
            if (!timeline.TryAddItems(
                    [newGroup],
                    newGroup.Frame,
                    newGroup.Layer,
                    isItemSelectionEnabled: false))
            {
                throw new InvalidOperationException(
                    "YMM4 rejected the planned Group Control item.");
            }
        }

        timeline.LayerSelection.Clear();
        timeline.RefreshTimelineLengthAndMaxLayer();
    }

    internal static FrameworkElement FindLayerElement(FrameworkElement labels, int layer)
    {
        return Host.Elements(labels)
            .Where(x => x.IsVisible
                && LayerId(x.DataContext) == layer
                && x.ActualWidth > 20
                && x.ActualHeight > 8)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Layer label row is not realized: " + layer);
    }

    internal static FrameworkElement FindLayerContextOwner(FrameworkElement labels, int layer)
    {
        var direct = Host.Elements(labels)
            .Where(x => x.IsVisible
                && LayerId(x.DataContext) == layer
                && x.ContextMenu is not null)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();
        if (direct is not null)
            return direct;

        DependencyObject? current = FindLayerElement(labels, layer);
        while (current is not null)
        {
            if (current is FrameworkElement { ContextMenu: not null } fe)
                return fe;
            current = Parent(current);
        }

        throw new InvalidOperationException("ContextMenu owner is not realized for layer " + layer);
    }
}
