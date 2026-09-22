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

public sealed class StructuralUndoSyncEntry : ILocalizePlugin
{
    public string Name => "CNWL structural undo sync";
    public void SetCulture(CultureInfo cultureInfo) => StructuralUndoSyncProbe.Schedule();
}

internal static class StructuralUndoSyncProbe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static readonly List<string> facts = [];

    private static readonly Guid FolderA =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FolderC =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private sealed record HostSnapshot(
        Dictionary<IItem, int> Layers,
        HashSet<int> EmptySettingLayers,
        string Text);

    private sealed record RouteResult(
        string Target,
        string Parameter,
        bool Executed);

    private sealed class FolderState
    {
        private FolderRange[] ranges =
        [
            new FolderRange(FolderA, 2, 6),
            new FolderRange(FolderC, 20, 30)
        ];

        public IReadOnlyList<FolderRange> Ranges => ranges;

        public void Restore(IEnumerable<FolderRange> next) =>
            ranges = next
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End)
                .ThenBy(x => x.Id)
                .ToArray();

        public string Signature => string.Join(
            "|",
            ranges.Select(
                x => $"{Short(x.Id)}:{x.Start}-{x.End}"));

        private static string Short(Guid id) =>
            id == FolderA ? "A" : id == FolderC ? "C" : id.ToString("N")[..4];
    }

    private sealed class PendingFolderHistory
    {
        private readonly FolderState folderState;
        private readonly Action<string> trace;

        public PendingFolderHistory(
            FolderState folderState,
            CommandType commandType,
            int layer,
            HostSnapshot hostBefore,
            int previewSequence,
            Action<string> trace)
        {
            this.folderState = folderState;
            this.trace = trace;
            CommandType = commandType;
            Layer = layer;
            HostBefore = hostBefore;
            PreviewSequence = previewSequence;
            Before = folderState.Ranges.ToArray();
        }

        public CommandType CommandType { get; }
        public int Layer { get; }
        public HostSnapshot HostBefore { get; }
        public FolderRange[] Before { get; }
        public FolderRange[]? After { get; private set; }
        public StructuralDetection? Detection { get; private set; }

        public bool Armed { get; private set; }
        public int PreviewSequence { get; }
        public int RecordedSequence { get; set; }
        public int ExecutedSequence { get; set; }
        public int UndoCalls { get; private set; }
        public int RedoCalls { get; private set; }

        public void Arm(
            StructuralDetection detection,
            IReadOnlyList<FolderRange> after)
        {
            Detection = detection;
            After = after.ToArray();
            Armed = true;
            folderState.Restore(After);
            trace(
                $"transaction_armed command={CommandType} layer={Layer} folder={folderState.Signature}");
        }

        public void Undo()
        {
            UndoCalls++;
            trace(
                $"folder_undo call={UndoCalls} armed={Armed} command={CommandType} layer={Layer}");
            if (Armed)
                folderState.Restore(Before);
        }

        public void Redo()
        {
            RedoCalls++;
            trace(
                $"folder_redo call={RedoCalls} armed={Armed} command={CommandType} layer={Layer}");
            if (Armed && After is not null)
                folderState.Restore(After);
        }
    }

    private sealed class StructuralHistoryBridge : IDisposable
    {
        private readonly FrameworkElement root;
        private readonly Timeline timeline;
        private readonly UndoRedoManager manager;
        private readonly FolderState folderState;
        private readonly ExecutedRoutedEventHandler previewHandler;
        private readonly ExecutedRoutedEventHandler executedHandler;
        private int sequence;

        public StructuralHistoryBridge(
            FrameworkElement root,
            Timeline timeline,
            UndoRedoManager manager,
            FolderState folderState)
        {
            this.root = root;
            this.timeline = timeline;
            this.manager = manager;
            this.folderState = folderState;

            previewHandler = OnPreviewExecuted;
            executedHandler = OnExecuted;

            root.AddHandler(
                CommandManager.PreviewExecutedEvent,
                previewHandler,
                true);
            root.AddHandler(
                CommandManager.ExecutedEvent,
                executedHandler,
                true);

            manager.Recorded += Manager_Recorded;
        }

        public PendingFolderHistory? Current { get; private set; }
        public PendingFolderHistory? Last { get; private set; }
        public int PreviewCount { get; private set; }
        public int ExecutedCount { get; private set; }
        public int RecordedWhilePendingCount { get; private set; }

        private int NextSequence(string label)
        {
            sequence++;
            Log($"sequence={sequence} {label}");
            return sequence;
        }

        private static bool TryGetStructural(
            ExecutedRoutedEventArgs e,
            out CommandType commandType,
            out int layer)
        {
            layer = -1;
            commandType = default;

            if (e.Parameter is not int value)
                return false;

            foreach (var type in new[]
            {
                CommandType.AddLayer,
                CommandType.DeleteLayer,
                CommandType.MoveUpLayer,
                CommandType.MoveDownLayer
            })
            {
                var configured = CommandSettings.Default[type];
                if (configured is null || !ReferenceEquals(e.Command, configured))
                    continue;

                commandType = type;
                layer = value;
                return true;
            }

            return false;
        }

        private void OnPreviewExecuted(
            object sender,
            ExecutedRoutedEventArgs e)
        {
            if (!TryGetStructural(e, out var commandType, out var layer))
                return;

            if (Current is { } existing
                && existing.ExecutedSequence == 0)
            {
                throw new InvalidOperationException(
                    "Overlapping structural history transaction.");
            }

            PreviewCount++;
            var tx = new PendingFolderHistory(
                folderState,
                commandType,
                layer,
                Capture(timeline),
                NextSequence($"preview command={commandType} layer={layer}"),
                Log);

            Current = tx;
            Last = tx;

            manager.AddCommand(
                new UndoRedoActionCommand(
                    tx.Undo,
                    tx.Redo));

            Log(
                $"folder_command_added command={commandType} layer={layer} folder_before={folderState.Signature}");
        }

        private void OnExecuted(
            object sender,
            ExecutedRoutedEventArgs e)
        {
            if (!TryGetStructural(e, out var commandType, out var layer))
                return;

            var tx = Current
                ?? throw new InvalidOperationException(
                    "Executed structural command without preview transaction.");

            if (tx.CommandType != commandType || tx.Layer != layer)
            {
                throw new InvalidOperationException(
                    $"Structural command mismatch preview={tx.CommandType}:{tx.Layer} executed={commandType}:{layer}");
            }

            ExecutedCount++;
            tx.ExecutedSequence =
                NextSequence($"executed command={commandType} layer={layer}");

            var afterHost = Capture(timeline);
            var detection = Detect(
                tx.HostBefore,
                afterHost,
                new HashSet<int> { layer });

            Log(
                $"post_detect command={commandType} layer={layer} detection={Describe(detection)}");

            if (detection.Status != StructuralDetectionStatus.Exact
                || detection.Edits.Count != 1)
            {
                Log("transaction_not_armed reason=non_exact_or_multi_edit");
                return;
            }

            var plan = FolderRangeTracker.Apply(
                folderState.Ranges,
                detection.Edits[0]);

            tx.Arm(detection, plan.Ranges);
        }

        private void Manager_Recorded(object? sender, EventArgs e)
        {
            if (Current is not { } tx || tx.RecordedSequence != 0)
                return;

            RecordedWhilePendingCount++;
            tx.RecordedSequence =
                NextSequence(
                    $"recorded pending={tx.CommandType}:{tx.Layer} armed={tx.Armed}");
        }

        public void Dispose()
        {
            manager.Recorded -= Manager_Recorded;
            root.RemoveHandler(
                CommandManager.PreviewExecutedEvent,
                previewHandler);
            root.RemoveHandler(
                CommandManager.ExecutedEvent,
                executedHandler);
        }
    }

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_STRUCTURAL_UNDO_SYNC_DIR");
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
                    if (main?.GetType().FullName
                        != "YukkuriMovieMaker.ViewModels.MainViewModel")
                    {
                        continue;
                    }

                    var active = Host.Get(
                        main,
                        "ActiveTimelineViewModel");

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType()
                            .GetMethod(
                                "CreateProject",
                                Type.EmptyTypes)?
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

    private static void Log(string text) =>
        File.AppendAllText(
            Path.Combine(output, "progress.txt"),
            DateTime.UtcNow.ToString("O")
            + " "
            + text
            + Environment.NewLine);

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
        var layers =
            new Dictionary<IItem, int>(
                ReferenceEqualityComparer.Instance);

        foreach (var item in timeline.Items)
        {
            if (item.Remark?.StartsWith(
                    "CNWL_US_",
                    StringComparison.Ordinal)
                == true)
            {
                layers[item] = item.Layer;
            }
        }

        var emptySettings = timeline.LayerSettings.Items
            .Where(x => x.IsEmpty())
            .Select(x => x.Layer)
            .ToHashSet();

        var text = string.Join(
            "|",
            layers
                .OrderBy(
                    x => x.Key.Remark,
                    StringComparer.Ordinal)
                .Select(
                    x =>
                        $"{x.Key.Remark}@L{x.Value}:F{x.Key.Frame}"));

        return new HostSnapshot(
            layers,
            emptySettings,
            text);
    }

    private static StructuralDetection Detect(
        HostSnapshot before,
        HostSnapshot after,
        IReadOnlySet<int>? operationPositionHints = null)
    {
        var pairs = new List<LayerPair>();
        var survivingOldLayers = new HashSet<int>();

        foreach (var (item, newLayer) in after.Layers)
        {
            if (!before.Layers.TryGetValue(
                    item,
                    out var oldLayer))
            {
                continue;
            }

            pairs.Add(
                new LayerPair(
                    oldLayer,
                    newLayer));
            survivingOldLayers.Add(oldLayer);
        }

        var vanished = before.Layers.Values
            .Where(
                x => !survivingOldLayers.Contains(x))
            .ToHashSet();

        var insertedHints = after.EmptySettingLayers
            .Where(
                x => !before.EmptySettingLayers.Contains(x))
            .ToHashSet();

        return StructuralDeltaDetector.Detect(
            pairs,
            vanishedLayers: vanished,
            insertedHints: insertedHints,
            operationPositionHints: operationPositionHints);
    }

    private static string Describe(
        StructuralDetection detection) =>
        detection.Status
        + ":"
        + string.Join(
            "|",
            detection.Edits.Select(
                edit => edit switch
                {
                    InsertLayers x =>
                        $"I:{x.Position}:{x.Count}",
                    DeleteLayers x =>
                        $"D:{x.Position}:{x.Count}",
                    SwapAdjacentLayers x =>
                        $"S:{x.FirstLayer}",
                    _ => edit.GetType().Name
                }));

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

    private static string DescribeParameter(
        object? value) =>
        value switch
        {
            null => "<null>",
            int number => "Int32:" + number,
            _ => value.GetType().FullName
                ?? value.GetType().Name
        };

    private static FrameworkElement? FindElementForDataContext(
        FrameworkElement root,
        object dataContext) =>
        Host.Elements(root)
            .Where(
                x => x.IsVisible
                     && ReferenceEquals(
                         x.DataContext,
                         dataContext))
            .OrderByDescending(
                x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private static RouteResult ExecuteStandardLayerCommand(
        CommandType commandType,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        int layer)
    {
        ICommand command =
            CommandSettings.Default[commandType]
            ?? throw new InvalidOperationException(
                "Command missing: " + commandType);

        var labelVm =
            layer >= 0 && layer < vm.LayerLabels.Count
                ? vm.LayerLabels[layer]
                : null;

        var labelElement =
            labelVm is null
                ? null
                : FindElementForDataContext(
                    timelineView,
                    labelVm);

        if (labelElement is not null)
        {
            try
            {
                labelElement.Focus();
            }
            catch
            {
            }
        }

        window.Activate();
        Native.SetForegroundWindow(
            new WindowInteropHelper(window).Handle);

        var targets =
            new List<(string Name, IInputElement Target)>();

        if (labelElement is not null)
            targets.Add(("LayerLabel", labelElement));

        if (Keyboard.FocusedElement is IInputElement focused)
            targets.Add(("Focused", focused));

        targets.Add(("TimelineView", timelineView));
        targets.Add(("Window", window));

        var parameters =
            new List<object?>
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
            foreach (var (targetName, target)
                     in uniqueTargets)
            {
                foreach (var parameter in parameters)
                {
                    bool can;

                    try
                    {
                        can = routed.CanExecute(
                            parameter,
                            target);
                    }
                    catch
                    {
                        continue;
                    }

                    Log(
                        $"route_can command={commandType} target={targetName} param={DescribeParameter(parameter)} can={can}");

                    if (!can)
                        continue;

                    routed.Execute(parameter, target);

                    Fact(
                        commandType + "_route",
                        $"{targetName}|{DescribeParameter(parameter)}");

                    return new RouteResult(
                        targetName,
                        DescribeParameter(parameter),
                        true);
                }
            }
        }

        throw new InvalidOperationException(
            "No executable routed structural command.");
    }

    private static async Task Exercise(
        string name,
        CommandType commandType,
        int layer,
        string expectedAfterFolder,
        string expectedDetection,
        Window window,
        FrameworkElement timelineView,
        TimelineViewModel vm,
        Timeline timeline,
        FolderState folderState,
        StructuralHistoryBridge bridge,
        Func<int> recordedCount,
        Func<int> undoCount,
        Func<int> redoCount,
        string baselineHost,
        string baselineFolder)
    {
        timeline.SelectedItems = ImmutableList<IItem>.Empty;
        timeline.LayerSelection.SelectedLayers =
            ImmutableList.Create(layer);

        await Task.Delay(100);

        Check(
            name + "_starts_host_baseline",
            Capture(timeline).Text == baselineHost);
        Check(
            name + "_starts_folder_baseline",
            folderState.Signature == baselineFolder);

        var previewBefore = bridge.PreviewCount;
        var executedBefore = bridge.ExecutedCount;
        var recordedBefore = recordedCount();

        var route = ExecuteStandardLayerCommand(
            commandType,
            window,
            timelineView,
            vm,
            timeline,
            layer);

        await WaitUntil(
            name + " forward sync",
            () =>
                bridge.PreviewCount > previewBefore
                && bridge.ExecutedCount > executedBefore
                && recordedCount() > recordedBefore
                && bridge.Last is
                {
                    Armed: true,
                    RecordedSequence: > 0,
                    ExecutedSequence: > 0
                });

        var tx = bridge.Last
            ?? throw new InvalidOperationException(
                "Missing structural history transaction.");

        var afterHost = Capture(timeline).Text;
        var afterFolder = folderState.Signature;

        Fact(name + "_after_host", afterHost);
        Fact(name + "_after_folder", afterFolder);
        Fact(
            name + "_detection",
            tx.Detection is null
                ? "<none>"
                : Describe(tx.Detection));
        Fact(
            name + "_sequence",
            $"preview:{tx.PreviewSequence},recorded:{tx.RecordedSequence},executed:{tx.ExecutedSequence}");

        Check(name + "_route_executed", route.Executed);
        Check(
            name + "_preview_before_recorded",
            tx.PreviewSequence < tx.RecordedSequence);
        Check(
            name + "_folder_after",
            afterFolder == expectedAfterFolder);
        Check(
            name + "_detection_exact",
            tx.Detection is
            {
                Status:
                    StructuralDetectionStatus.Exact
            });
        Check(
            name + "_detection_expected",
            tx.Detection is not null
            && Describe(tx.Detection)
                == "Exact:" + expectedDetection);
        Check(
            name + "_folder_command_not_undone_yet",
            tx.UndoCalls == 0);
        Check(
            name + "_folder_command_not_redone_yet",
            tx.RedoCalls == 0);

        window.Activate();
        Native.SetForegroundWindow(
            new WindowInteropHelper(window).Handle);

        var undoBefore = undoCount();
        await Native.Key(0x5A, true);

        await WaitUntil(
            name + " one-step undo",
            () =>
                undoCount() > undoBefore
                && Capture(timeline).Text == baselineHost
                && folderState.Signature == baselineFolder
                && tx.UndoCalls == 1);

        Check(
            name + "_one_undo_host",
            Capture(timeline).Text == baselineHost);
        Check(
            name + "_one_undo_folder",
            folderState.Signature == baselineFolder);
        Check(
            name + "_one_undo_folder_command_once",
            tx.UndoCalls == 1);

        var redoBefore = redoCount();
        await Native.Key(0x59, true);

        await WaitUntil(
            name + " one-step redo",
            () =>
                redoCount() > redoBefore
                && Capture(timeline).Text == afterHost
                && folderState.Signature == expectedAfterFolder
                && tx.RedoCalls == 1);

        Check(
            name + "_one_redo_host",
            Capture(timeline).Text == afterHost);
        Check(
            name + "_one_redo_folder",
            folderState.Signature == expectedAfterFolder);
        Check(
            name + "_one_redo_folder_command_once",
            tx.RedoCalls == 1);

        undoBefore = undoCount();
        await Native.Key(0x5A, true);

        await WaitUntil(
            name + " final reset",
            () =>
                undoCount() > undoBefore
                && Capture(timeline).Text == baselineHost
                && folderState.Signature == baselineFolder
                && tx.UndoCalls == 2);

        Check(
            name + "_reset_host",
            Capture(timeline).Text == baselineHost);
        Check(
            name + "_reset_folder",
            folderState.Signature == baselineFolder);
        Check(
            name + "_second_undo_folder_command_once",
            tx.UndoCalls == 2);
    }

    private static async Task Run(Window window)
    {
        EventHandler? recordedHandler = null;
        EventHandler? undoHandler = null;
        EventHandler? redoHandler = null;
        StructuralHistoryBridge? bridge = null;
        UndoRedoManager? manager = null;

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
                .Where(
                    x => x.GetType().Name
                         == "TimelineView"
                         && x.IsVisible)
                .OrderByDescending(
                    x => x.ActualWidth * x.ActualHeight)
                .First();

            var vm =
                timelineView.DataContext
                    as TimelineViewModel
                ?? throw new InvalidOperationException(
                    "Visible TimelineViewModel missing");

            var timeline =
                Host.Get(vm, "Timeline")
                    as Timeline
                ?? vm.GetType()
                    .GetField(
                        "timeline",
                        Host.Flags)?
                    .GetValue(vm)
                    as Timeline
                ?? throw new InvalidOperationException(
                    "Visible Timeline missing");

            Check(
                "visible_context_bound",
                ReferenceEquals(
                    timelineView.DataContext,
                    vm));

            var character =
                new Character
                {
                    Name = "CNWL_STRUCTURAL_UNDO_SYNC"
                };

            for (var layer = 0; layer <= 8; layer++)
            {
                var item =
                    new VoiceItem(character)
                    {
                        Frame = 20 + layer * 40,
                        Layer = layer,
                        Length = 20,
                        Serif = "D" + layer,
                        Remark =
                            "CNWL_US_DENSE_L" + layer
                    };

                if (!timeline.TryAddItems(
                        [item],
                        item.Frame,
                        item.Layer))
                {
                    throw new InvalidOperationException(
                        "Dense fixture add L" + layer);
                }
            }

            foreach (var layer in new[] { 20, 30 })
            {
                var item =
                    new VoiceItem(character)
                    {
                        Frame = 500 + layer * 10,
                        Layer = layer,
                        Length = 20,
                        Serif = "S" + layer,
                        Remark =
                            "CNWL_US_SPARSE_L" + layer
                    };

                if (!timeline.TryAddItems(
                        [item],
                        item.Frame,
                        item.Layer))
                {
                    throw new InvalidOperationException(
                        "Sparse fixture add L" + layer);
                }
            }

            timeline.SelectedItems =
                ImmutableList<IItem>.Empty;
            timeline.LayerSelection.SelectedLayers =
                ImmutableList<int>.Empty;

            await Task.Delay(600);

            var main =
                window.DataContext
                ?? throw new InvalidOperationException(
                    "MainViewModel missing");

            var model =
                main.GetType()
                    .GetField(
                        "model",
                        Host.Flags)?
                    .GetValue(main)
                ?? throw new MissingMemberException(
                    "MainViewModel.model");

            manager =
                Host.Get(
                    model,
                    "UndoRedoManager")
                    as UndoRedoManager
                ?? throw new MissingMemberException(
                    "MainModel.UndoRedoManager");

            // Separate synthetic fixture creation from the real user gestures.
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

            var folderState = new FolderState();

            bridge =
                new StructuralHistoryBridge(
                    window,
                    timeline,
                    manager,
                    folderState);

            var baselineHost = Capture(timeline).Text;
            var baselineFolder = folderState.Signature;

            Fact("baseline_host", baselineHost);
            Fact("baseline_folder", baselineFolder);

            Check(
                "fixture_count",
                Capture(timeline).Layers.Count == 11);
            Check(
                "folder_baseline_expected",
                baselineFolder
                    == "A:2-6|C:20-30");

            await Exercise(
                "add_L3",
                CommandType.AddLayer,
                3,
                "A:2-7|C:21-31",
                "I:3:1",
                window,
                timelineView,
                vm,
                timeline,
                folderState,
                bridge,
                () => recorded,
                () => undone,
                () => redone,
                baselineHost,
                baselineFolder);

            await Exercise(
                "delete_L3",
                CommandType.DeleteLayer,
                3,
                "A:2-5|C:19-29",
                "D:3:1",
                window,
                timelineView,
                vm,
                timeline,
                folderState,
                bridge,
                () => recorded,
                () => undone,
                () => redone,
                baselineHost,
                baselineFolder);

            await Exercise(
                "sparse_delete_L24",
                CommandType.DeleteLayer,
                24,
                "A:2-6|C:20-29",
                "D:24:1",
                window,
                timelineView,
                vm,
                timeline,
                folderState,
                bridge,
                () => recorded,
                () => undone,
                () => redone,
                baselineHost,
                baselineFolder);

            Check(
                "final_host_baseline",
                Capture(timeline).Text == baselineHost);
            Check(
                "final_folder_baseline",
                folderState.Signature == baselineFolder);

            Check(
                "bridge_preview_count",
                bridge.PreviewCount == 3);
            Check(
                "bridge_executed_count",
                bridge.ExecutedCount == 3);
            Check(
                "bridge_recorded_pending_count",
                bridge.RecordedWhilePendingCount == 3);

            Fact(
                "manager_recorded_total",
                recorded);
            Fact(
                "manager_undoed_total",
                undone);
            Fact(
                "manager_redoed_total",
                redone);
            Fact(
                "host_version",
                typeof(Timeline)
                    .Assembly
                    .GetName()
                    .Version);

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
                !typeof(StructuralUndoSyncProbe)
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

            Log("native structural undo sync complete");
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
        var temp =
            Path.Combine(output, "result.tmp");

        File.WriteAllLines(
            temp,
            new[]
            {
                "status="
                + (
                    failed
                        ? "FAIL_STRUCTURAL_UNDO_SYNC"
                        : "PASS_STRUCTURAL_UNDO_SYNC"),
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
