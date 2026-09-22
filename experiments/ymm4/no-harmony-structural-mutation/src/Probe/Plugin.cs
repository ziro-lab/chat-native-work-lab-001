using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class StructuralMutationEntry : ILocalizePlugin
{
    public string Name => "CNWL structural mutation";
    public void SetCulture(CultureInfo cultureInfo) => StructuralProbe.Schedule();
}

internal static class StructuralProbe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static readonly List<string> facts = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_STRUCTURAL_MUTATION_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path))
            return;

        scheduled = true;
        output = Path.GetFullPath(path);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static void Bootstrap()
    {
        var ticks = 0;
        var created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100)
                    throw new TimeoutException("MainViewModel bootstrap");

                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = Host.Get(main, "ActiveTimelineViewModel");
                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        return;
                    }

                    if (active is null)
                        continue;

                    timer.Stop();
                    _ = Run(window);
                    return;
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Fail(ex);
                Finish();
            }
        };

        timer.Start();
    }

    private static void Log(string text) =>
        File.AppendAllText(
            Path.Combine(output, "progress.txt"),
            DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);

    private static void Check(string name, bool value)
    {
        checks.Add(name + "=" + value);
        failed |= !value;
        Log("assert " + checks[^1]);
    }

    private static void Fact(string name, object? value)
    {
        var line = name + "=" + (value?.ToString() ?? "<null>");
        facts.Add(line);
        Log("fact " + line);
    }

    private static string Snapshot(Timeline timeline) =>
        string.Join(
            "|",
            timeline.Items
                .Where(x => x.Remark?.StartsWith("CNWL_STRUCT_", StringComparison.Ordinal) == true)
                .OrderBy(x => x.Remark, StringComparer.Ordinal)
                .Select(x => $"{x.Remark}@L{x.Layer}:F{x.Frame}"));

    private static string Selected(Timeline timeline) =>
        string.Join(",", timeline.LayerSelection.SelectedLayers.OrderBy(x => x));

    private static string Targets(IEnumerable<IItem> items) =>
        string.Join(
            ",",
            items
                .Where(x => x.Remark?.StartsWith("CNWL_STRUCT_", StringComparison.Ordinal) == true)
                .OrderBy(x => x.Remark, StringComparer.Ordinal)
                .Select(x => x.Remark));

    private sealed record RouteResult(
        string Command,
        string Target,
        string Parameter,
        bool Executed);

    private sealed record MutationResult(
        string After,
        string Undo,
        string Redo,
        string Reset,
        string SelectionAfter,
        int TimelineEvents,
        int SettingsEvents,
        RouteResult Route);

    private static string DescribeParameter(object? value) =>
        value switch
        {
            null => "<null>",
            int number => "Int32:" + number,
            _ => value.GetType().FullName ?? value.GetType().Name
        };

    private static FrameworkElement? FindElementForDataContext(
        FrameworkElement root,
        object dataContext) =>
        Host.Elements(root)
            .Where(x => x.IsVisible && ReferenceEquals(x.DataContext, dataContext))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private static RouteResult ExecuteStandardLayerCommand(
        CommandType commandType,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        int layer)
    {
        ICommand command = CommandSettings.Default[commandType]
            ?? throw new InvalidOperationException("Command missing: " + commandType);

        var labelVm = layer >= 0 && layer < vm.LayerLabels.Count
            ? vm.LayerLabels[layer]
            : null;

        var labelElement = labelVm is null
            ? null
            : FindElementForDataContext(timelineView, labelVm);

        if (labelElement is not null)
        {
            try { labelElement.Focus(); }
            catch { }
        }

        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);

        var targets = new List<(string Name, IInputElement Target)>();
        if (labelElement is not null)
            targets.Add(("LayerLabel", labelElement));
        if (Keyboard.FocusedElement is IInputElement focused)
            targets.Add(("Focused", focused));
        targets.Add(("TimelineView", timelineView));
        targets.Add(("Window", window));

        var parameters = new List<object?>
        {
            null,
            layer,
            labelVm,
            timeline
        };

        var uniqueTargets = targets
            .GroupBy(x => x.Target, ReferenceEqualityComparer.Instance)
            .Select(x => x.First())
            .ToArray();

        if (command is RoutedCommand routed)
        {
            foreach (var (targetName, target) in uniqueTargets)
            {
                foreach (var parameter in parameters)
                {
                    bool can;
                    try
                    {
                        can = routed.CanExecute(parameter, target);
                    }
                    catch (Exception ex)
                    {
                        Log($"route_can_exception command={commandType} target={targetName} param={DescribeParameter(parameter)} error={ex.GetBaseException().Message}");
                        continue;
                    }

                    Log($"route_can command={commandType} target={targetName} param={DescribeParameter(parameter)} can={can}");
                    if (!can)
                        continue;

                    routed.Execute(parameter, target);
                    var route = new RouteResult(
                        commandType.ToString(),
                        targetName,
                        DescribeParameter(parameter),
                        true);
                    Fact(commandType + "_route", $"{route.Target}|{route.Parameter}");
                    return route;
                }
            }
        }
        else
        {
            foreach (var parameter in parameters)
            {
                bool can;
                try { can = command.CanExecute(parameter); }
                catch { continue; }

                Log($"route_can command={commandType} target=<direct> param={DescribeParameter(parameter)} can={can}");
                if (!can)
                    continue;

                command.Execute(parameter);
                var route = new RouteResult(
                    commandType.ToString(),
                    "<direct>",
                    DescribeParameter(parameter),
                    true);
                Fact(commandType + "_route", $"{route.Target}|{route.Parameter}");
                return route;
            }
        }

        throw new InvalidOperationException(
            $"No executable route for {commandType}; label_element={labelElement?.GetType().FullName ?? "<none>"}");
    }

    private static async Task<MutationResult> Exercise(
        string name,
        CommandType commandType,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        string baseline,
        int layer,
        Func<int> timelineEventCount,
        Func<int> settingsEventCount)
    {
        var beforeTimelineEvents = timelineEventCount();
        var beforeSettingsEvents = settingsEventCount();

        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        timeline.LayerSelection.SelectedLayers = ImmutableList.Create(layer);
        await Task.Delay(150);

        Log("phase=" + name + "_command");
        var route = ExecuteStandardLayerCommand(
            commandType,
            window,
            timelineView,
            vm,
            timeline,
            layer);

        await Task.Delay(600);

        var after = Snapshot(timeline);
        var selectionAfter = Selected(timeline);
        Fact(name + "_after", after);
        Fact(name + "_selection_after", selectionAfter);
        Check(name + "_route_executed", route.Executed);
        Check(name + "_changed", after != baseline);

        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);

        await Native.Key(0x5A, true);
        var undo = Snapshot(timeline);
        Fact(name + "_undo", undo);
        Check(name + "_undo_baseline", undo == baseline);

        await Native.Key(0x59, true);
        var redo = Snapshot(timeline);
        Fact(name + "_redo", redo);
        Check(name + "_redo_exact", redo == after);

        await Native.Key(0x5A, true);
        var reset = Snapshot(timeline);
        Fact(name + "_reset", reset);
        Check(name + "_reset_baseline", reset == baseline);

        var timelineDelta = timelineEventCount() - beforeTimelineEvents;
        var settingsDelta = settingsEventCount() - beforeSettingsEvents;
        Fact(name + "_timeline_events", timelineDelta);
        Fact(name + "_settings_events", settingsDelta);
        Check(name + "_undo_event_surface", timelineDelta + settingsDelta > 0);

        if (reset != baseline)
            throw new InvalidOperationException(
                $"{name} failed to restore baseline; subsequent mutation evidence would be contaminated");

        return new(
            after,
            undo,
            redo,
            reset,
            selectionAfter,
            timelineDelta,
            settingsDelta,
            route);
    }

    private static async Task Run(Window window)
    {
        Timeline? subscribedTimeline = null;
        EventHandler<UndoRedoEventArgs>? timelineHandler = null;
        EventHandler<UndoRedoEventArgs>? settingsHandler = null;

        try
        {
            window.WindowState = WindowState.Normal;
            window.Left = 0;
            window.Top = 0;
            window.Width = 1000;
            window.Height = 700;
            window.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);
            await Task.Delay(1200);

            var timelineView = Host.Elements(window)
                .Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();

            var vm = timelineView.DataContext as TimelineViewModel
                ?? throw new InvalidOperationException("Visible TimelineViewModel missing");

            var timeline = Host.Get(vm, "Timeline") as Timeline
                ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline
                ?? throw new InvalidOperationException("Visible Timeline missing");

            subscribedTimeline = timeline;
            Check("visible_context_bound", ReferenceEquals(timelineView.DataContext, vm));

            var character = new Character { Name = "CNWL_STRUCTURAL" };
            for (var layer = 0; layer <= 8; layer++)
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20 + layer * 40,
                    Layer = layer,
                    Length = 20,
                    Serif = "L" + layer,
                    Remark = "CNWL_STRUCT_L" + layer
                };

                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add L" + layer);
            }

            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            timeline.LayerSelection.SelectedLayers = ImmutableList<int>.Empty;
            await Task.Delay(800);

            var main = window.DataContext
                ?? throw new InvalidOperationException("MainViewModel missing");
            var model = main.GetType().GetField("model", Host.Flags)?.GetValue(main)
                ?? throw new MissingMemberException("MainViewModel.model");
            var manager = Host.Get(model, "UndoRedoManager")
                ?? throw new MissingMemberException("MainModel.UndoRedoManager");
            var record = manager.GetType().GetMethod(
                "Record",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null)
                ?? throw new MissingMethodException("UndoRedoManager.Record()");
            record.Invoke(manager, null);

            var baseline = Snapshot(timeline);
            Fact("baseline", baseline);
            Fact("baseline_max_layer", timeline.MaxLayer);
            Fact("baseline_layer_settings_max", timeline.LayerSettings.MaxLayer);
            Check(
                "fixture_count",
                timeline.Items.Count(
                    x => x.Remark?.StartsWith("CNWL_STRUCT_", StringComparison.Ordinal) == true) == 9);

            Fact("add_targets_L3", Targets(timeline.GetAddLayerTargetItems(3)));
            Fact("remove_targets_L3", Targets(timeline.GetRemoveLayerTargetItems(3)));

            var timelineEvents = 0;
            var settingsEvents = 0;

            timelineHandler = (_, e) =>
            {
                timelineEvents++;
                Log("timeline_undo_event " + e);
            };
            settingsHandler = (_, e) =>
            {
                settingsEvents++;
                Log("settings_undo_event " + e);
            };

            timeline.UndoRedoCommandCreated += timelineHandler;
            timeline.LayerSettings.UndoRedoCommandCreated += settingsHandler;

            await Exercise(
                "add_L3",
                CommandType.AddLayer,
                window,
                timelineView,
                vm,
                timeline,
                baseline,
                3,
                () => timelineEvents,
                () => settingsEvents);

            await Exercise(
                "delete_L3",
                CommandType.DeleteLayer,
                window,
                timelineView,
                vm,
                timeline,
                baseline,
                3,
                () => timelineEvents,
                () => settingsEvents);

            await Exercise(
                "move_L3_down",
                CommandType.MoveDownLayer,
                window,
                timelineView,
                vm,
                timeline,
                baseline,
                3,
                () => timelineEvents,
                () => settingsEvents);

            Check("final_baseline", Snapshot(timeline) == baseline);
            Fact("final_selection", Selected(timeline));
            Fact("host_version", typeof(Timeline).Assembly.GetName().Version);
            Fact("timeline_event_total", timelineEvents);
            Fact("settings_event_total", settingsEvents);

            Check(
                "no_harmony_loaded",
                !AppDomain.CurrentDomain.GetAssemblies().Any(
                    x => string.Equals(
                        x.GetName().Name,
                        "0Harmony",
                        StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(
                            x.GetName().Name,
                            "HarmonyLib",
                            StringComparison.OrdinalIgnoreCase)));

            Check(
                "no_harmony_reference",
                !typeof(StructuralProbe).Assembly.GetReferencedAssemblies().Any(
                    x => string.Equals(x.Name, "0Harmony", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(x.Name, "HarmonyLib", StringComparison.OrdinalIgnoreCase)));

            Log("structural routed-command observation complete");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                if (subscribedTimeline is not null)
                {
                    if (timelineHandler is not null)
                        subscribedTimeline.UndoRedoCommandCreated -= timelineHandler;
                    if (settingsHandler is not null)
                        subscribedTimeline.LayerSettings.UndoRedoCommandCreated -= settingsHandler;
                }

                Native.Release();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            Finish();
        }
    }

    private static void Fail(Exception ex)
    {
        failed = true;
        File.AppendAllText(Path.Combine(output, "error.txt"), ex + Environment.NewLine);
        Log("FAIL " + ex.GetBaseException().Message);
    }

    private static void Finish()
    {
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllLines(
            temp,
            new[]
            {
                "status=" + (
                    failed
                        ? "FAIL_STRUCTURAL_MUTATION_SEMANTICS"
                        : "PASS_STRUCTURAL_MUTATION_SEMANTICS"),
                "assertion_count=" + checks.Count
            }.Concat(checks).Concat(facts));
        File.Move(temp, Path.Combine(output, "result.txt"), true);
    }
}
