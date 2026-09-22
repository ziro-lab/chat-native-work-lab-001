using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyStructuralObserver;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class StructuralObserverEntry : ILocalizePlugin
{
    public string Name => "CNWL structural observer native";
    public void SetCulture(CultureInfo cultureInfo) => StructuralObserverProbe.Schedule();
}

internal static class StructuralObserverProbe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static readonly List<string> facts = [];

    private sealed record HostSnapshot(
        Dictionary<IItem, int> Layers,
        object SettingsIdentity,
        HashSet<int> EmptySettingLayers,
        string Text);

    private sealed record RouteResult(
        string Command,
        string Target,
        string Parameter,
        bool Executed);

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_STRUCTURAL_OBSERVER_DIR");
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

    private static HostSnapshot Capture(Timeline timeline)
    {
        var layers = new Dictionary<IItem, int>(ReferenceEqualityComparer.Instance);

        foreach (var item in timeline.Items)
        {
            if (item.Remark?.StartsWith("CNWL_OBS_", StringComparison.Ordinal) == true)
                layers[item] = item.Layer;
        }

        var emptySettings = timeline.LayerSettings.Items
            .Where(x => x.IsEmpty())
            .Select(x => x.Layer)
            .ToHashSet();

        var text = string.Join(
            "|",
            layers
                .OrderBy(x => x.Key.Remark, StringComparer.Ordinal)
                .Select(x => $"{x.Key.Remark}@L{x.Value}:F{x.Key.Frame}"));

        return new HostSnapshot(
            layers,
            timeline.LayerSettings.Items,
            emptySettings,
            text);
    }

    private static StructuralDetection Detect(HostSnapshot before, HostSnapshot after)
    {
        var pairs = new List<LayerPair>();
        var survivingOldLayers = new HashSet<int>();

        foreach (var (item, newLayer) in after.Layers)
        {
            if (!before.Layers.TryGetValue(item, out var oldLayer))
                continue;

            pairs.Add(new LayerPair(oldLayer, newLayer));
            survivingOldLayers.Add(oldLayer);
        }

        var vanished = before.Layers.Values
            .Where(x => !survivingOldLayers.Contains(x))
            .ToHashSet();

        var insertedHints = after.EmptySettingLayers
            .Where(x => !before.EmptySettingLayers.Contains(x))
            .ToHashSet();

        return StructuralDeltaDetector.Detect(
            pairs,
            vanishedLayers: vanished,
            insertedHints: insertedHints);
    }

    private static string Describe(StructuralDetection detection) =>
        detection.Status + ":" + string.Join(
            "|",
            detection.Edits.Select(
                edit => edit switch
                {
                    InsertLayers x => $"I:{x.Position}:{x.Count}",
                    DeleteLayers x => $"D:{x.Position}:{x.Count}",
                    SwapAdjacentLayers x => $"S:{x.FirstLayer}",
                    _ => edit.GetType().Name
                }));

    private static string EmptySettings(HostSnapshot snapshot) =>
        string.Join(",", snapshot.EmptySettingLayers.OrderBy(x => x));

    private static async Task WaitUntil(
        string name,
        Func<bool> predicate,
        int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException(name);
    }

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

                if (!can)
                    continue;

                command.Execute(parameter);
                return new RouteResult(
                    commandType.ToString(),
                    "<direct>",
                    DescribeParameter(parameter),
                    true);
            }
        }

        throw new InvalidOperationException(
            $"No executable route for {commandType}; label_element={labelElement?.GetType().FullName ?? "<none>"}");
    }

    private static async Task ExerciseExact(
        string name,
        CommandType commandType,
        string expectedForward,
        string expectedReverse,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        UndoRedoManager manager,
        int layer,
        Func<int> recordedCount,
        Func<int> undoCount,
        Func<int> redoCount,
        string baselineText)
    {
        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        timeline.LayerSelection.SelectedLayers = ImmutableList.Create(layer);
        await Task.Delay(100);

        var before = Capture(timeline);
        Check(name + "_starts_at_baseline", before.Text == baselineText);

        var recordedBefore = recordedCount();
        var route = ExecuteStandardLayerCommand(
            commandType,
            window,
            timelineView,
            vm,
            timeline,
            layer);

        await WaitUntil(
            name + " command stabilization",
            () => recordedCount() > recordedBefore && Capture(timeline).Text != before.Text);

        var after = Capture(timeline);
        var forward = Detect(before, after);

        Fact(name + "_after", after.Text);
        Fact(name + "_forward", Describe(forward));
        Fact(name + "_settings_before", EmptySettings(before));
        Fact(name + "_settings_after", EmptySettings(after));
        Fact(
            name + "_settings_identity_changed",
            !ReferenceEquals(before.SettingsIdentity, after.SettingsIdentity));

        Check(name + "_route_executed", route.Executed);
        Check(name + "_recorded_trigger", recordedCount() > recordedBefore);
        Check(name + "_forward_exact", forward.Status == StructuralDetectionStatus.Exact);
        Check(name + "_forward_edit", Describe(forward) == "Exact:" + expectedForward);

        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);

        var undoBefore = undoCount();
        await Native.Key(0x5A, true);
        await WaitUntil(
            name + " undo stabilization",
            () => undoCount() > undoBefore && Capture(timeline).Text == baselineText);

        var undone = Capture(timeline);
        var reverse = Detect(after, undone);
        Fact(name + "_undo_detection", Describe(reverse));
        Check(name + "_undo_trigger", undoCount() > undoBefore);
        Check(name + "_undo_baseline", undone.Text == baselineText);
        Check(name + "_undo_exact", reverse.Status == StructuralDetectionStatus.Exact);
        Check(name + "_undo_edit", Describe(reverse) == "Exact:" + expectedReverse);

        var redoBefore = redoCount();
        await Native.Key(0x59, true);
        await WaitUntil(
            name + " redo stabilization",
            () => redoCount() > redoBefore && Capture(timeline).Text == after.Text);

        var redone = Capture(timeline);
        var redoDetection = Detect(undone, redone);
        Fact(name + "_redo_detection", Describe(redoDetection));
        Check(name + "_redo_trigger", redoCount() > redoBefore);
        Check(name + "_redo_exact_state", redone.Text == after.Text);
        Check(name + "_redo_detection_exact", redoDetection.Status == StructuralDetectionStatus.Exact);
        Check(name + "_redo_edit", Describe(redoDetection) == "Exact:" + expectedForward);

        undoBefore = undoCount();
        await Native.Key(0x5A, true);
        await WaitUntil(
            name + " reset stabilization",
            () => undoCount() > undoBefore && Capture(timeline).Text == baselineText);

        var reset = Capture(timeline);
        var resetDetection = Detect(redone, reset);
        Fact(name + "_reset_detection", Describe(resetDetection));
        Check(name + "_reset_trigger", undoCount() > undoBefore);
        Check(name + "_reset_baseline", reset.Text == baselineText);
        Check(name + "_reset_detection_exact", resetDetection.Status == StructuralDetectionStatus.Exact);
        Check(name + "_reset_edit", Describe(resetDetection) == "Exact:" + expectedReverse);
    }

    private static async Task ObserveSparse(
        string name,
        CommandType commandType,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        int layer,
        Func<int> recordedCount,
        Func<int> undoCount,
        string baselineText)
    {
        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        timeline.LayerSelection.SelectedLayers = ImmutableList.Create(layer);
        await Task.Delay(100);

        var before = Capture(timeline);
        Check(name + "_starts_at_baseline", before.Text == baselineText);

        var recordedBefore = recordedCount();
        ExecuteStandardLayerCommand(
            commandType,
            window,
            timelineView,
            vm,
            timeline,
            layer);

        await WaitUntil(
            name + " sparse command stabilization",
            () => recordedCount() > recordedBefore && Capture(timeline).Text != before.Text);

        var after = Capture(timeline);
        var detection = Detect(before, after);

        Fact(name + "_detection", Describe(detection));
        Fact(name + "_reason", detection.Reason);
        Fact(name + "_settings_before", EmptySettings(before));
        Fact(name + "_settings_after", EmptySettings(after));
        Fact(
            name + "_settings_identity_changed",
            !ReferenceEquals(before.SettingsIdentity, after.SettingsIdentity));

        Check(name + "_recorded_trigger", recordedCount() > recordedBefore);

        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);

        var undoBefore = undoCount();
        await Native.Key(0x5A, true);
        await WaitUntil(
            name + " sparse undo stabilization",
            () => undoCount() > undoBefore && Capture(timeline).Text == baselineText);

        Check(name + "_undo_baseline", Capture(timeline).Text == baselineText);
    }

    private static async Task Run(Window window)
    {
        EventHandler? recordedHandler = null;
        EventHandler? undoHandler = null;
        EventHandler? redoHandler = null;
        UndoRedoManager? manager = null;

        try
        {
            window.WindowState = WindowState.Normal;
            window.Left = 0;
            window.Top = 0;
            window.Width = 1000;
            window.Height = 700;
            window.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);
            await Task.Delay(1000);

            var timelineView = Host.Elements(window)
                .Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();

            var vm = timelineView.DataContext as TimelineViewModel
                ?? throw new InvalidOperationException("Visible TimelineViewModel missing");

            var timeline = Host.Get(vm, "Timeline") as Timeline
                ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline
                ?? throw new InvalidOperationException("Visible Timeline missing");

            Check("visible_context_bound", ReferenceEquals(timelineView.DataContext, vm));

            var character = new Character { Name = "CNWL_STRUCTURAL_OBSERVER" };

            for (var layer = 0; layer <= 8; layer++)
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20 + layer * 40,
                    Layer = layer,
                    Length = 20,
                    Serif = "D" + layer,
                    Remark = "CNWL_OBS_DENSE_L" + layer
                };

                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Dense fixture add L" + layer);
            }

            foreach (var layer in new[] { 20, 30 })
            {
                var item = new VoiceItem(character)
                {
                    Frame = 500 + layer * 10,
                    Layer = layer,
                    Length = 20,
                    Serif = "S" + layer,
                    Remark = "CNWL_OBS_SPARSE_L" + layer
                };

                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Sparse fixture add L" + layer);
            }

            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            timeline.LayerSelection.SelectedLayers = ImmutableList<int>.Empty;
            await Task.Delay(600);

            var main = window.DataContext
                ?? throw new InvalidOperationException("MainViewModel missing");
            var model = main.GetType().GetField("model", Host.Flags)?.GetValue(main)
                ?? throw new MissingMemberException("MainViewModel.model");
            manager = Host.Get(model, "UndoRedoManager") as UndoRedoManager
                ?? throw new MissingMemberException("MainModel.UndoRedoManager");

            manager.Record();

            var recorded = 0;
            var undone = 0;
            var redone = 0;

            recordedHandler = (_, _) =>
            {
                recorded++;
                Log("manager_recorded=" + recorded);
            };
            undoHandler = (_, _) =>
            {
                undone++;
                Log("manager_undoed=" + undone);
            };
            redoHandler = (_, _) =>
            {
                redone++;
                Log("manager_redoed=" + redone);
            };

            manager.Recorded += recordedHandler;
            manager.Undoed += undoHandler;
            manager.Redoed += redoHandler;

            var baseline = Capture(timeline);
            Fact("baseline", baseline.Text);
            Fact("baseline_empty_settings", EmptySettings(baseline));
            Check("fixture_count", baseline.Layers.Count == 11);

            await ExerciseExact(
                "dense_add_L3",
                CommandType.AddLayer,
                "I:3:1",
                "D:3:1",
                window,
                timelineView,
                vm,
                timeline,
                manager,
                3,
                () => recorded,
                () => undone,
                () => redone,
                baseline.Text);

            await ExerciseExact(
                "dense_delete_L3",
                CommandType.DeleteLayer,
                "D:3:1",
                "I:3:1",
                window,
                timelineView,
                vm,
                timeline,
                manager,
                3,
                () => recorded,
                () => undone,
                () => redone,
                baseline.Text);

            await ExerciseExact(
                "dense_move_L3_down",
                CommandType.MoveDownLayer,
                "S:3",
                "S:3",
                window,
                timelineView,
                vm,
                timeline,
                manager,
                3,
                () => recorded,
                () => undone,
                () => redone,
                baseline.Text);

            await ObserveSparse(
                "sparse_add_L24",
                CommandType.AddLayer,
                window,
                timelineView,
                vm,
                timeline,
                24,
                () => recorded,
                () => undone,
                baseline.Text);

            await ObserveSparse(
                "sparse_delete_L24",
                CommandType.DeleteLayer,
                window,
                timelineView,
                vm,
                timeline,
                24,
                () => recorded,
                () => undone,
                baseline.Text);

            Check("final_baseline", Capture(timeline).Text == baseline.Text);
            Fact("manager_recorded_total", recorded);
            Fact("manager_undoed_total", undone);
            Fact("manager_redoed_total", redone);
            Fact("host_version", typeof(Timeline).Assembly.GetName().Version);

            Check(
                "no_harmony_loaded",
                !AppDomain.CurrentDomain.GetAssemblies().Any(
                    x => string.Equals(x.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.GetName().Name, "HarmonyLib", StringComparison.OrdinalIgnoreCase)));

            Check(
                "no_harmony_reference",
                !typeof(StructuralObserverProbe).Assembly.GetReferencedAssemblies().Any(
                    x => string.Equals(x.Name, "0Harmony", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.Name, "HarmonyLib", StringComparison.OrdinalIgnoreCase)));

            Log("native structural observer complete");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                if (manager is not null)
                {
                    if (recordedHandler is not null)
                        manager.Recorded -= recordedHandler;
                    if (undoHandler is not null)
                        manager.Undoed -= undoHandler;
                    if (redoHandler is not null)
                        manager.Redoed -= redoHandler;
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
                        ? "FAIL_STRUCTURAL_OBSERVER_NATIVE"
                        : "PASS_STRUCTURAL_OBSERVER_NATIVE"),
                "assertion_count=" + checks.Count
            }.Concat(checks).Concat(facts));
        File.Move(temp, Path.Combine(output, "result.txt"), true);
    }
}
