using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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

public sealed class TrackCStructuralEntry : ILocalizePlugin
{
    public string Name => "CNWL Track C structural integration";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static readonly Guid A =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid C =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly FolderRange[] BaselineFolders =
    [
        new FolderRange(A, 1, 5),
        new FolderRange(B, 2, 4),
        new FolderRange(C, 6, 8)
    ];

    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static readonly List<string> facts = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_TRACK_C_STRUCTURAL_DIR");
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
                    throw new TimeoutException("Bootstrap");

                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName !=
                        "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var vm = Host.Get(main, "ActiveTimelineViewModel");
                    if (vm is null && !created)
                    {
                        created = true;
                        main.GetType()
                            .GetMethod("CreateProject", Type.EmptyTypes)?
                            .Invoke(main, null);
                        return;
                    }

                    if (vm is null)
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

    private static string IdName(Guid id) =>
        id == A ? "A" : id == B ? "B" : id == C ? "C" : "?";

    private static string FolderText(IEnumerable<FolderRange> ranges) =>
        string.Join(
            "|",
            ranges
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End)
                .Select(x => $"{IdName(x.Id)}:{x.Start}-{x.End}"));

    private static CollapsedSpan[] ToSpans(IEnumerable<FolderRange> ranges) =>
        ranges
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.End)
            .Select(x => new CollapsedSpan(x.Start, x.End))
            .ToArray();

    private sealed record HostSnapshot(
        Dictionary<IItem, int> Layers,
        HashSet<int> EmptySettingLayers,
        string Text);

    private static HostSnapshot Capture(Timeline timeline)
    {
        var layers = new Dictionary<IItem, int>(
            ReferenceEqualityComparer.Instance);

        foreach (var item in timeline.Items)
        {
            if (item.Remark?.StartsWith(
                "CNWL_CSTRUCT_",
                StringComparison.Ordinal) == true)
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
                .OrderBy(x => x.Key.Remark, StringComparer.Ordinal)
                .Select(x =>
                    $"{x.Key.Remark}@L{x.Value}:F{x.Key.Frame}"));

        return new HostSnapshot(
            layers,
            emptySettings,
            text);
    }

    private static StructuralDetection Detect(
        HostSnapshot before,
        HostSnapshot after,
        int commandLayer)
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
            insertedHints: insertedHints,
            operationPositionHints: new HashSet<int> { commandLayer });
    }

    private static string EditText(StructuralEdit edit) =>
        edit switch
        {
            InsertLayers x => $"I:{x.Position}:{x.Count}",
            DeleteLayers x => $"D:{x.Position}:{x.Count}",
            SwapAdjacentLayers x => $"S:{x.FirstLayer}",
            _ => edit.GetType().Name
        };

    private static string DetectionText(StructuralDetection detection) =>
        detection.Status + ":" +
        string.Join("|", detection.Edits.Select(EditText));

    private static async Task WaitUntil(
        string name,
        Func<bool> predicate,
        int timeoutMs = 9000)
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

    private static async Task Reveal(
        Host host,
        double top)
    {
        var current =
            (Rect)(Host.Reactive(host.Vm, "Viewport")
                ?? throw new InvalidOperationException("Viewport"));

        Host.SetReactive(
            host.Vm,
            "Viewport",
            new Rect(
                new Point(current.X, top),
                current.Size));

        await Task.Delay(350);
    }

    private static async Task Sample(
        Host host,
        DirectDisplay display,
        string name)
    {
        display.Phase = name;
        await Task.Delay(350);
        display.ThrowIfFailed();

        Check(name + "_geometry", display.GeometryMatches());
        Check(
            name + "_extent",
            Math.Abs(
                host.Scroll.ExtentHeight
                - display.ExpectedExtent) < 1.5);
        Check(name + "_no_reentry", display.Reentries == 0);
        Check(
            name + "_no_hidden_view",
            !host.ItemViews().Any(
                x =>
                    Host.Item(x.DataContext) is IItem item
                    && display.Layout.IsHidden(item.Layer)));
    }

    private static async Task ClickAndRight(
        Host host,
        DirectDisplay display,
        IItem target,
        string name)
    {
        var h = display.Height;
        await Reveal(
            host,
            display.Layout.VisualRowOfLogical(target.Layer) * h);

        host.Timeline.SelectedItems =
            ImmutableList<IItem>.Empty;

        await Native.Click(host.Center(target));

        Check(
            name + "_click",
            host.Timeline.SelectedItems.Any(
                x => ReferenceEquals(x, target)));

        var p = host.OffsetScreen(
            host.Center(target),
            65,
            0);

        await Native.Click(p, true);

        var position =
            Host.Reactive(
                host.Vm,
                "TimelineCursorPositionWhenRightClick");

        Check(
            name + "_right_cursor",
            position is Point point
            && (int)Math.Floor(point.Y / h) == target.Layer);

        Check(
            name + "_right_converter",
            position is Point mapped
            && Host.ConverterLayer(mapped, h) == target.Layer);

        await Native.Key(0x1B);
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
            .OrderByDescending(
                x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private sealed record RouteResult(
        string Target,
        string Parameter);

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
            try { labelElement.Focus(); }
            catch { }
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

        var parameters = new List<object?>
        {
            null,
            layer,
            labelVm,
            timeline
        };

        foreach (var (targetName, target) in
            targets
                .GroupBy(
                    x => x.Target,
                    ReferenceEqualityComparer.Instance)
                .Select(x => x.First()))
        {
            if (command is not RoutedCommand routed)
                continue;

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

                if (!can)
                    continue;

                routed.Execute(parameter, target);
                return new RouteResult(
                    targetName,
                    DescribeParameter(parameter));
            }
        }

        throw new InvalidOperationException(
            "No executable route for " + commandType);
    }

    private sealed record PreviewFact(
        CommandType CommandType,
        int Layer,
        StructuralEdit Edit,
        string Before,
        string After,
        bool Changed);

    private sealed class StructuralFolderBridge : IDisposable
    {
        private readonly FrameworkElement root;
        private readonly UndoRedoManager manager;
        private readonly DirectDisplay display;
        private readonly ExecutedRoutedEventHandler previewHandler;

        public StructuralFolderBridge(
            FrameworkElement root,
            UndoRedoManager manager,
            DirectDisplay display,
            IEnumerable<FolderRange> initial)
        {
            this.root = root;
            this.manager = manager;
            this.display = display;
            State = initial.ToArray();
            ApplyDisplay();
            previewHandler = OnPreview;
            root.AddHandler(
                CommandManager.PreviewExecutedEvent,
                previewHandler,
                true);
        }

        public FolderRange[] State { get; private set; }
        public PreviewFact? Last { get; private set; }
        public int AddedUndoCommands { get; private set; }
        public int UndoCallbacks { get; private set; }
        public int RedoCallbacks { get; private set; }

        public void ResetLast() => Last = null;

        private static bool TryEdit(
            ICommand command,
            int layer,
            out CommandType type,
            out StructuralEdit? edit)
        {
            foreach (var candidate in new[]
            {
                CommandType.AddLayer,
                CommandType.DeleteLayer,
                CommandType.MoveUpLayer,
                CommandType.MoveDownLayer
            })
            {
                var configured =
                    CommandSettings.Default[candidate];

                if (configured is null
                    || !ReferenceEquals(command, configured))
                    continue;

                type = candidate;
                edit = candidate switch
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

            type = default;
            edit = null;
            return false;
        }

        private void ApplyState(
            IReadOnlyList<FolderRange> state,
            string reason)
        {
            State = state.ToArray();
            ApplyDisplay();
            Log(
                $"folder_apply reason={reason} state={FolderText(State)}");
        }

        private void ApplyDisplay() =>
            display.SetSpans(ToSpans(State));

        private void OnPreview(
            object sender,
            ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is not int layer
                || !TryEdit(
                    e.Command,
                    layer,
                    out var type,
                    out var edit)
                || edit is null)
                return;

            var before = State.ToArray();
            var plan =
                FolderRangeTracker.Apply(before, edit);
            var after = plan.Ranges.ToArray();
            var changed = !before.SequenceEqual(after);

            Last = new PreviewFact(
                type,
                layer,
                edit,
                FolderText(before),
                FolderText(after),
                changed);

            Log(
                $"preview type={type} layer={layer} edit={EditText(edit)} before={Last.Before} after={Last.After} changed={changed}");

            if (!changed)
                return;

            ApplyState(after, "preview-forward");

            manager.AddCommand(
                new UndoRedoActionCommand(
                    () =>
                    {
                        UndoCallbacks++;
                        ApplyState(before, "undo");
                    },
                    () =>
                    {
                        RedoCallbacks++;
                        ApplyState(after, "redo");
                    }));

            AddedUndoCommands++;
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
        string expectedEdit,
        Host host,
        TimelineViewModel vm,
        DirectDisplay display,
        StructuralFolderBridge bridge,
        IItem target,
        string timelineBaseline,
        string folderBaseline,
        Func<int> recordedCount,
        Func<int> undoCount,
        Func<int> redoCount)
    {
        Check(
            name + "_starts_timeline_baseline",
            Capture(host.Timeline).Text == timelineBaseline);
        Check(
            name + "_starts_folder_baseline",
            FolderText(bridge.State) == folderBaseline);

        var before = Capture(host.Timeline);
        var addBefore = bridge.AddedUndoCommands;
        var undoCbBefore = bridge.UndoCallbacks;
        var redoCbBefore = bridge.RedoCallbacks;
        var recordedBefore = recordedCount();

        bridge.ResetLast();

        host.Timeline.SelectedItems =
            ImmutableList<IItem>.Empty;
        host.Timeline.LayerSelection.SelectedLayers =
            ImmutableList.Create(layer);

        var route = ExecuteStandardLayerCommand(
            commandType,
            host.Window,
            host.View,
            vm,
            host.Timeline,
            layer);

        await WaitUntil(
            name + " forward",
            () =>
                recordedCount() > recordedBefore
                && Capture(host.Timeline).Text != timelineBaseline
                && FolderText(bridge.State) == expectedFolderAfter);

        var after = Capture(host.Timeline);
        var preview = bridge.Last
            ?? throw new InvalidOperationException(
                name + " preview missing");

        var detected =
            Detect(before, after, layer);

        Fact(
            name + "_route",
            $"{route.Target}|{route.Parameter}");
        Fact(
            name + "_predicted",
            EditText(preview.Edit));
        Fact(
            name + "_detected",
            DetectionText(detected));
        Fact(
            name + "_folder_after",
            FolderText(bridge.State));

        Check(
            name + "_predicted_edit",
            EditText(preview.Edit) == expectedEdit);
        Check(
            name + "_detected_exact",
            detected.Status ==
                StructuralDetectionStatus.Exact);
        Check(
            name + "_detected_matches",
            detected.Edits.Count == 1
            && EditText(detected.Edits[0]) == expectedEdit);
        Check(
            name + "_folder_forward_exact",
            FolderText(bridge.State) ==
                expectedFolderAfter);
        Check(
            name + "_single_record",
            recordedCount() - recordedBefore == 1);
        Check(
            name + "_undo_command_count",
            bridge.AddedUndoCommands - addBefore ==
                (preview.Changed ? 1 : 0));

        await Sample(
            host,
            display,
            name + "_forward_display");
        var timelineAfter = after.Text;

        Fact(
            name + "_recorded_before_undo_delta",
            recordedCount() - recordedBefore);

        host.Activate();
        await Task.Delay(100);
        var undoBefore = undoCount();
        await Native.Key(0x5A, true);

        await WaitUntil(
            name + " undo",
            () =>
                undoCount() > undoBefore
                && Capture(host.Timeline).Text ==
                    timelineBaseline
                && FolderText(bridge.State) ==
                    folderBaseline);

        Fact(
            name + "_undo_timeline",
            Capture(host.Timeline).Text);
        Fact(
            name + "_undo_folder",
            FolderText(bridge.State));

        Check(
            name + "_undo_event_one",
            undoCount() - undoBefore == 1);
        Check(
            name + "_undo_callbacks",
            bridge.UndoCallbacks - undoCbBefore ==
                (preview.Changed ? 1 : 0));

        await Sample(
            host,
            display,
            name + "_undo_display");

        host.Activate();
        await Task.Delay(100);
        var redoBefore = redoCount();
        await Native.Key(0x59, true);

        await WaitUntil(
            name + " redo",
            () =>
                redoCount() > redoBefore
                && Capture(host.Timeline).Text ==
                    timelineAfter
                && FolderText(bridge.State) ==
                    expectedFolderAfter);

        Check(
            name + "_redo_event_one",
            redoCount() - redoBefore == 1);
        Check(
            name + "_redo_callbacks",
            bridge.RedoCallbacks - redoCbBefore ==
                (preview.Changed ? 1 : 0));

        await Sample(
            host,
            display,
            name + "_redo_display");

        host.Activate();
        await Task.Delay(100);
        undoBefore = undoCount();
        await Native.Key(0x5A, true);

        await WaitUntil(
            name + " reset",
            () =>
                undoCount() > undoBefore
                && Capture(host.Timeline).Text ==
                    timelineBaseline
                && FolderText(bridge.State) ==
                    folderBaseline);

        await Sample(
            host,
            display,
            name + "_reset_display");
    }

    private static async Task Run(Window window)
    {
        DirectDisplay? display = null;
        InputMapAdapter? input = null;
        FileDropMapAdapter? fileDrop = null;
        StructuralFolderBridge? bridge = null;
        UndoRedoManager? manager = null;
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
            await Task.Delay(1000);

            var view = Host.Elements(window)
                .Where(x =>
                    x.GetType().Name == "TimelineView"
                    && x.IsVisible)
                .OrderByDescending(
                    x => x.ActualWidth * x.ActualHeight)
                .First();

            var vm = view.DataContext as TimelineViewModel
                ?? throw new InvalidOperationException(
                    "Visible TimelineViewModel missing");

            var timeline =
                Host.Get(vm, "Timeline") as Timeline
                ?? vm.GetType()
                    .GetField("timeline", Host.Flags)?
                    .GetValue(vm) as Timeline
                ?? throw new InvalidOperationException(
                    "Visible Timeline missing");

            var scroll = Host.Elements(view)
                .OfType<ScrollViewer>()
                .Where(x =>
                    x.IsVisible
                    && x.ActualHeight > 50)
                .OrderByDescending(
                    x => x.ActualWidth * x.ActualHeight)
                .First();

            var host = new Host(
                window,
                timeline,
                vm,
                view,
                scroll.Content as FrameworkElement
                    ?? throw new InvalidOperationException(
                        "Timeline content missing"),
                scroll);

            host.Activate();

            Check(
                "visible_context_bound",
                ReferenceEquals(
                    vm,
                    view.DataContext));

            var character = new Character
            {
                Name = "CNWL_CSTRUCT"
            };

            VoiceItem Make(
                int layer,
                int frame) =>
                new(character)
                {
                    Frame = frame,
                    Layer = layer,
                    Length = 26,
                    Serif = "L" + layer,
                    Remark = "CNWL_CSTRUCT_L" + layer
                };

            var items = new List<IItem>();

            for (var layer = 0; layer <= 12; layer++)
            {
                var item = Make(
                    layer,
                    20 + layer * 35);

                if (!timeline.TryAddItems(
                    [item],
                    item.Frame,
                    item.Layer))
                {
                    throw new InvalidOperationException(
                        "Fixture add L" + layer);
                }

                items.Add(item);
            }

            var target = items[6];

            timeline.SelectedItems =
                ImmutableList<IItem>.Empty;
            await Task.Delay(700);

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

            manager.Record();

            var recorded = 0;
            var undone = 0;
            var redone = 0;

            recordedHandler = (_, _) => recorded++;
            undoHandler = (_, _) => undone++;
            redoHandler = (_, _) => redone++;

            manager.Recorded += recordedHandler;
            manager.Undoed += undoHandler;
            manager.Redoed += redoHandler;

            display = new DirectDisplay(
                host,
                Log);

            bridge = new StructuralFolderBridge(
                window,
                manager,
                display,
                BaselineFolders);

            input = new InputMapAdapter(
                host,
                display,
                Log);
            fileDrop = new FileDropMapAdapter(
                host,
                display,
                Log);

            var timelineBaseline =
                Capture(timeline).Text;
            var folderBaseline =
                FolderText(bridge.State);

            Fact(
                "timeline_baseline",
                timelineBaseline);
            Fact(
                "folder_baseline",
                folderBaseline);

            Check(
                "folder_baseline_exact",
                folderBaseline ==
                    "A:1-5|B:2-4|C:6-8");

            await Sample(
                host,
                display,
                "baseline_display");
            await ClickAndRight(
                host,
                display,
                target,
                "baseline_input");

            await Exercise(
                "add_L3",
                CommandType.AddLayer,
                3,
                "A:1-6|B:2-5|C:7-9",
                "I:3:1",
                host,
                vm,
                display,
                bridge,
                target,
                timelineBaseline,
                folderBaseline,
                () => recorded,
                () => undone,
                () => redone);

            await Exercise(
                "delete_L3",
                CommandType.DeleteLayer,
                3,
                "A:1-4|B:2-3|C:5-7",
                "D:3:1",
                host,
                vm,
                display,
                bridge,
                target,
                timelineBaseline,
                folderBaseline,
                () => recorded,
                () => undone,
                () => redone);

            await Exercise(
                "move_down_L3",
                CommandType.MoveDownLayer,
                3,
                folderBaseline,
                "S:3",
                host,
                vm,
                display,
                bridge,
                target,
                timelineBaseline,
                folderBaseline,
                () => recorded,
                () => undone,
                () => redone);

            Check(
                "final_timeline_baseline",
                Capture(timeline).Text ==
                    timelineBaseline);
            Check(
                "final_folder_baseline",
                FolderText(bridge.State) ==
                    folderBaseline);

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
                "bridge_added_undo_commands",
                bridge.AddedUndoCommands);
            Fact(
                "bridge_undo_callbacks",
                bridge.UndoCallbacks);
            Fact(
                "bridge_redo_callbacks",
                bridge.RedoCallbacks);
            Fact(
                "host_version",
                typeof(Timeline)
                    .Assembly
                    .GetName()
                    .Version);

            Check(
                "bridge_added_expected",
                bridge.AddedUndoCommands == 2);
            Check(
                "bridge_undo_callbacks_expected",
                bridge.UndoCallbacks == 4);
            Check(
                "bridge_redo_callbacks_expected",
                bridge.RedoCallbacks == 2);

            // Input mapping is checked after the strict history sequences so any
            // host-owned selection/context history cannot sit above the structural
            // transaction being measured.
            var postInputBefore = Capture(timeline);
            var postInputRecordedBefore = recorded;
            bridge.ResetLast();
            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            timeline.LayerSelection.SelectedLayers = ImmutableList.Create(3);

            ExecuteStandardLayerCommand(
                CommandType.AddLayer,
                window,
                view,
                vm,
                timeline,
                3);

            await WaitUntil(
                "post_structural_input forward",
                () =>
                    recorded > postInputRecordedBefore
                    && Capture(timeline).Text != postInputBefore.Text
                    && FolderText(bridge.State) ==
                        "A:1-6|B:2-5|C:7-9");

            await Sample(
                host,
                display,
                "post_structural_input_display");
            await ClickAndRight(
                host,
                display,
                target,
                "post_structural_input");

            Check(
                "post_structural_input_preview",
                bridge.Last is not null
                && EditText(bridge.Last.Edit) == "I:3:1");
            Fact(
                "post_structural_input_recorded_after_click",
                recorded - postInputRecordedBefore);

            // P1.5b: keep the structurally-mutated Add L3 state active:
            // folders A1..6 / B2..5 / C7..9. The original L6 item is now the
            // visible C owner at L7. One folded display row down maps to L10.
            var structuralFolderState = FolderText(bridge.State);
            Check(
                "p15b_structural_folder_state",
                structuralFolderState ==
                    "A:1-6|B:2-5|C:7-9");

            var dragBefore = (target.Layer, target.Frame);
            Check(
                "p15b_drag_starts_L7",
                dragBefore.Layer == 7);

            await Reveal(
                host,
                display.Layout.VisualRowOfLogical(target.Layer)
                    * display.Height);
            timeline.SelectedItems =
                ImmutableList<IItem>.Empty;

            var correctionBefore = input.Corrections;
            Log("phase=p15b_structural_drag");
            var dragStart = host.Center(target);

            await Native.Drag(
                dragStart,
                host.OffsetScreen(
                    dragStart,
                    32,
                    display.Height));

            await Sample(
                host,
                display,
                "p15b_drag_forward");

            var dragAfter = (target.Layer, target.Frame);
            Fact(
                "p15b_drag_after",
                $"L{dragAfter.Layer}:F{dragAfter.Frame}");
            Fact(
                "p15b_drag_frame_delta",
                dragAfter.Frame - dragBefore.Frame);

            Check(
                "p15b_drag_L7_to_L10",
                dragAfter.Layer == 10
                && input.Corrections > correctionBefore);
            Check(
                "p15b_drag_native_frame_changed",
                dragAfter.Frame > dragBefore.Frame);
            Check(
                "p15b_drag_folder_unchanged",
                FolderText(bridge.State) ==
                    structuralFolderState);

            host.Activate();
            await Task.Delay(100);
            var dragUndoBefore = undone;
            await Native.Key(0x5A, true);
            await WaitUntil(
                "p15b drag undo",
                () =>
                    undone > dragUndoBefore
                    && (target.Layer, target.Frame) ==
                        dragBefore
                    && FolderText(bridge.State) ==
                        structuralFolderState);

            Check(
                "p15b_drag_undo_exact",
                (target.Layer, target.Frame) ==
                    dragBefore);
            Check(
                "p15b_drag_undo_folder_unchanged",
                FolderText(bridge.State) ==
                    structuralFolderState);

            host.Activate();
            await Task.Delay(100);
            var dragRedoBefore = redone;
            await Native.Key(0x59, true);
            await WaitUntil(
                "p15b drag redo",
                () =>
                    redone > dragRedoBefore
                    && (target.Layer, target.Frame) ==
                        dragAfter
                    && FolderText(bridge.State) ==
                        structuralFolderState);

            Check(
                "p15b_drag_redo_exact",
                (target.Layer, target.Frame) ==
                    dragAfter);

            host.Activate();
            await Task.Delay(100);
            dragUndoBefore = undone;
            await Native.Key(0x5A, true);
            await WaitUntil(
                "p15b drag reset",
                () =>
                    undone > dragUndoBefore
                    && (target.Layer, target.Frame) ==
                        dragBefore
                    && FolderText(bridge.State) ==
                        structuralFolderState);

            await Sample(
                host,
                display,
                "p15b_drag_history");

            // Real FileDrop after structural mutation. Use the original L9 item,
            // now visible at L10 after Add L3.
            var dropAnchor = items[9];
            Check(
                "p15b_drop_anchor_L10",
                dropAnchor.Layer == 10);

            await Reveal(
                host,
                display.Layout.VisualRowOfLogical(
                    dropAnchor.Layer)
                    * display.Height);

            timeline.SelectedItems =
                ImmutableList<IItem>.Empty;

            var anchorBox =
                Host.ScreenRect(
                    host.ItemView(dropAnchor));
            var scrollBox =
                Host.ScreenRect(host.Scroll);
            var dropPoint = new Point(
                Math.Min(
                    scrollBox.Right - 36,
                    anchorBox.Right + 72),
                anchorBox.Y
                    + anchorBox.Height / 2);

            var enterBefore = fileDrop.DragEnterCount;
            var overBefore = fileDrop.DragOverCount;
            var dropBeforeCount = fileDrop.DropCount;
            var addFileBefore =
                fileDrop.AddCommandExecutions;
            var correctedBefore =
                fileDrop.PostCorrectedItems;

            Log("phase=p15b_structural_filedrop");
            var dropObservation =
                await IntegratedFileDropHarness.DropPng(
                    window,
                    host,
                    dropPoint,
                    output,
                    "track-c-structural");

            await Sample(
                host,
                display,
                "p15b_filedrop_added");

            Check(
                "p15b_filedrop_drag_enter",
                fileDrop.DragEnterCount > enterBefore);
            Check(
                "p15b_filedrop_drag_over",
                fileDrop.DragOverCount > overBefore);
            Check(
                "p15b_filedrop_drop",
                fileDrop.DropCount ==
                    dropBeforeCount + 1);
            Check(
                "p15b_filedrop_add_command",
                fileDrop.AddCommandExecutions ==
                    addFileBefore + 1);
            Check(
                "p15b_filedrop_post_corrected",
                fileDrop.PostCorrectedItems >
                    correctedBefore);
            Check(
                "p15b_filedrop_logical_L10",
                fileDrop.LastLogicalLayer == 10);
            Check(
                "p15b_filedrop_added_item",
                dropObservation.AddedItems.Length > 0);
            Check(
                "p15b_filedrop_added_layer",
                dropObservation.AddedItems.All(
                    x => x.Layer == 10));
            Check(
                "p15b_filedrop_folder_unchanged",
                FolderText(bridge.State) ==
                    structuralFolderState);

            var dropPath =
                dropObservation.FilePath;
            var addedRefs =
                dropObservation.AddedItems.ToArray();

            var dropUndoBefore = undone;
            host.Activate();
            await Task.Delay(100);
            await Native.Key(0x5A, true);
            await WaitUntil(
                "p15b filedrop undo",
                () =>
                    undone > dropUndoBefore
                    && !timeline.Items.Any(
                        x =>
                            addedRefs.Any(
                                y => ReferenceEquals(x, y))
                            || string.Equals(
                                IntegratedFileDropHarness
                                    .FilePathOf(x),
                                dropPath,
                                StringComparison
                                    .OrdinalIgnoreCase))
                    && FolderText(bridge.State) ==
                        structuralFolderState);

            await Sample(
                host,
                display,
                "p15b_filedrop_undo");
            Check(
                "p15b_filedrop_undo_removed",
                !timeline.Items.Any(
                    x =>
                        addedRefs.Any(
                            y => ReferenceEquals(x, y))
                        || string.Equals(
                            IntegratedFileDropHarness
                                .FilePathOf(x),
                            dropPath,
                            StringComparison
                                .OrdinalIgnoreCase)));
            Check(
                "p15b_filedrop_undo_folder_unchanged",
                FolderText(bridge.State) ==
                    structuralFolderState);

            var dropRedoBefore = redone;
            host.Activate();
            await Task.Delay(100);
            await Native.Key(0x59, true);
            await WaitUntil(
                "p15b filedrop redo event",
                () => redone > dropRedoBefore);

            await Task.Delay(700);

            var redoDrop =
                timeline.Items.FirstOrDefault(
                    x =>
                        addedRefs.Any(
                            y => ReferenceEquals(x, y))
                        || string.Equals(
                            IntegratedFileDropHarness
                                .FilePathOf(x),
                            dropPath,
                            StringComparison
                                .OrdinalIgnoreCase));

            await Sample(
                host,
                display,
                "p15b_filedrop_redo");

            Check(
                "p15b_filedrop_redo_restored",
                redoDrop is not null);
            Check(
                "p15b_filedrop_redo_layer",
                redoDrop?.Layer == 10);
            Check(
                "p15b_filedrop_redo_folder_unchanged",
                FolderText(bridge.State) ==
                    structuralFolderState);

            dropUndoBefore = undone;
            host.Activate();
            await Task.Delay(100);
            await Native.Key(0x5A, true);
            await WaitUntil(
                "p15b filedrop reset",
                () =>
                    undone > dropUndoBefore
                    && !timeline.Items.Any(
                        x =>
                            addedRefs.Any(
                                y => ReferenceEquals(x, y))
                            || string.Equals(
                                IntegratedFileDropHarness
                                    .FilePathOf(x),
                                dropPath,
                                StringComparison
                                    .OrdinalIgnoreCase)));

            await Sample(
                host,
                display,
                "p15b_filedrop_reset");
            Check(
                "p15b_filedrop_reset_folder_unchanged",
                FolderText(bridge.State) ==
                    structuralFolderState);

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
                !typeof(Probe)
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

            Log("P1.5a Track C structural integration complete");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                fileDrop?.Dispose();
                input?.Dispose();
                bridge?.Dispose();
                display?.Dispose();

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
                        ? "FAIL_TRACK_C_STRUCTURAL"
                        : "PASS_TRACK_C_STRUCTURAL"),
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
