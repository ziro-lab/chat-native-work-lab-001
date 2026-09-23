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

public sealed class BootstrapEntry : ILocalizePlugin
{
    public string Name => "CNWL Timeline Undo Public Route Bootstrap";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class UndoRouteTool : IToolPlugin
{
    public string Name => Probe.ToolName;
    public Type ViewModelType => typeof(UndoRouteViewModel);
    public Type ViewType => typeof(UndoRouteView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class UndoRouteView : UserControl
{
    public UndoRouteView() => Content = new TextBlock { Text = Probe.ToolName };
}

public sealed class UndoRouteViewModel : IToolViewModel, ITimelineToolViewModel
{
    public string Title => Probe.ToolName;
    public bool CanSuspend => false;
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public void SetTimelineToolInfo(TimelineToolInfo info) => Probe.Accept(info);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested
    {
        add { }
        remove { }
    }
}

internal static class Probe
{
    internal const string ToolName = "CNWL Undo Manager Public Route";

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
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
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
                        .Select(p => new
                        {
                            p.Name,
                            type = p.PropertyType.FullName,
                            publicGet = p.GetMethod?.IsPublic == true
                        }).ToArray(),
                    managerPublicMethods = managerType
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(m => !m.IsSpecialName)
                        .Select(m => new
                        {
                            m.Name,
                            returnType = m.ReturnType.FullName,
                            parameters = m.GetParameters().Select(p => p.ParameterType.FullName).ToArray()
                        })
                        .OrderBy(x => x.Name)
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
        int ticks = 0;
        bool created = false;
        bool attemptedOpen = false;

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

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
                        return;
                    }

                    if (active is null)
                        continue;

                    if (!attemptedOpen)
                    {
                        attemptedOpen = true;
                        var item = FindMenuItem(main, ToolName);
                        DumpMenuItem(item);
                        Check("tool_menu_item_found", item is not null);
                        Check("tool_menu_open_invoked", item is not null && TryInvokeMenuItem(item));
                        Log("menu open attempted");
                    }
                }

                if (ticks > 100)
                {
                    timer.Stop();
                    Write("FAIL_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE",
                        "Host did not call SetTimelineToolInfo before timeout.");
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

    static object? FindMenuItem(object main, string name)
    {
        var prop = main.GetType().GetProperty(
            "ToolMenuItems",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.GetValue(main) is not IEnumerable roots)
            return null;

        foreach (var root in roots.Cast<object>())
        {
            var found = Visit(root, name, 0);
            if (found is not null)
                return found;
        }
        return null;
    }

    static object? Visit(object item, string name, int depth)
    {
        if (depth > 8)
            return null;

        var label = GetLabel(item);
        if (label == name)
            return item;

        foreach (var propertyName in new[] { "Children", "Items" })
        {
            try
            {
                if (item.GetType().GetProperty(propertyName)?.GetValue(item) is IEnumerable children)
                {
                    foreach (var child in children.Cast<object>())
                    {
                        var found = Visit(child, name, depth + 1);
                        if (found is not null)
                            return found;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    static string? GetLabel(object item)
    {
        foreach (var name in new[] { "Header", "Title", "Name" })
        {
            try
            {
                var value = item.GetType().GetProperty(name)?.GetValue(item)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }
        }
        return null;
    }

    static bool TryInvokeMenuItem(object item)
    {
        if (item is ICommand direct)
        {
            if (direct.CanExecute(null))
            {
                direct.Execute(null);
                return true;
            }
        }

        foreach (var p in item.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try
            {
                if (p.GetIndexParameters().Length == 0 && p.GetValue(item) is ICommand command)
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

        foreach (var m in item.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.GetParameters().Length != 0)
                continue;
            if (!(m.Name.Contains("Open", StringComparison.OrdinalIgnoreCase)
               || m.Name.Contains("Execute", StringComparison.OrdinalIgnoreCase)
               || m.Name.Contains("Activate", StringComparison.OrdinalIgnoreCase)
               || m.Name.Contains("Show", StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                m.Invoke(item, null);
                return true;
            }
            catch { }
        }

        return false;
    }

    static void DumpMenuItem(object? item)
    {
        if (item is null)
            return;

        var type = item.GetType();
        File.WriteAllText(
            Path.Combine(output, "menu-item-surface.json"),
            JsonSerializer.Serialize(new
            {
                type = type.FullName,
                label = GetLabel(item),
                properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .Select(p => new
                    {
                        p.Name,
                        type = p.PropertyType.FullName,
                        publicGet = p.GetMethod?.IsPublic == true
                    }).ToArray(),
                methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => new
                    {
                        m.Name,
                        visibility = m.IsPublic ? "public" : "nonpublic",
                        parameters = m.GetParameters().Select(p => p.ParameterType.FullName).ToArray()
                    }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
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
