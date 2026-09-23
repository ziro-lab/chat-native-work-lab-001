using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4TimelineToolUndoManagerPublicRouteProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Timeline Undo Public Route";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class ControlTool : IToolPlugin
{
    public string Name => Probe.ControlToolName;
    public Type ViewModelType => typeof(ControlVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class TimelineUndoTool : IToolPlugin
{
    public string Name => Probe.ToolName;
    public Type ViewModelType => typeof(TimelineUndoVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class ProbeView : UserControl
{
    public ProbeView() => Content = new TextBlock { Text = "CNWL timeline undo route" };
}

public sealed class ControlVm : IToolViewModel
{
    public string Title => Probe.ControlToolName;
    public bool CanSuspend => false;
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}

// Public reference implementations use ITimelineToolViewModel directly;
// do not add IToolViewModel unless the host requires it.
public sealed class TimelineUndoVm : ITimelineToolViewModel
{
    public void SetTimelineToolInfo(TimelineToolInfo info) => Probe.Accept(info);
}

internal static class Probe
{
    internal const string ToolName = "CNWL Undo Manager Public Route";
    internal const string ControlToolName = "CNWL Undo Public Control";

    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_TIMELINE_UNDO_PUBLIC_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    internal static void Accept(TimelineToolInfo info)
    {
        try
        {
            Log("SetTimelineToolInfo received");
            var manager = info.UndoRedoManager;

            Check("host_set_timeline_tool_info_called", true);
            Check("timeline_info_timeline_nonnull", info.Timeline is not null);
            Check("timeline_info_undo_manager_nonnull", manager is not null);

            var managerType = manager?.GetType() ?? typeof(UndoRedoManager);
            bool PublicMethod(string name) => managerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Any(m => m.Name == name);

            Check("undo_manager_add_command_public", PublicMethod("AddCommand"));
            Check("undo_manager_record_public", PublicMethod("Record"));
            Check("undo_manager_undo_async_public", PublicMethod("UndoAsync"));
            Check("undo_manager_redo_async_public", PublicMethod("RedoAsync"));

            File.WriteAllText(
                Path.Combine(output, "observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    timelineToolInfoType = info.GetType().FullName,
                    timelineType = info.Timeline?.GetType().FullName,
                    undoRedoManagerType = manager?.GetType().FullName,
                    infoProperties = info.GetType()
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(p => new { p.Name, type = p.PropertyType.FullName, publicGet = p.GetMethod?.IsPublic == true })
                        .ToArray()
                }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE", ex.ToString());
        }
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
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = main.GetType().GetProperty(
                        "ActiveTimelineViewModel",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(main);

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        continue;
                    }
                    if (active is null)
                        continue;

                    var prop = main.GetType().GetProperty(
                        "ToolMenuItems",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop?.GetValue(main) is not IEnumerable items)
                        continue;

                    var found = new Dictionary<string, object>();
                    foreach (var item in items.Cast<object>())
                        Visit(item, found, 0);

                    if (found.ContainsKey(ControlToolName))
                        Log("control tool observed");

                    if (!found.TryGetValue(ToolName, out var target))
                        continue;

                    Log("target tool menu item observed");

                    if (!openAttempted)
                    {
                        openAttempted = true;
                        var invoked = TryInvoke(target);
                        Log("target invoke=" + invoked);
                    }
                }

                if (ticks >= 100)
                {
                    timer.Stop();
                    Write("FAIL_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE",
                        "Host did not deliver TimelineToolInfo before timeout.");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE", ex.ToString());
            }
        };

        timer.Start();
    }

    static void Visit(object item, Dictionary<string, object> found, int depth)
    {
        if (depth > 8)
            return;

        var type = item.GetType();
        string? label = null;
        foreach (var name in new[] { "Header", "Title", "Name" })
        {
            try
            {
                var value = type.GetProperty(name)?.GetValue(item)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    label = value;
                    break;
                }
            }
            catch { }
        }

        if (label == ToolName || label == ControlToolName)
            found[label] = item;

        foreach (var childName in new[] { "Children", "Items" })
        {
            try
            {
                if (type.GetProperty(childName)?.GetValue(item) is IEnumerable children)
                    foreach (var child in children.Cast<object>())
                        Visit(child, found, depth + 1);
            }
            catch { }
        }
    }

    static bool TryInvoke(object item)
    {
        if (item is ICommand direct && direct.CanExecute(null))
        {
            direct.Execute(null);
            return true;
        }

        foreach (var property in item.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try
            {
                if (property.GetIndexParameters().Length == 0 && property.GetValue(item) is ICommand command)
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
                schema = "cnwl.timeline-tool-undo-manager-public-route.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
