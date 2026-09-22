using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4P3ToolStateDiscovery;

public sealed class DiscoveryEntry : ILocalizePlugin
{
    public string Name => "CNWL P3 ToolState discovery";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class DiscoveryToolPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(DiscoveryToolViewModel);
    public Type ViewType => typeof(DiscoveryToolView);
    public string Name => "CNWL P3 ToolState";
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
    public int DefaultOrder => 9990;
}

public sealed class DiscoveryToolView : UserControl
{
}

public sealed class DiscoveryToolViewModel : IToolViewModel
{
    event EventHandler<CreateNewToolViewRequestedEventArgs>? IToolViewModel.CreateNewToolViewRequested { add { } remove { } }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }
    public string Title => "CNWL P3 ToolState";
    public void LoadState(ToolState stateData) { }
    public ToolState SaveState() => new() { Title = Title, SavedState = "{\"probe\":true}" };
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_P3_TOOLSTATE_DISCOVERY_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path)) return;
        scheduled = true;
        output = Path.GetFullPath(path);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static void Bootstrap()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100) throw new TimeoutException("Bootstrap");
                var window = Application.Current.Windows.Cast<Window>()
                    .FirstOrDefault(x => x.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (window is null) return;

                var root = window.DataContext!;
                var active = Get(root, "ActiveTimelineViewModel");
                if (active is null)
                {
                    root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                    return;
                }

                timer.Stop();
                Run(root, active);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Finish(new { status = "FAIL_P3_TOOLSTATE_DISCOVERY", error = ex.ToString() });
            }
        };
        timer.Start();
    }

    private static object? Get(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

    private static string MemberSummary(Type type, Func<MemberInfo, bool>? filter = null)
    {
        filter ??= _ => true;
        return string.Join(" | ",
            type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                .Where(filter)
                .OrderBy(x => x.MemberType)
                .ThenBy(x => x.Name)
                .Select(x => x switch
                {
                    MethodInfo m => $"M:{m}",
                    PropertyInfo p => $"P:{p.PropertyType.FullName} {p.Name}",
                    ConstructorInfo c => $"C:{c}",
                    _ => $"{x.MemberType}:{x.Name}"
                }));
    }

    private static object[] RuntimeEntries(object? enumerable)
    {
        if (enumerable is not System.Collections.IEnumerable values)
            return [];

        return values.Cast<object>().Select((value, index) =>
        {
            var type = value.GetType();
            var scalars = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanRead && x.GetIndexParameters().Length == 0))
            {
                try
                {
                    var v = p.GetValue(value);
                    if (v is null)
                    {
                        if (p.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("Name", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("Title", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("Header", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("Command", StringComparison.OrdinalIgnoreCase))
                            scalars[p.Name] = null;
                    }
                    else if (v is string or bool or int or Guid or Type
                        || p.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Name", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Title", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Header", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Command", StringComparison.OrdinalIgnoreCase))
                    {
                        scalars[p.Name] = v.ToString();
                    }
                }
                catch (Exception ex)
                {
                    scalars[p.Name] = "<getter:" + ex.GetType().Name + ">";
                }
            }

            return (object)new
            {
                index,
                type = type.FullName,
                text = value.ToString(),
                values = scalars,
                publicSurface = MemberSummary(type, x =>
                    x.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Item", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("State", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Title", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Header", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Name", StringComparison.OrdinalIgnoreCase))
            };
        }).ToArray();
    }

    private static void Run(object root, object activeVm)
    {
        var timeline = Get(activeVm, "Timeline") as Timeline
            ?? activeVm.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(activeVm) as Timeline
            ?? throw new InvalidOperationException("Timeline missing.");

        var idProp = timeline.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public);
        var id = idProp?.GetValue(timeline);

        var model = Get(root, "Model") ?? Get(root, "MainModel");
        var project = model is null ? null : Get(model, "Project") ?? Get(model, "CurrentProject");
        var scenes = model is null ? null : Get(model, "Scenes");

        var pluginLoaderType = typeof(IToolPlugin).Assembly.GetType("YukkuriMovieMaker.Plugin.PluginLoader");
        var toolPluginsProp = pluginLoaderType?.GetProperty("ToolPlugins", BindingFlags.Public | BindingFlags.Static);
        var toolPlugins = toolPluginsProp?.GetValue(null) as System.Collections.IEnumerable;
        var plugins = toolPlugins?.Cast<object>()
            .Select(x => new
            {
                type = x.GetType().FullName,
                name = x.GetType().GetProperty("Name")?.GetValue(x)?.ToString(),
                viewModel = x.GetType().GetProperty("ViewModelType")?.GetValue(x)?.ToString()
            })
            .ToArray() ?? [];

        var timelineScene = scenes is null ? null :
            Get(scenes, "AllScenes") is System.Collections.IEnumerable all
                ? all.Cast<object>().FirstOrDefault(x => ReferenceEquals(Get(x, "Timeline"), timeline))
                : null;

        var result = new
        {
            status = "PASS_P3_TOOLSTATE_DISCOVERY",
            hostVersion = typeof(Timeline).Assembly.GetName().Version?.ToString(),
            timeline = new
            {
                idPropertyPublic = idProp?.GetMethod?.IsPublic == true,
                idType = idProp?.PropertyType.FullName,
                idValue = id?.ToString(),
                publicIdentityMembers = MemberSummary(timeline.GetType(), x =>
                    x.Name.Contains("ID", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Name", StringComparison.OrdinalIgnoreCase))
            },
            scene = timelineScene is null ? null : new
            {
                type = timelineScene.GetType().FullName,
                identityMembers = MemberSummary(timelineScene.GetType(), x =>
                    x.Name.Contains("ID", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains("Name", StringComparison.OrdinalIgnoreCase)
                    || x.Name == "Timeline")
            },
            toolState = new
            {
                type = typeof(ToolState).FullName,
                publicSurface = MemberSummary(typeof(ToolState)),
                sample = new DiscoveryToolViewModel().SaveState()
            },
            toolPlugin = new
            {
                interfaceSurface = MemberSummary(typeof(IToolPlugin)),
                registered = plugins.Any(x => x.name == "CNWL P3 ToolState"),
                plugins
            },
            mainViewModelToolSurface = MemberSummary(root.GetType(), x =>
                x.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Plugin", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("View", StringComparison.OrdinalIgnoreCase)),
            mainViewModelProjectSurface = MemberSummary(root.GetType(), x =>
                x.Name.Contains("Project", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Load", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Open", StringComparison.OrdinalIgnoreCase)),
            toolMenuEntries = RuntimeEntries(Get(root, "ToolMenuItems")),
            anchorableAreaEntries = RuntimeEntries(Get(root, "AnchorableAreaViewModels")),
            mainModelToolSurface = model is null ? "<null>" : MemberSummary(model.GetType(), x =>
                x.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Plugin", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Project", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Load", StringComparison.OrdinalIgnoreCase)),
            projectType = project?.GetType().FullName,
            projectToolSurface = project is null ? "<null>" : MemberSummary(project.GetType(), x =>
                x.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("State", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Window", StringComparison.OrdinalIgnoreCase))
        };

        Finish(result);
    }

    private static void Finish(object result)
    {
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path.Combine(output, "result.json"), true);
    }
}
