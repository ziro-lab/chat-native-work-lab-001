using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ymm4NoHarmonyFolderRanges;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class UndoSyncEntry : ILocalizePlugin
{
    public string Name => "CNWL no-Harmony undo sync";
    public void SetCulture(CultureInfo cultureInfo) => UndoSyncProbe.Schedule();
}

internal static class UndoSyncProbe
{
    private static readonly Guid ParentId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChildId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly FolderRange[] BaselineFolders =
    [
        new FolderRange(ParentId, 2, 8),
        new FolderRange(ChildId, 4, 6)
    ];

    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static readonly List<string> facts = [];
    private static FolderRange[] folders = CloneRanges(BaselineFolders);

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_UNDO_SYNC_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path))
            return;

        scheduled = true;
        output = Path.GetFullPath(path);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(Bootstrap),
            DispatcherPriority.ApplicationIdle);
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
                    if (main?.GetType().FullName !=
                        "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = Host.Get(main, "ActiveTimelineViewModel");
                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType()
                            .GetMethod("CreateProject", Type.EmptyTypes)?
                            .Invoke(main, null);
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

    private static FolderRange[] CloneRanges(IEnumerable<FolderRange> source) =>
        source.ToArray();

    private static string FolderText(IEnumerable<FolderRange>? source = null) =>
        string.Join(
            "|",
            (source ?? folders)
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End)
                .Select(x =>
                    $"{(x.Id == ParentId ? "A" : x.Id == ChildId ? "B" : "?")}:{x.Start}-{x.End}"));

    private static bool SameRanges(
        IReadOnlyList<FolderRange> left,
        IReadOnlyList<FolderRange> right) =>
        left.SequenceEqual(right);

    private static void SetFolders(
        IReadOnlyList<FolderRange> next,
        string reason)
    {
        folders = CloneRanges(next);
        Log($"folder_state reason={reason} value={FolderText()}");
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

    private static string TimelineSnapshot(Timeline timeline) =>
        string.Join(
            "|",
            timeline.Items
                .Where(x =>
                    x.Remark?.StartsWith(
                        "CNWL_UNDOSYNC_",
                        StringComparison.Ordinal) == true)
                .OrderBy(x => x.Remark, StringComparer.Ordinal)
                .Select(x =>
                    $"{x.Remark}@L{x.Layer}:F{x.Frame}"));

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
            .Where(x =>
                x.IsVisible
                && ReferenceEquals(x.DataContext, dataContext))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private sealed record RouteResult(
        string Command,
        string Target,
        string Parameter,
        bool Executed);

    private static RouteResult ExecuteStandardLayerCommand(
        CommandType commandType,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        int layer)
    {
        ICommand command = CommandSettings.Default[commandType]
            ?? throw new InvalidOperationException(
                "Command missing: " + commandType);

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
        Native.SetForegroundWindow(
            new WindowInteropHelper(window).Handle);

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
            .GroupBy(
                x => x.Target,
                ReferenceEqualityComparer.Instance)
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
                        Log(
                            $"route_can_exception command={commandType} target={targetName} param={DescribeParameter(parameter)} error={ex.GetBaseException().Message}");
                        continue;
                    }

                    Log(
                        $"route_can command={commandType} target={targetName} param={DescribeParameter(parameter)} can={can}");

                    if (!can)
                        continue;

                    routed.Execute(parameter, target);

                    return new RouteResult(
                        commandType.ToString(),
                        targetName,
                        DescribeParameter(parameter),
                        true);
                }
            }
        }

        throw new InvalidOperationException(
            $"No executable route for {commandType}; label_element={labelElement?.GetType().FullName ?? "<none>"}");
    }

    private sealed record PreviewFact(
        CommandType CommandType,
        int Layer,
        string Before,
        string After,
        bool StateChanged,
        StructuralEdit Edit);

    private sealed class FolderUndoBridge : IDisposable
    {
        private readonly FrameworkElement root;
        private readonly UndoRedoManager manager;
        private readonly ExecutedRoutedEventHandler previewHandler;

        public FolderUndoBridge(
            FrameworkElement root,
            UndoRedoManager manager)
        {
            this.root = root;
            this.manager = manager;
            previewHandler = OnPreviewExecuted;
            root.AddHandler(
                CommandManager.PreviewExecutedEvent,
                previewHandler,
                true);
        }

        public PreviewFact? Last { get; private set; }
        public int SeenCount { get; private set; }
        public int AddedCommandCount { get; private set; }
        public int UndoCallbackCount { get; private set; }
        public int RedoCallbackCount { get; private set; }

        public void ResetLast() => Last = null;

        private static bool TryMap(
            ICommand command,
            int layer,
            out CommandType commandType,
            out StructuralEdit? edit)
        {
            foreach (var type in new[]
            {
                CommandType.AddLayer,
                CommandType.DeleteLayer,
                CommandType.MoveUpLayer,
                CommandType.MoveDownLayer
            })
            {
                var configured = CommandSettings.Default[type];

                if (configured is null
                    || !ReferenceEquals(command, configured))
                    continue;

                commandType = type;
                edit = type switch
                {
                    CommandType.AddLayer =>
                        new InsertLayers(layer, 1),
                    CommandType.DeleteLayer =>
                        new DeleteLayers(layer, 1),
                    CommandType.MoveDownLayer =>
                        new SwapAdjacentLayers(layer),
                    CommandType.MoveUpLayer when layer > 0 =>
                        new SwapAdjacentLayers(layer - 1),
                    _ => null
                };
                return edit is not null;
            }

            commandType = default;
            edit = null;
            return false;
        }

        private void OnPreviewExecuted(
            object sender,
            ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is not int layer
                || !TryMap(
                    e.Command,
                    layer,
                    out var commandType,
                    out var edit)
                || edit is null)
                return;

            SeenCount++;

            var before = CloneRanges(folders);
            var plan = FolderRangeTracker.Apply(before, edit);
            var after = CloneRanges(plan.Ranges);
            var changed = !SameRanges(before, after);

            Last = new PreviewFact(
                commandType,
                layer,
                FolderText(before),
                FolderText(after),
                changed,
                edit);

            Log(
                $"preview command={commandType} layer={layer} before={Last.Before} after={Last.After} changed={changed}");

            if (!changed)
                return;

            // Critical P1.4 boundary:
            // mutate plugin state + enqueue our undo command BEFORE the native
            // structural command handler completes. Do NOT call Record() here.
            SetFolders(after, "preview-forward");

            manager.AddCommand(
                new UndoRedoActionCommand(
                    () =>
                    {
                        UndoCallbackCount++;
                        SetFolders(before, "native-undo-callback");
                    },
                    () =>
                    {
                        RedoCallbackCount++;
                        SetFolders(after, "native-redo-callback");
                    }));

            AddedCommandCount++;
            Log(
                $"plugin_add_command count={AddedCommandCount} command={commandType} layer={layer}");
        }

        public void Dispose() =>
            root.RemoveHandler(
                CommandManager.PreviewExecutedEvent,
                previewHandler);
    }

    private static async Task Exercise(
        string name,
        CommandType commandType,
        int layer,
        string expectedFolderAfter,
        bool expectedFolderChange,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        FolderUndoBridge bridge,
        string timelineBaseline,
        string folderBaseline,
        Func<int> recordedCount,
        Func<int> undoCount,
        Func<int> redoCount)
    {
        Check(name + "_timeline_starts_baseline",
            TimelineSnapshot(timeline) == timelineBaseline);
        Check(name + "_folder_starts_baseline",
            FolderText() == folderBaseline);

        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        timeline.LayerSelection.SelectedLayers =
            ImmutableList.Create(layer);
        await Task.Delay(100);

        bridge.ResetLast();

        var recordedBefore = recordedCount();
        var undoBeforeCallbacks = bridge.UndoCallbackCount;
        var redoBeforeCallbacks = bridge.RedoCallbackCount;
        var addBefore = bridge.AddedCommandCount;

        var route = ExecuteStandardLayerCommand(
            commandType,
            window,
            timelineView,
            vm,
            timeline,
            layer);

        await WaitUntil(
            name + " forward stabilization",
            () =>
                recordedCount() > recordedBefore
                && TimelineSnapshot(timeline) != timelineBaseline
                && FolderText() == expectedFolderAfter);

        var timelineAfter = TimelineSnapshot(timeline);
        var preview = bridge.Last;

        Fact(name + "_route",
            $"{route.Target}|{route.Parameter}");
        Fact(name + "_timeline_after", timelineAfter);
        Fact(name + "_folder_after", FolderText());
        Fact(name + "_preview_before", preview?.Before);
        Fact(name + "_preview_after", preview?.After);
        Fact(name + "_recorded_delta",
            recordedCount() - recordedBefore);

        Check(name + "_route_executed", route.Executed);
        Check(name + "_preview_seen", preview is not null);
        Check(name + "_preview_command",
            preview?.CommandType == commandType);
        Check(name + "_preview_layer",
            preview?.Layer == layer);
        Check(name + "_preview_change",
            preview?.StateChanged == expectedFolderChange);
        Check(name + "_folder_forward_exact",
            FolderText() == expectedFolderAfter);
        Check(name + "_single_record_boundary",
            recordedCount() - recordedBefore == 1);
        Check(
            name + "_plugin_command_count",
            bridge.AddedCommandCount - addBefore ==
                (expectedFolderChange ? 1 : 0));

        window.Activate();
        Native.SetForegroundWindow(
            new WindowInteropHelper(window).Handle);

        var undoEventBefore = undoCount();
        await Native.Key(0x5A, true);

        await WaitUntil(
            name + " undo stabilization",
            () =>
                undoCount() > undoEventBefore
                && TimelineSnapshot(timeline) == timelineBaseline
                && FolderText() == folderBaseline);

        Fact(name + "_undo_event_delta",
            undoCount() - undoEventBefore);
        Fact(name + "_folder_undo_callbacks",
            bridge.UndoCallbackCount - undoBeforeCallbacks);

        Check(name + "_one_undo_event",
            undoCount() - undoEventBefore == 1);
        Check(name + "_undo_timeline_baseline",
            TimelineSnapshot(timeline) == timelineBaseline);
        Check(name + "_undo_folder_baseline",
            FolderText() == folderBaseline);
        Check(
            name + "_undo_callback_count",
            bridge.UndoCallbackCount - undoBeforeCallbacks ==
                (expectedFolderChange ? 1 : 0));

        var redoEventBefore = redoCount();
        await Native.Key(0x59, true);

        await WaitUntil(
            name + " redo stabilization",
            () =>
                redoCount() > redoEventBefore
                && TimelineSnapshot(timeline) == timelineAfter
                && FolderText() == expectedFolderAfter);

        Fact(name + "_redo_event_delta",
            redoCount() - redoEventBefore);
        Fact(name + "_folder_redo_callbacks",
            bridge.RedoCallbackCount - redoBeforeCallbacks);

        Check(name + "_one_redo_event",
            redoCount() - redoEventBefore == 1);
        Check(name + "_redo_timeline_exact",
            TimelineSnapshot(timeline) == timelineAfter);
        Check(name + "_redo_folder_exact",
            FolderText() == expectedFolderAfter);
        Check(
            name + "_redo_callback_count",
            bridge.RedoCallbackCount - redoBeforeCallbacks ==
                (expectedFolderChange ? 1 : 0));

        undoEventBefore = undoCount();
        await Native.Key(0x5A, true);

        await WaitUntil(
            name + " reset stabilization",
            () =>
                undoCount() > undoEventBefore
                && TimelineSnapshot(timeline) == timelineBaseline
                && FolderText() == folderBaseline);

        Check(name + "_reset_timeline_baseline",
            TimelineSnapshot(timeline) == timelineBaseline);
        Check(name + "_reset_folder_baseline",
            FolderText() == folderBaseline);
        Check(
            name + "_second_undo_callback",
            bridge.UndoCallbackCount - undoBeforeCallbacks ==
                (expectedFolderChange ? 2 : 0));
    }

    private static async Task Run(Window window)
    {
        UndoRedoManager? manager = null;
        FolderUndoBridge? bridge = null;
        EventHandler? recordedHandler = null;
        EventHandler? undoHandler = null;
        EventHandler? redoHandler = null;

        try
        {
            window.WindowState = WindowState.Normal;
            window.Left = 0;
            window.Top = 0;
            window.Width = 1000;
            window.Height = 700;
            window.Activate();
            Native.SetForegroundWindow(
                new WindowInteropHelper(window).Handle);
            await Task.Delay(1000);

            var timelineView = Host.Elements(window)
                .Where(x =>
                    x.GetType().Name == "TimelineView"
                    && x.IsVisible)
                .OrderByDescending(
                    x => x.ActualWidth * x.ActualHeight)
                .First();

            var vm = timelineView.DataContext as TimelineViewModel
                ?? throw new InvalidOperationException(
                    "Visible TimelineViewModel missing");

            var timeline =
                Host.Get(vm, "Timeline") as Timeline
                ?? vm.GetType()
                    .GetField("timeline", Host.Flags)?
                    .GetValue(vm) as Timeline
                ?? throw new InvalidOperationException(
                    "Visible Timeline missing");

            Check(
                "visible_context_bound",
                ReferenceEquals(
                    timelineView.DataContext,
                    vm));

            var character = new Character
            {
                Name = "CNWL_UNDO_SYNC"
            };

            for (var layer = 0; layer <= 8; layer++)
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20 + layer * 40,
                    Layer = layer,
                    Length = 20,
                    Serif = "L" + layer,
                    Remark = "CNWL_UNDOSYNC_L" + layer
                };

                if (!timeline.TryAddItems(
                    [item],
                    item.Frame,
                    item.Layer))
                {
                    throw new InvalidOperationException(
                        "Fixture add L" + layer);
                }
            }

            timeline.SelectedItems =
                ImmutableList<IItem>.Empty;
            timeline.LayerSelection.SelectedLayers =
                ImmutableList<int>.Empty;
            await Task.Delay(600);

            var main = window.DataContext
                ?? throw new InvalidOperationException(
                    "MainViewModel missing");

            var model = main.GetType()
                .GetField("model", Host.Flags)?
                .GetValue(main)
                ?? throw new MissingMemberException(
                    "MainViewModel.model");

            manager =
                Host.Get(model, "UndoRedoManager")
                    as UndoRedoManager
                ?? throw new MissingMemberException(
                    "MainModel.UndoRedoManager");

            // Fixture creation is a separate historical unit.
            // This is the ONLY explicit Record() call made by this probe.
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

            SetFolders(BaselineFolders, "test-baseline");
            var timelineBaseline =
                TimelineSnapshot(timeline);
            var folderBaseline = FolderText();

            Fact("timeline_baseline", timelineBaseline);
            Fact("folder_baseline", folderBaseline);
            Fact(
                "plugin_explicit_record_calls_during_commands",
                0);

            Check(
                "fixture_count",
                timeline.Items.Count(
                    x =>
                        x.Remark?.StartsWith(
                            "CNWL_UNDOSYNC_",
                            StringComparison.Ordinal) == true) == 9);
            Check(
                "folder_baseline_valid",
                folderBaseline == "A:2-8|B:4-6");

            bridge = new FolderUndoBridge(
                window,
                manager);

            await Exercise(
                "add_inside_L5",
                CommandType.AddLayer,
                5,
                "A:2-9|B:4-7",
                true,
                window,
                timelineView,
                vm,
                timeline,
                bridge,
                timelineBaseline,
                folderBaseline,
                () => recorded,
                () => undone,
                () => redone);

            await Exercise(
                "delete_inside_L5",
                CommandType.DeleteLayer,
                5,
                "A:2-7|B:4-5",
                true,
                window,
                timelineView,
                vm,
                timeline,
                bridge,
                timelineBaseline,
                folderBaseline,
                () => recorded,
                () => undone,
                () => redone);

            await Exercise(
                "move_down_L5",
                CommandType.MoveDownLayer,
                5,
                folderBaseline,
                false,
                window,
                timelineView,
                vm,
                timeline,
                bridge,
                timelineBaseline,
                folderBaseline,
                () => recorded,
                () => undone,
                () => redone);

            Check(
                "final_timeline_baseline",
                TimelineSnapshot(timeline) ==
                    timelineBaseline);
            Check(
                "final_folder_baseline",
                FolderText() == folderBaseline);

            Fact("manager_recorded_total", recorded);
            Fact("manager_undoed_total", undone);
            Fact("manager_redoed_total", redone);
            Fact(
                "bridge_preview_total",
                bridge.SeenCount);
            Fact(
                "bridge_add_command_total",
                bridge.AddedCommandCount);
            Fact(
                "bridge_undo_callbacks_total",
                bridge.UndoCallbackCount);
            Fact(
                "bridge_redo_callbacks_total",
                bridge.RedoCallbackCount);
            Fact(
                "host_version",
                typeof(Timeline)
                    .Assembly
                    .GetName()
                    .Version);

            Check(
                "bridge_preview_expected",
                bridge.SeenCount == 3);
            Check(
                "bridge_add_command_expected",
                bridge.AddedCommandCount == 2);
            Check(
                "bridge_undo_callbacks_expected",
                bridge.UndoCallbackCount == 4);
            Check(
                "bridge_redo_callbacks_expected",
                bridge.RedoCallbackCount == 2);

            Check(
                "no_harmony_loaded",
                !AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Any(
                        x =>
                            string.Equals(
                                x.GetName().Name,
                                "0Harmony",
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                x.GetName().Name,
                                "HarmonyLib",
                                StringComparison.OrdinalIgnoreCase)));

            Check(
                "no_harmony_reference",
                !typeof(UndoSyncProbe)
                    .Assembly
                    .GetReferencedAssemblies()
                    .Any(
                        x =>
                            string.Equals(
                                x.Name,
                                "0Harmony",
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                x.Name,
                                "HarmonyLib",
                                StringComparison.OrdinalIgnoreCase)));

            Log("P1.4 undo synchronization probe complete");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                bridge?.Dispose();

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
        File.AppendAllText(
            Path.Combine(output, "error.txt"),
            ex + Environment.NewLine);
        Log("FAIL " + ex.GetBaseException().Message);
    }

    private static void Finish()
    {
        var temp = Path.Combine(
            output,
            "result.tmp");

        File.WriteAllLines(
            temp,
            new[]
            {
                "status=" + (
                    failed
                        ? "FAIL_UNDO_SYNC"
                        : "PASS_UNDO_SYNC"),
                "assertion_count=" + checks.Count
            }
            .Concat(checks)
            .Concat(facts));

        File.Move(
            temp,
            Path.Combine(output, "result.txt"),
            true);
    }
}
