using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4StandardCommandTileProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Standard Command Tile Probe";
    public void SetCulture(CultureInfo cultureInfo) => CommandHost.Schedule();
}

public sealed class CommandToolPlugin : IToolPlugin
{
    public string Name => "CNWL Standard Command Tile Probe";
    public Type ViewModelType => typeof(CommandProbeViewModel);
    public Type ViewType => typeof(CommandProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class CommandProbeView : UserControl
{
    public CommandProbeView() => Content = new TextBlock { Text = "CNWL standard command tile probe", Margin = new Thickness(12) };
}

public sealed class CommandProbeViewModel : ITimelineToolViewModel, IToolViewModel, IDisposable
{
    public string Title => "CNWL Standard Command Tile Probe";
    public bool CanSuspend => false;
    public void SetTimelineToolInfo(TimelineToolInfo info) => CommandProof.Run(info.Timeline, info.UndoRedoManager);
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public void Dispose() { }
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}

internal static class CommandHost
{
    private static bool scheduled;

    public static void Schedule()
    {
        var output = Environment.GetEnvironmentVariable("CNWL_YMM4_COMMAND_TILE_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(output)) return;
        scheduled = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(() => Start(Path.GetFullPath(output))));
    }

    private static void Start(string output)
    {
        Directory.CreateDirectory(output);
        var ticks = 0;
        var projectCreated = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                    if (active == null && !projectCreated)
                    {
                        projectCreated = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        break;
                    }
                    if (active == null) continue;
                    if (OpenTool(main))
                    {
                        File.WriteAllText(Path.Combine(output, "host.txt"), "tool_open_requested=True\n", new UTF8Encoding(false));
                        timer.Stop();
                        return;
                    }
                }

                if (ticks >= 90)
                {
                    timer.Stop();
                    File.WriteAllText(Path.Combine(output, "result.txt"), "status=FAIL_TOOL_OPEN_TIMEOUT\n", new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output, "host-error.txt"), ex.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output, "result.txt"), "status=FAIL_HOST_EXCEPTION\n", new UTF8Encoding(false));
            }
        };
        timer.Start();
    }

    private static bool OpenTool(object main)
    {
        var items = main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) as IEnumerable;
        if (items == null) return false;

        bool Visit(object item, int depth)
        {
            if (depth > 8) return false;
            var type = item.GetType();
            var header = type.GetProperty("Header")?.GetValue(item)?.ToString()
                ?? type.GetProperty("Title")?.GetValue(item)?.ToString()
                ?? type.GetProperty("Name")?.GetValue(item)?.ToString() ?? "";

            if (header.Contains("CNWL Standard Command Tile Probe", StringComparison.Ordinal))
            {
                if (type.GetProperty("Command")?.GetValue(item) is ICommand command)
                {
                    var parameter = type.GetProperty("CommandParameter")?.GetValue(item);
                    if (command.CanExecute(parameter))
                    {
                        command.Execute(parameter);
                        return true;
                    }
                }

                if (type.GetProperty("ViewModelType")?.GetValue(item) is Type vmType && vmType == typeof(CommandProbeViewModel))
                {
                    type.GetProperty("IsVisible")?.SetValue(item, true);
                    type.GetProperty("IsSelected")?.SetValue(item, true);
                    type.GetProperty("IsActive")?.SetValue(item, true);
                    return true;
                }
            }

            var children = (type.GetProperty("Children")?.GetValue(item) ?? type.GetProperty("Items")?.GetValue(item)) as IEnumerable;
            if (children != null)
                foreach (var child in children)
                    if (child != null && Visit(child, depth + 1)) return true;
            return false;
        }

        foreach (var item in items)
            if (item != null && Visit(item, 0)) return true;
        return false;
    }
}

internal static class CommandProof
{
    private static bool ran;

    public static void Run(Timeline timeline, UndoRedoManager undo)
    {
        if (ran) return;
        var output = Environment.GetEnvironmentVariable("CNWL_YMM4_COMMAND_TILE_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        ran = true;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);

        var originalItems = timeline.Items;
        var originalSelection = timeline.SelectedItems;
        var frameProperty = FindFrameProperty(timeline.GetType());
        int? originalFrame = frameProperty == null ? null : (int?)frameProperty.GetValue(timeline);

        try
        {
            var mainWindow = Application.Current.MainWindow ?? Application.Current.Windows.Cast<Window>().FirstOrDefault();
            if (mainWindow == null) throw new InvalidOperationException("YMM4 MainWindow not available.");

            var testedTypes = new[]
            {
                CommandType.Undo,
                CommandType.Redo,
                CommandType.SeekToNextFrame,
                CommandType.SeekToPreviousFrame,
                CommandType.SplitCurrentPositionItems,
                CommandType.AddKeyFrameAtCurrentFrame
            };

            var routed = new Dictionary<CommandType, RoutedCommand>();
            var surface = new List<string>();
            foreach (var type in testedTypes)
            {
                var raw = CommandSettings.Default[type];
                surface.Add($"{type}.object_type={raw?.GetType().FullName ?? "<null>"}");
                surface.Add($"{type}.is_routed={raw is RoutedCommand}");
                if (raw is RoutedCommand rc) routed[type] = rc;
            }
            surface.Add("frame_property=" + (frameProperty?.Name ?? "<none>"));
            surface.Add("frame_property_public_setter=" + (frameProperty?.SetMethod?.IsPublic == true));
            File.WriteAllLines(Path.Combine(output, "command-surface.txt"), surface, new UTF8Encoding(false));

            bool allRouted = testedTypes.All(routed.ContainsKey);
            if (!allRouted || frameProperty?.SetMethod?.IsPublic != true)
            {
                WriteResult(output, "FAIL_PUBLIC_SURFACE", new Dictionary<string, object?>
                {
                    ["all_required_commands_routed"] = allRouted,
                    ["public_frame_setter"] = frameProperty?.SetMethod?.IsPublic == true
                });
                return;
            }

            // A. Native Undo / Redo through the same route used by a command tile.
            var undoFixture = new TachieFaceItem(new Character { Name = "CNWL_CommandUndo" })
            {
                Frame = 20,
                Length = 30,
                Layer = 3,
                Remark = "CNWL_UNDO_FIXTURE"
            };
            undo.Record();
            timeline.Items = timeline.Items.Add(undoFixture);
            timeline.RefreshTimelineLengthAndMaxLayer();
            undo.Record();
            Pump();

            bool undoCan = Can(routed[CommandType.Undo], mainWindow);
            if (undoCan) Execute(routed[CommandType.Undo], mainWindow);
            Pump();
            bool undoRemoved = !timeline.Items.Any(x => x.Remark == "CNWL_UNDO_FIXTURE");

            bool redoCan = Can(routed[CommandType.Redo], mainWindow);
            if (redoCan) Execute(routed[CommandType.Redo], mainWindow);
            Pump();
            bool redoRestored = timeline.Items.Any(x => x.Remark == "CNWL_UNDO_FIXTURE");

            bool cleanupUndoCan = Can(routed[CommandType.Undo], mainWindow);
            if (cleanupUndoCan) Execute(routed[CommandType.Undo], mainWindow);
            Pump();

            // B. A non-mutating standard command through the same route.
            SetFrame(timeline, frameProperty, 200);
            Pump();
            int seekStart = (int)frameProperty.GetValue(timeline)!;
            bool seekNextCan = Can(routed[CommandType.SeekToNextFrame], mainWindow);
            if (seekNextCan) Execute(routed[CommandType.SeekToNextFrame], mainWindow);
            Pump();
            int seekNext = (int)frameProperty.GetValue(timeline)!;
            bool seekPrevCan = Can(routed[CommandType.SeekToPreviousFrame], mainWindow);
            if (seekPrevCan) Execute(routed[CommandType.SeekToPreviousFrame], mainWindow);
            Pump();
            int seekBack = (int)frameProperty.GetValue(timeline)!;
            bool seekWorked = seekNextCan && seekPrevCan && seekNext == seekStart + 1 && seekBack == seekStart;

            // C. A selected-item editing command (split) through the same route.
            var splitFixture = new TachieFaceItem(new Character { Name = "CNWL_CommandSplit" })
            {
                Frame = 300,
                Length = 100,
                Layer = 5,
                Remark = "CNWL_SPLIT_FIXTURE"
            };
            undo.Record();
            timeline.Items = timeline.Items.Add(splitFixture);
            timeline.SelectedItems = ImmutableList.Create<IItem>(splitFixture);
            SetFrame(timeline, frameProperty, 340);
            timeline.RefreshTimelineLengthAndMaxLayer();
            undo.Record();
            Pump();

            int splitBefore = timeline.Items.Count(x => x.Remark == "CNWL_SPLIT_FIXTURE");
            bool splitCan = Can(routed[CommandType.SplitCurrentPositionItems], mainWindow);
            if (splitCan) Execute(routed[CommandType.SplitCurrentPositionItems], mainWindow);
            Pump();
            int splitAfter = timeline.Items.Count(x => x.Remark == "CNWL_SPLIT_FIXTURE");
            bool splitWorked = splitCan && splitBefore == 1 && splitAfter == 2;

            bool splitUndoCan = Can(routed[CommandType.Undo], mainWindow);
            if (splitUndoCan) Execute(routed[CommandType.Undo], mainWindow);
            Pump();
            int splitAfterUndo = timeline.Items.Count(x => x.Remark == "CNWL_SPLIT_FIXTURE");
            bool splitUndoWorked = splitUndoCan && splitAfterUndo == 1;

            // D. Midpoint command: record routability / executable state and dispatch if available.
            var currentSplit = timeline.Items.FirstOrDefault(x => x.Remark == "CNWL_SPLIT_FIXTURE");
            bool midpointCan = false;
            bool midpointExecuted = false;
            bool midpointUndoAvailableAfter = false;
            if (currentSplit != null)
            {
                timeline.SelectedItems = ImmutableList.Create(currentSplit);
                SetFrame(timeline, frameProperty, currentSplit.Frame + Math.Max(1, currentSplit.Length / 2));
                Pump();
                midpointCan = Can(routed[CommandType.AddKeyFrameAtCurrentFrame], mainWindow);
                if (midpointCan)
                {
                    Execute(routed[CommandType.AddKeyFrameAtCurrentFrame], mainWindow);
                    Pump();
                    midpointExecuted = true;
                    midpointUndoAvailableAfter = Can(routed[CommandType.Undo], mainWindow);
                    if (midpointUndoAvailableAfter)
                    {
                        Execute(routed[CommandType.Undo], mainWindow);
                        Pump();
                    }
                }
            }

            bool pass = allRouted
                && undoCan && undoRemoved
                && redoCan && redoRestored
                && seekWorked
                && splitWorked
                && splitUndoWorked;

            WriteResult(output, pass ? "PASS_STANDARD_COMMAND_TILE_ROUTE" : "FAIL_ASSERTION",
                new Dictionary<string, object?>
                {
                    ["all_required_commands_routed"] = allRouted,
                    ["undo_can_execute"] = undoCan,
                    ["undo_removed_fixture"] = undoRemoved,
                    ["redo_can_execute"] = redoCan,
                    ["redo_restored_fixture"] = redoRestored,
                    ["seek_next_can_execute"] = seekNextCan,
                    ["seek_previous_can_execute"] = seekPrevCan,
                    ["seek_start_frame"] = seekStart,
                    ["seek_next_frame"] = seekNext,
                    ["seek_back_frame"] = seekBack,
                    ["seek_roundtrip_worked"] = seekWorked,
                    ["split_can_execute"] = splitCan,
                    ["split_count_before"] = splitBefore,
                    ["split_count_after"] = splitAfter,
                    ["split_worked"] = splitWorked,
                    ["split_undo_can_execute"] = splitUndoCan,
                    ["split_count_after_undo"] = splitAfterUndo,
                    ["split_undo_worked"] = splitUndoWorked,
                    ["midpoint_is_routed_command"] = routed.ContainsKey(CommandType.AddKeyFrameAtCurrentFrame),
                    ["midpoint_can_execute"] = midpointCan,
                    ["midpoint_dispatched_without_exception"] = midpointExecuted,
                    ["midpoint_undo_available_after_dispatch"] = midpointUndoAvailableAfter
                });
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
            WriteResult(output, "FAIL_EXCEPTION", new Dictionary<string, object?>
            {
                ["exception"] = ex.GetBaseException().GetType().FullName,
                ["message"] = ex.GetBaseException().Message
            });
        }
        finally
        {
            try
            {
                timeline.Items = originalItems;
                timeline.SelectedItems = originalSelection;
                if (originalFrame.HasValue && frameProperty?.SetMethod?.IsPublic == true)
                    frameProperty.SetValue(timeline, originalFrame.Value);
                timeline.RefreshTimelineLengthAndMaxLayer();
            }
            catch { }
        }
    }

    private static PropertyInfo? FindFrameProperty(Type type)
    {
        var exact = type.GetProperty("CurrentFrame", BindingFlags.Instance | BindingFlags.Public);
        if (exact?.PropertyType == typeof(int) && exact.CanRead) return exact;
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x => x.PropertyType == typeof(int) && x.CanRead
                && x.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase)
                && x.Name.Contains("Current", StringComparison.OrdinalIgnoreCase));
    }

    private static void SetFrame(Timeline timeline, PropertyInfo prop, int frame)
    {
        if (prop.SetMethod?.IsPublic != true) throw new InvalidOperationException($"Public frame setter unavailable: {prop.Name}");
        prop.SetValue(timeline, frame);
    }

    private static bool Can(RoutedCommand command, Window mainWindow)
    {
        CommandManager.InvalidateRequerySuggested();
        Pump();
        return command.CanExecute(null, mainWindow);
    }

    private static void Execute(RoutedCommand command, Window mainWindow)
    {
        command.Execute(null, mainWindow);
    }

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void WriteResult(string output, string status, IReadOnlyDictionary<string, object?> values)
    {
        var lines = new List<string> { "status=" + status };
        lines.AddRange(values.Select(x => x.Key + "=" + (x.Value?.ToString() ?? "<null>")));
        File.WriteAllLines(Path.Combine(output, "result.txt"), lines, new UTF8Encoding(false));
    }
}
