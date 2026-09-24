using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4VoiceControllerLifecycleSignalsProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Voice Controller Lifecycle Bootstrap";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class LifecycleTool : IToolPlugin
{
    public string Name => Probe.ToolName;
    public Type ViewModelType => typeof(LifecycleViewModel);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName =>
        YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class ProbeView : UserControl
{
    public ProbeView() => Content = new TextBlock { Text = Probe.ToolName };
}

public sealed class LifecycleViewModel : ITimelineToolViewModel
{
    public void SetTimelineToolInfo(TimelineToolInfo info) => Probe.Accept(info);
}

internal static class Probe
{
    internal const string ToolName = "CNWL Voice Controller Lifecycle";
    const string Remark = "CNWL_VOICE_CONTROLLER_SIGNAL";

    static bool scheduled;
    static bool running;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICE_CONTROLLER_SIGNAL_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(Start),
            DispatcherPriority.ApplicationIdle);
    }

    internal static void Accept(TimelineToolInfo info)
    {
        if (running || File.Exists(Path.Combine(output, "result.json")))
            return;

        running = true;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunAsync(info)),
            DispatcherPriority.ApplicationIdle);
    }

    static async Task RunAsync(TimelineToolInfo info)
    {
        var timeline = info.Timeline;
        var manager = info.UndoRedoManager;

        EventHandler? recordedHandler = null;
        EventHandler? undoHandler = null;
        EventHandler? redoHandler = null;
        PropertyChangedEventHandler? timelinePropertyHandler = null;
        EventHandler<UndoRedoEventArgs>? timelineUndoCommandHandler = null;

        try
        {
            Check("host_timeline_info_received", true);
            Check("timeline_nonnull", timeline is not null);
            Check("undo_manager_nonnull", manager is not null);

            if (timeline is null || manager is null)
                throw new InvalidOperationException("Host TimelineToolInfo incomplete.");

            var initial = CountProbeVoices(timeline);
            Check("initial_probe_count_zero", initial == 0);

            int recorded = 0;
            int undoed = 0;
            int redoed = 0;
            int timelineUndoCommands = 0;
            var timelineProperties = new List<string>();

            recordedHandler = (_, _) =>
            {
                recorded++;
                Log($"manager-recorded {recorded} count={CountProbeVoices(timeline)}");
            };
            undoHandler = (_, _) =>
            {
                undoed++;
                Log($"manager-undoed {undoed} count={CountProbeVoices(timeline)}");
            };
            redoHandler = (_, _) =>
            {
                redoed++;
                Log($"manager-redoed {redoed} count={CountProbeVoices(timeline)}");
            };
            timelinePropertyHandler = (_, e) =>
            {
                timelineProperties.Add(e.PropertyName ?? "<null>");
                Log("timeline-property " + (e.PropertyName ?? "<null>"));
            };
            timelineUndoCommandHandler = (_, _) =>
            {
                timelineUndoCommands++;
                Log("timeline-undo-command " + timelineUndoCommands);
            };

            manager.Recorded += recordedHandler;
            manager.Undoed += undoHandler;
            manager.Redoed += redoHandler;
            timeline.PropertyChanged += timelinePropertyHandler;
            timeline.UndoRedoCommandCreated += timelineUndoCommandHandler;

            var character = new Character { Name = "CNWL Controller Signal" };
            var voice = new VoiceItem(character)
            {
                Serif = "signal",
                Hatsuon = "signal",
                Remark = Remark
            };

            var recordedBefore = recorded;
            Check("try_add_voice_succeeded",
                timeline.TryAddItems([voice], 120, 5));
            manager.Record();

            await WaitUntil(
                "add recorded",
                () => recorded > recordedBefore && CountProbeVoices(timeline) == 1);

            Check("add_recorded_event_observed",
                recorded - recordedBefore == 1);
            Check("rescan_sees_added_voice",
                CountProbeVoices(timeline) == 1);

            var undoBefore = undoed;
            await manager.UndoAsync();
            await WaitUntil(
                "add undo",
                () => undoed > undoBefore && CountProbeVoices(timeline) == 0);

            Check("undoed_event_removes_voice",
                undoed - undoBefore == 1 && CountProbeVoices(timeline) == 0);

            var redoBefore = redoed;
            await manager.RedoAsync();
            await WaitUntil(
                "add redo",
                () => redoed > redoBefore && CountProbeVoices(timeline) == 1);

            Check("redoed_event_restores_voice",
                redoed - redoBefore == 1 && CountProbeVoices(timeline) == 1);

            var current = timeline.Items
                .OfType<VoiceItem>()
                .Where(x => x.Remark == Remark)
                .ToArray();
            Check("restored_voice_resolved_by_rescan", current.Length == 1);

            recordedBefore = recorded;
            timeline.DeleteItems(current);
            manager.Record();

            await WaitUntil(
                "delete recorded",
                () => recorded > recordedBefore && CountProbeVoices(timeline) == 0);

            Check("delete_recorded_event_observed",
                recorded - recordedBefore == 1);
            Check("rescan_sees_deleted_voice",
                CountProbeVoices(timeline) == 0);

            undoBefore = undoed;
            await manager.UndoAsync();
            await WaitUntil(
                "delete undo",
                () => undoed > undoBefore && CountProbeVoices(timeline) == 1);

            Check("delete_undo_restores_voice",
                undoed - undoBefore == 1 && CountProbeVoices(timeline) == 1);

            Check("timeline_undo_command_traffic_observed",
                timelineUndoCommands > 0);

            File.WriteAllText(
                Path.Combine(output, "lifecycle-signals-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    events = new
                    {
                        recorded,
                        undoed,
                        redoed,
                        timelineUndoCommands,
                        timelineProperties
                    },
                    finalProbeVoiceCount = CountProbeVoices(timeline),
                    publicSurface = new
                    {
                        timelineType = timeline.GetType().FullName,
                        managerType = manager.GetType().FullName,
                        timelineUndoRedoCommandCreatedPublic =
                            typeof(Timeline).GetEvent(
                                "UndoRedoCommandCreated",
                                BindingFlags.Instance | BindingFlags.Public) is not null,
                        managerRecordedPublic =
                            typeof(UndoRedoManager).GetEvent(
                                "Recorded",
                                BindingFlags.Instance | BindingFlags.Public) is not null,
                        managerUndoedPublic =
                            typeof(UndoRedoManager).GetEvent(
                                "Undoed",
                                BindingFlags.Instance | BindingFlags.Public) is not null,
                        managerRedoedPublic =
                            typeof(UndoRedoManager).GetEvent(
                                "Redoed",
                                BindingFlags.Instance | BindingFlags.Public) is not null
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_VOICE_CONTROLLER_LIFECYCLE_SIGNALS", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICE_CONTROLLER_LIFECYCLE_SIGNALS", ex.ToString());
        }
        finally
        {
            if (timeline is not null)
            {
                if (timelinePropertyHandler is not null)
                    timeline.PropertyChanged -= timelinePropertyHandler;
                if (timelineUndoCommandHandler is not null)
                    timeline.UndoRedoCommandCreated -= timelineUndoCommandHandler;
            }

            if (manager is not null)
            {
                if (recordedHandler is not null)
                    manager.Recorded -= recordedHandler;
                if (undoHandler is not null)
                    manager.Undoed -= undoHandler;
                if (redoHandler is not null)
                    manager.Redoed -= redoHandler;
            }
        }
    }

    static int CountProbeVoices(Timeline timeline) =>
        timeline.Items.OfType<VoiceItem>().Count(x => x.Remark == Remark);

    static async Task WaitUntil(
        string name,
        Func<bool> condition,
        int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException(name);
    }

    static void Start()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        int ticks = 0;
        bool created = false;
        bool openAttempted = false;

        timer.Tick += (_, _) =>
        {
            try
            {
                if (File.Exists(Path.Combine(output, "result.json")))
                {
                    timer.Stop();
                    return;
                }

                ticks++;
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName !=
                        "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = main.GetType().GetProperty(
                        "ActiveTimelineViewModel",
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic)?.GetValue(main);

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod(
                            "CreateProject",
                            Type.EmptyTypes)?.Invoke(main, null);
                        continue;
                    }
                    if (active is null)
                        continue;

                    if (openAttempted)
                        continue;

                    var prop = main.GetType().GetProperty(
                        "ToolMenuItems",
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic);
                    if (prop?.GetValue(main) is not IEnumerable items)
                        continue;

                    foreach (var item in items.Cast<object>())
                    {
                        var target = FindTool(item, 0);
                        if (target is null)
                            continue;

                        openAttempted = true;
                        var invoked = TryInvoke(target);
                        Log("tool invoke=" + invoked);
                        break;
                    }
                }

                if (ticks >= 100)
                {
                    timer.Stop();
                    Write(
                        "FAIL_VOICE_CONTROLLER_LIFECYCLE_SIGNALS",
                        "Host did not deliver TimelineToolInfo before timeout.");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICE_CONTROLLER_LIFECYCLE_SIGNALS", ex.ToString());
            }
        };

        timer.Start();
    }

    static object? FindTool(object item, int depth)
    {
        if (depth > 8)
            return null;

        var type = item.GetType();
        foreach (var name in new[] { "Header", "Title", "Name" })
        {
            try
            {
                var label = type.GetProperty(name)?.GetValue(item)?.ToString();
                if (label == ToolName)
                    return item;
            }
            catch { }
        }

        foreach (var childName in new[] { "Children", "Items" })
        {
            try
            {
                if (type.GetProperty(childName)?.GetValue(item)
                    is IEnumerable children)
                {
                    foreach (var child in children.Cast<object>())
                    {
                        var found = FindTool(child, depth + 1);
                        if (found is not null)
                            return found;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    static bool TryInvoke(object item)
    {
        if (item is ICommand direct && direct.CanExecute(null))
        {
            direct.Execute(null);
            return true;
        }

        foreach (var p in item.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try
            {
                if (p.GetIndexParameters().Length == 0
                    && p.GetValue(item) is ICommand command)
                {
                    foreach (var parameter in new object?[] { null, item })
                    {
                        if (command.CanExecute(parameter))
                        {
                            command.Execute(parameter);
                            return true;
                        }
                    }
                }
            }
            catch { }
        }

        return false;
    }

    static void Log(string text) =>
        File.AppendAllText(
            Path.Combine(output, "progress.txt"),
            DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voice-controller-lifecycle-signals.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
