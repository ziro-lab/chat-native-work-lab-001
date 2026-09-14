using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SelectionProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Selection Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        SelectionSurfaceProbe.Schedule(cultureInfo.Name);
    }
}

public sealed class SelectionToolPlugin : IToolPlugin
{
    public string Name => "CNWL Selection Probe";
    public Type ViewModelType => typeof(SelectionProbeViewModel);
    public Type ViewType => typeof(SelectionProbeView);
    public bool AllowMultipleInstances => false;
}

public sealed class SelectionProbeView : UserControl
{
    public SelectionProbeView()
    {
        Content = new TextBlock
        {
            Text = "Chat Native Work Lab — Timeline Selection Probe",
            Margin = new Thickness(12)
        };
    }
}

public sealed class SelectionProbeViewModel : ITimelineToolViewModel, IToolViewModel, IDisposable
{
    public string Title => "CNWL Selection Probe";
    public bool CanSuspend => false;

    public void SetTimelineToolInfo(TimelineToolInfo info) => SelectionSurfaceProbe.ObserveToolInfo(info);
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public void Dispose() { }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}

internal static class SelectionSurfaceProbe
{
    private static readonly string[] Keywords = ["select", "selection", "current", "cursor", "frame", "item", "timeline"];
    private static bool scheduled;
    private static bool toolInfoSeen;
    private static bool toolOpenRequested;
    private static bool fixtureInserted;
    private static bool surfaceDumped;
    private static string output = "";
    private static string culture = "";
    private static object? activeTimelineViewModel;

    public static void Schedule(string cultureName)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SELECTION_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        culture = cultureName;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    public static void ObserveToolInfo(TimelineToolInfo info)
    {
        toolInfoSeen = true;
        Append("TOOL_INFO_INSTANCE " + info.GetType().FullName);
        DumpMembers("TimelineToolInfo instance", info, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static void Start()
    {
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
                    var root = window.DataContext;
                    if (root?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;

                    var active = root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
                    if (active == null && !projectCreated)
                    {
                        projectCreated = true;
                        root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                        break;
                    }
                    if (active == null) continue;

                    activeTimelineViewModel = active;
                    var timeline = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (timeline == null) continue;

                    if (!fixtureInserted)
                    {
                        InsertFixture(timeline);
                        fixtureInserted = true;
                    }

                    if (!surfaceDumped)
                    {
                        Append("HOST culture=" + culture);
                        Append("ACTIVE_TIMELINE_VM " + active.GetType().FullName);
                        DumpMembers("ActiveTimelineViewModel", active, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        DumpTypeMembers("TimelineToolInfo type", typeof(TimelineToolInfo));
                        DumpCandidateTypes(typeof(Timeline).Assembly, "YukkuriMovieMaker");
                        DumpCandidateTypes(typeof(TimelineToolInfo).Assembly, "YukkuriMovieMaker.Plugin");
                        surfaceDumped = true;
                    }

                    if (!toolOpenRequested)
                    {
                        toolOpenRequested = true;
                        Append("OPEN_TOOL requested=" + OpenTool(root));
                    }

                    if (toolInfoSeen && fixtureInserted && surfaceDumped)
                    {
                        timer.Stop();
                        WriteResult("PASS_DISCOVERY");
                        return;
                    }
                }

                if (ticks >= 90)
                {
                    timer.Stop();
                    WriteResult("FAIL_TIMEOUT");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION");
            }
        };
        timer.Start();
    }

    private static void InsertFixture(Timeline timeline)
    {
        if (timeline.Items.Any(x => x.Remark == "CNWL_SELECTION_FIXTURE")) return;
        var character = new Character { Name = "CNWL_SelectA" };
        var voice = new VoiceItem(character)
        {
            Frame = 120,
            Length = 40,
            Layer = 10,
            Serif = "selection probe voice",
            Remark = "CNWL_SELECTION_FIXTURE"
        };
        var face = new TachieFaceItem(character)
        {
            Frame = 120,
            Length = 40,
            Layer = 20,
            Remark = "CNWL_SELECTION_FIXTURE"
        };
        if (!timeline.TryAddItems([voice], voice.Frame, voice.Layer)) throw new InvalidOperationException("Could not insert VoiceItem fixture.");
        if (!timeline.TryAddItems([face], face.Frame, face.Layer)) throw new InvalidOperationException("Could not insert TachieFaceItem fixture.");
        File.WriteAllText(Path.Combine(output, "timeline-fixture.txt"),
            $"VoiceItem character={voice.CharacterName} frame={voice.Frame} length={voice.Length} layer={voice.Layer}\n" +
            $"TachieFaceItem character={face.CharacterName} frame={face.Frame} length={face.Length} layer={face.Layer}\n",
            new UTF8Encoding(false));
        Append("FIXTURE inserted VoiceItem + TachieFaceItem");
    }

    private static bool OpenTool(object main)
    {
        var items = main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) as IEnumerable;
        if (items == null) return false;

        bool Visit(object item, int depth)
        {
            if (depth > 6) return false;
            var type = item.GetType();
            var header = type.GetProperty("Header")?.GetValue(item)?.ToString()
                ?? type.GetProperty("Title")?.GetValue(item)?.ToString()
                ?? type.GetProperty("Name")?.GetValue(item)?.ToString()
                ?? "";

            if (header.Contains("CNWL Selection Probe", StringComparison.Ordinal))
            {
                if (type.GetProperty("Command")?.GetValue(item) is ICommand command)
                {
                    var parameter = type.GetProperty("CommandParameter")?.GetValue(item);
                    if (command.CanExecute(parameter)) { command.Execute(parameter); return true; }
                }
                if (type.GetProperty("ViewModelType")?.GetValue(item) is Type vmType && vmType == typeof(SelectionProbeViewModel))
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

    private static void DumpTypeMembers(string title, Type type)
    {
        Append("=== " + title + " :: " + type.FullName + " ===");
        foreach (var member in Relevant(type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            Append(DescribeMember(member, null));
    }

    private static void DumpMembers(string title, object instance, BindingFlags flags)
    {
        Append("=== " + title + " :: " + instance.GetType().FullName + " ===");
        foreach (var member in Relevant(instance.GetType().GetMembers(flags)))
            Append(DescribeMember(member, instance));
    }

    private static IEnumerable<MemberInfo> Relevant(IEnumerable<MemberInfo> members) => members
        .Where(x => Keywords.Any(k => x.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
        .Where(x => x is not MethodInfo m || !m.IsSpecialName)
        .OrderBy(x => x.MemberType)
        .ThenBy(x => x.Name)
        .Take(250);

    private static string DescribeMember(MemberInfo member, object? instance)
    {
        try
        {
            return member switch
            {
                PropertyInfo p => $"PROPERTY {Access(p.GetMethod ?? p.SetMethod)} {p.PropertyType.FullName} {p.Name} read={p.CanRead} write={p.CanWrite} value={SafeValue(instance != null && p.CanRead && p.GetIndexParameters().Length == 0 ? p.GetValue(instance) : null)}",
                FieldInfo f => $"FIELD {(f.IsPublic ? "public" : "nonpublic")} {f.FieldType.FullName} {f.Name} value={SafeValue(instance != null ? f.GetValue(instance) : null)}",
                EventInfo e => $"EVENT {Access(e.AddMethod)} {e.EventHandlerType?.FullName} {e.Name}",
                MethodInfo m => $"METHOD {(m.IsPublic ? "public" : "nonpublic")} {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))})",
                _ => member.MemberType + " " + member.Name
            };
        }
        catch (Exception ex)
        {
            return member.MemberType + " " + member.Name + " value=<getter threw " + ex.GetBaseException().GetType().Name + ">";
        }
    }

    private static string Access(MethodInfo? method) => method?.IsPublic == true ? "public" : "nonpublic";

    private static string SafeValue(object? value)
    {
        if (value == null) return "<null>";
        if (value is string s) return '"' + (s.Length > 80 ? s[..80] + "…" : s) + '"';
        if (value is ICollection collection) return $"<{value.GetType().FullName} Count={collection.Count}>";
        var type = value.GetType();
        if (type.IsPrimitive || value is decimal || value is Guid || value is Enum) return value.ToString() ?? "<null-string>";
        return "<" + type.FullName + ">";
    }

    private static void DumpCandidateTypes(Assembly assembly, string label)
    {
        Append("=== CANDIDATE TYPES " + label + " :: " + assembly.GetName().Name + " ===");
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).Cast<Type>().ToArray(); }
        foreach (var type in types.Where(t => Keywords.Any(k => t.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).OrderBy(t => t.FullName).Take(200))
            Append("TYPE " + type.FullName);
    }

    private static void WriteResult(string status)
    {
        var candidateCount = 0;
        var publicCandidateCount = 0;
        if (activeTimelineViewModel != null)
        {
            var members = Relevant(activeTimelineViewModel.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).ToArray();
            candidateCount = members.Length;
            publicCandidateCount = members.Count(IsPublic);
        }
        var lines = new[]
        {
            "status=" + status,
            "tool_info_received=" + toolInfoSeen,
            "fixture_inserted=" + fixtureInserted,
            "surface_dumped=" + surfaceDumped,
            "selection_related_member_count=" + candidateCount,
            "public_selection_related_member_count=" + publicCandidateCount
        };
        File.WriteAllLines(Path.Combine(output, "result.txt"), lines, new UTF8Encoding(false));
    }

    private static bool IsPublic(MemberInfo member) => member switch
    {
        PropertyInfo p => (p.GetMethod ?? p.SetMethod)?.IsPublic == true,
        FieldInfo f => f.IsPublic,
        EventInfo e => e.AddMethod?.IsPublic == true,
        MethodInfo m => m.IsPublic,
        _ => false
    };

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "selection-surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
