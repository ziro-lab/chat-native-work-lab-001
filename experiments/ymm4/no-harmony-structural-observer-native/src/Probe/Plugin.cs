using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

public sealed class StructuralObserverNativeEntry : ILocalizePlugin
{
    public string Name => "CNWL structural observer native";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static readonly List<string> facts = [];

    private sealed record HostSnapshot(
        Dictionary<IItem, int> ItemLayers,
        ImmutableList<LayerSetting> Settings,
        ImmutableList<int> SelectedLayers);

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_STRUCTURAL_OBSERVER_NATIVE_DIR");
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
                    throw new TimeoutException("bootstrap");

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

    private static void Check(string name, bool ok)
    {
        checks.Add(name + "=" + ok);
        failed |= !ok;
        Log("assert " + checks[^1]);
    }

    private static void Fact(string name, object? value)
    {
        var line = name + "=" + (value?.ToString() ?? "<null>");
        facts.Add(line);
        Log("fact " + line);
    }

    private static HostSnapshot Snapshot(Timeline timeline)
    {
        var itemLayers = new Dictionary<IItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var item in timeline.Items)
            itemLayers[item] = item.Layer;

        return new HostSnapshot(
            itemLayers,
            timeline.LayerSettings.Items,
            timeline.LayerSelection.SelectedLayers);
    }

    private static string MarkerSnapshot(Timeline timeline) =>
        string.Join(
            "|",
            timeline.Items
                .Where(x => x.Remark?.StartsWith("CNWL_OBSERVER_", StringComparison.Ordinal) == true)
                .OrderBy(x => x.Remark, StringComparer.Ordinal)
                .Select(x => $"{x.Remark}@L{x.Layer}:F{x.Frame}"));

    private static IEnumerable<LayerPair> MatchSettings(
        ImmutableList<LayerSetting> before,
        ImmutableList<LayerSetting> after)
    {
        static (string?, Color, bool, double) Key(LayerSetting setting) =>
            (setting.Label, setting.Color, setting.IsHidden, setting.Volume);

        var oldUnique = before
            .Where(x => !x.IsEmpty())
            .GroupBy(Key)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.First().Layer);

        foreach (var group in after
                     .Where(x => !x.IsEmpty())
                     .GroupBy(Key)
                     .Where(x => x.Count() == 1))
        {
            if (oldUnique.TryGetValue(group.Key, out var oldLayer))
                yield return new LayerPair(oldLayer, group.First().Layer);
        }
    }

    private static StructuralDetection DetectHostDelta(
        HostSnapshot before,
        HostSnapshot after,
        bool layerSettingsSignaled)
    {
        var pairs = new List<LayerPair>();
        var survivingOldLayers = new HashSet<int>();

        foreach (var (item, newLayer) in after.ItemLayers)
        {
            if (!before.ItemLayers.TryGetValue(item, out var oldLayer))
                continue;

            pairs.Add(new LayerPair(oldLayer, newLayer));
            survivingOldLayers.Add(oldLayer);
        }

        var vanished = before.ItemLayers.Values
            .Where(x => !survivingOldLayers.Contains(x))
            .ToHashSet();

        pairs.AddRange(MatchSettings(before.Settings, after.Settings));

        var oldEmpty = before.Settings
            .Where(x => x.IsEmpty())
            .Select(x => x.Layer)
            .ToHashSet();

        var insertedHints = after.Settings
            .Where(x => x.IsEmpty())
            .Select(x => x.Layer)
            .Where(x => !oldEmpty.Contains(x))
            .ToHashSet();

        var detection = StructuralDeltaDetector.Detect(
            pairs,
            vanished,
            insertedHints);

        var settingsReferenceChanged = !ReferenceEquals(before.Settings, after.Settings);

        if (detection.Status == StructuralDetectionStatus.Exact
            && detection.Edits.Any(x => x is InsertLayers or DeleteLayers)
            && !layerSettingsSignaled)
        {
            detection = StructuralDetection.Ambiguous(
                "Insert/delete-like item motion without LayerSettings signal.");
        }

        Log(
            $"detect settings_signal={layerSettingsSignaled} settings_ref_changed={settingsReferenceChanged} pairs={string.Join(",", pairs.Select(x => $"{x.OldLayer}>{x.NewLayer}"))} " +
            $"vanished={string.Join(",", vanished.OrderBy(x => x))} inserted={string.Join(",", insertedHints.OrderBy(x => x))} " +
            $"status={detection.Status} edits={EditText(detection)} reason={detection.Reason}");

        return detection;
    }

    private static string EditText(StructuralDetection detection) =>
        string.Join(
            "|",
            detection.Edits.Select(
                edit => edit switch
                {
                    InsertLayers x => $"I:{x.Position}:{x.Count}",
                    DeleteLayers x => $"D:{x.Position}:{x.Count}",
                    SwapAdjacentLayers x => $"S:{x.FirstLayer}",
                    _ => edit.GetType().Name
                }));

    private static FrameworkElement? FindElementForDataContext(
        FrameworkElement root,
        object dataContext) =>
        Host.Elements(root)
            .Where(x => x.IsVisible && ReferenceEquals(x.DataContext, dataContext))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private static string ExecuteStandardLayerCommand(
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
            labelElement.Focus();

        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);

        var targets = new List<(string Name, IInputElement Target)>();
        if (labelElement is not null)
            targets.Add(("LayerLabel", labelElement));
        if (Keyboard.FocusedElement is IInputElement focused)
            targets.Add(("Focused", focused));
        targets.Add(("TimelineView", timelineView));
        targets.Add(("Window", window));

        var parameters = new object?[] { layer, null, labelVm, timeline };

        if (command is RoutedCommand routed)
        {
            foreach (var (name, target) in targets
                         .GroupBy(x => x.Target, ReferenceEqualityComparer.Instance)
                         .Select(x => x.First()))
            {
                foreach (var parameter in parameters)
                {
                    bool can;
                    try { can = routed.CanExecute(parameter, target); }
                    catch { continue; }

                    Log($"route_can command={commandType} target={name} param={parameter?.GetType().Name ?? "<null>"} can={can}");

                    if (!can)
                        continue;

                    routed.Execute(parameter, target);
                    return $"{name}|{parameter?.GetType().Name ?? "<null>"}:{parameter ?? ""}";
                }
            }
        }

        throw new InvalidOperationException("No routed command path for " + commandType);
    }

    private static async Task WaitUntil(
        Func<bool> condition,
        string name,
        int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException(name);
    }

    private static async Task Exercise(
        string name,
        CommandType command,
        int layer,
        string expectedForward,
        string expectedUndo,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        Func<int> recorded,
        Func<int> undoed,
        Func<int> settingsSignals,
        string baseline)
    {
        var before = Snapshot(timeline);
        var recordedBefore = recorded();
        var undoedBefore = undoed();
        var settingsBefore = settingsSignals();

        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        timeline.LayerSelection.SelectedLayers = ImmutableList.Create(layer);
        await Task.Delay(120);

        var route = ExecuteStandardLayerCommand(
            command,
            window,
            timelineView,
            vm,
            timeline,
            layer);

        await WaitUntil(
            () => recorded() > recordedBefore,
            name + " Recorded");

        await Task.Delay(150);
        var after = Snapshot(timeline);
        var forward = DetectHostDelta(
            before,
            after,
            settingsSignals() > settingsBefore);

        Fact(name + "_route", route);
        Fact(name + "_forward", EditText(forward));
        Check(name + "_forward_exact", forward.Status == StructuralDetectionStatus.Exact);
        Check(name + "_forward_edit", EditText(forward) == expectedForward);

        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        var undoSettingsBefore = settingsSignals();
        await Native.Key(0x5A, true);

        await WaitUntil(
            () => undoed() > undoedBefore && MarkerSnapshot(timeline) == baseline,
            name + " Undo");

        await Task.Delay(150);
        var restored = Snapshot(timeline);
        var reverse = DetectHostDelta(
            after,
            restored,
            settingsSignals() > undoSettingsBefore);

        Fact(name + "_undo", EditText(reverse));
        Check(name + "_undo_exact", reverse.Status == StructuralDetectionStatus.Exact);
        Check(name + "_undo_edit", EditText(reverse) == expectedUndo);
        Check(name + "_baseline_restored", MarkerSnapshot(timeline) == baseline);
    }

    private static async Task Run(Window window)
    {
        UndoRedoManager? manager = null;
        EventHandler? recordedHandler = null;
        EventHandler? undoedHandler = null;
        EventHandler? redoedHandler = null;
        PropertyChangedEventHandler? layerSettingsHandler = null;

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

            var main = window.DataContext
                ?? throw new InvalidOperationException("MainViewModel missing");
            var model = main.GetType().GetField("model", Host.Flags)?.GetValue(main)
                ?? throw new MissingMemberException("MainViewModel.model");
            manager = Host.Get(model, "UndoRedoManager") as UndoRedoManager
                ?? throw new MissingMemberException("UndoRedoManager");

            Check("visible_context_bound", ReferenceEquals(timelineView.DataContext, vm));

            var character = new Character { Name = "CNWL_OBSERVER" };
            for (var layer = 0; layer <= 8; layer++)
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20 + layer * 40,
                    Layer = layer,
                    Length = 20,
                    Serif = "L" + layer,
                    Remark = "CNWL_OBSERVER_L" + layer
                };

                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add L" + layer);
            }

            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            timeline.LayerSelection.SelectedLayers = ImmutableList<int>.Empty;
            await Task.Delay(700);

            manager.Record();

            var recordedCount = 0;
            var undoedCount = 0;
            var redoedCount = 0;
            var layerSettingsSignals = 0;

            recordedHandler = (_, _) =>
            {
                recordedCount++;
                Log("manager_recorded=" + recordedCount);
            };
            undoedHandler = (_, _) =>
            {
                undoedCount++;
                Log("manager_undoed=" + undoedCount);
            };
            redoedHandler = (_, _) =>
            {
                redoedCount++;
                Log("manager_redoed=" + redoedCount);
            };

            layerSettingsHandler = (_, e) =>
            {
                layerSettingsSignals++;
                Log($"layer_settings_signal={layerSettingsSignals} property={e.PropertyName ?? "<null>"}");
            };

            manager.Recorded += recordedHandler;
            manager.Undoed += undoedHandler;
            manager.Redoed += redoedHandler;
            timeline.LayerSettings.PropertyChanged += layerSettingsHandler;

            var baseline = MarkerSnapshot(timeline);
            Fact("baseline", baseline);

            await Exercise(
                "add_L3",
                CommandType.AddLayer,
                3,
                "I:3:1",
                "D:3:1",
                window,
                timelineView,
                vm,
                timeline,
                () => recordedCount,
                () => undoedCount,
                () => layerSettingsSignals,
                baseline);

            await Exercise(
                "delete_L3",
                CommandType.DeleteLayer,
                3,
                "D:3:1",
                "I:3:1",
                window,
                timelineView,
                vm,
                timeline,
                () => recordedCount,
                () => undoedCount,
                () => layerSettingsSignals,
                baseline);

            await Exercise(
                "move_down_L3",
                CommandType.MoveDownLayer,
                3,
                "S:3",
                "S:3",
                window,
                timelineView,
                vm,
                timeline,
                () => recordedCount,
                () => undoedCount,
                () => layerSettingsSignals,
                baseline);

            await Exercise(
                "move_up_L4",
                CommandType.MoveUpLayer,
                4,
                "S:3",
                "S:3",
                window,
                timelineView,
                vm,
                timeline,
                () => recordedCount,
                () => undoedCount,
                () => layerSettingsSignals,
                baseline);

            // Strong false-positive check: imitate an insertion-like coordinated
            // item move while leaving LayerSettings unchanged. The pure detector
            // sees the same monotonic pattern, but the host boundary must reject
            // it because no layer structure changed.
            var beforeFake = Snapshot(timeline);
            foreach (var item in timeline.Items.Where(x => x.Layer >= 3).OrderByDescending(x => x.Layer))
                item.Layer++;

            await Task.Delay(150);
            var afterFake = Snapshot(timeline);
            var fakeSignalsBefore = layerSettingsSignals;
            var fake = DetectHostDelta(
                beforeFake,
                afterFake,
                layerSettingsSignals > fakeSignalsBefore);

            Fact("coordinated_item_shift", EditText(fake));
            Check("coordinated_item_shift_not_exact", fake.Status == StructuralDetectionStatus.Ambiguous);
            Check("coordinated_item_shift_no_settings_signal", layerSettingsSignals == fakeSignalsBefore);

            Check("manager_recorded_observed", recordedCount >= 4);
            Check("manager_undoed_observed", undoedCount >= 4);
            Check("manager_redoed_not_required", redoedCount == 0);

            Check(
                "no_harmony_loaded",
                !AppDomain.CurrentDomain.GetAssemblies().Any(
                    x => string.Equals(x.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(x.GetName().Name, "HarmonyLib", StringComparison.OrdinalIgnoreCase)));

            Check(
                "no_harmony_reference",
                !typeof(Probe).Assembly.GetReferencedAssemblies().Any(
                    x => string.Equals(x.Name, "0Harmony", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(x.Name, "HarmonyLib", StringComparison.OrdinalIgnoreCase)));

            Fact("host_version", typeof(Timeline).Assembly.GetName().Version);
            Fact("recorded_total", recordedCount);
            Fact("undoed_total", undoedCount);
            Fact("redoed_total", redoedCount);
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
                    if (undoedHandler is not null)
                        manager.Undoed -= undoedHandler;
                    if (redoedHandler is not null)
                        manager.Redoed -= redoedHandler;
                }

                if (layerSettingsHandler is not null)
                {
                    var visible = Host.Elements(window)
                        .FirstOrDefault(x => x.GetType().Name == "TimelineView" && x.IsVisible);
                    if (visible?.DataContext is TimelineViewModel visibleVm)
                    {
                        var visibleTimeline = Host.Get(visibleVm, "Timeline") as Timeline
                            ?? visibleVm.GetType().GetField("timeline", Host.Flags)?.GetValue(visibleVm) as Timeline;
                        if (visibleTimeline is not null)
                            visibleTimeline.LayerSettings.PropertyChanged -= layerSettingsHandler;
                    }
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
