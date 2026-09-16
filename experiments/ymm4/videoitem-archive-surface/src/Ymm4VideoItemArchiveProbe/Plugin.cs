using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4VideoItemArchiveProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — VideoItem Archive Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static readonly string[] Required = ["FilePath", "ContentOffset", "Frame", "Length", "PlaybackRate"];
    private static readonly string[] Keywords = ["file", "path", "content", "offset", "frame", "length", "playback", "rate", "speed", "source", "time"];
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_VIDEOITEM_ARCHIVE_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
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

                    var timeline = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (timeline == null) continue;

                    timer.Stop();
                    Run(timeline);
                    return;
                }

                if (ticks >= 90)
                {
                    timer.Stop();
                    WriteResult("FAIL_TIMEOUT", false, 0, false);
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", false, 0, false);
            }
        };
        timer.Start();
    }

    private static void Run(Timeline timeline)
    {
        Append($"HOST timelineAssembly={typeof(Timeline).Assembly.FullName}");
        var videoType = typeof(Timeline).Assembly.GetType("YukkuriMovieMaker.Project.Items.VideoItem", throwOnError: false);
        Assert(videoType != null, "VideoItem type exists");
        videoType!;

        Append("TYPE " + videoType.FullName);
        foreach (var ctor in videoType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(x => x.GetParameters().Length))
            Append("CTOR " + Access(ctor) + " " + ctor);

        object? instance = null;
        try { instance = Activator.CreateInstance(videoType, nonPublic: true); }
        catch (Exception ex) { Append("INSTANCE unavailable=" + ex.GetBaseException().GetType().Name + ":" + ex.GetBaseException().Message); }

        var publicReadable = 0;
        var allFound = true;
        foreach (var name in Required)
        {
            var p = FindProperty(videoType, name);
            if (p == null)
            {
                allFound = false;
                Append("REQUIRED missing=" + name);
                continue;
            }
            var getter = p.GetGetMethod(true);
            var setter = p.GetSetMethod(true);
            if (getter?.IsPublic == true) publicReadable++;
            Append($"REQUIRED name={name} type={p.PropertyType.FullName} declared={p.DeclaringType?.FullName} getter={Access(getter)} setter={Access(setter)} default={SafeGet(p, instance)}");
        }

        foreach (var p in AllProperties(videoType)
            .Where(p => Keywords.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Name))
        {
            Append($"PROPERTY name={p.Name} type={p.PropertyType.FullName} declared={p.DeclaringType?.FullName} getter={Access(p.GetGetMethod(true))} setter={Access(p.GetSetMethod(true))}");
        }

        foreach (var m in videoType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => !m.IsSpecialName && Keywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Name).Take(120))
        {
            Append($"METHOD {Access(m)} {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))}) declared={m.DeclaringType?.FullName}");
        }

        Assert(allFound, "all required archive-planning properties are discoverable");
        WriteResult("PASS_VIDEOITEM_ARCHIVE_SURFACE", true, publicReadable, instance != null);
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (p != null) return p;
        }
        return null;
    }

    private static IEnumerable<PropertyInfo> AllProperties(Type type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t != null; t = t.BaseType)
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (seen.Add(p.Name)) yield return p;
    }

    private static string SafeGet(PropertyInfo p, object? instance)
    {
        if (instance == null || p.GetIndexParameters().Length != 0 || p.GetGetMethod(true) == null) return "<not-read>";
        try
        {
            var value = p.GetValue(instance);
            if (value == null) return "<null>";
            if (value is string s) return '"' + s + '"';
            var type = value.GetType();
            return type.IsPrimitive || value is decimal || value is TimeSpan || value is Guid || value is Enum
                ? value.ToString() ?? "<null-string>"
                : "<" + type.FullName + ">";
        }
        catch (Exception ex) { return "<getter-threw:" + ex.GetBaseException().GetType().Name + ">"; }
    }

    private static string Access(MethodBase? method) => method == null ? "none" : method.IsPublic ? "public" : method.IsFamily ? "protected" : "nonpublic";

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, bool allFound, int publicReadable, bool constructible)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "all_required_members=" + allFound,
            "public_readable_required_count=" + publicReadable,
            "safe_parameterless_instance=" + constructible
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
