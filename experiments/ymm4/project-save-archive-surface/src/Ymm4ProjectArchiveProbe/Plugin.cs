using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4ProjectArchiveProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Project Archive Save Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static readonly string[] Keywords = ["save", "saveas", "project", "file", "path", "open", "load", "export", "import", "copy"];
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PROJECT_ARCHIVE_DIR");
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
                    var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);

                    timer.Stop();
                    Run(root, model, active, timeline);
                    return;
                }
                if (ticks >= 90)
                {
                    timer.Stop();
                    WriteResult("FAIL_TIMEOUT", 0, 0);
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", 0, 0);
            }
        };
        timer.Start();
    }

    private static void Run(object root, object? model, object active, Timeline timeline)
    {
        Append("HOST root=" + root.GetType().FullName);
        Append("HOST model=" + (model?.GetType().FullName ?? "<null>"));
        Append("HOST active=" + active.GetType().FullName);
        Append("HOST timeline=" + timeline.GetType().FullName);

        var saveCandidates = 0;
        var pathCandidates = 0;
        (saveCandidates, pathCandidates) = Add((saveCandidates, pathCandidates), InspectObject("MainViewModel", root, 0));
        if (model != null) (saveCandidates, pathCandidates) = Add((saveCandidates, pathCandidates), InspectObject("MainModel", model, 0));
        (saveCandidates, pathCandidates) = Add((saveCandidates, pathCandidates), InspectObject("ActiveTimelineViewModel", active, 0));
        (saveCandidates, pathCandidates) = Add((saveCandidates, pathCandidates), InspectObject("Timeline", timeline, 0));

        Assert(saveCandidates > 0 || pathCandidates > 0, "at least one Project save/copy/export/path candidate is discoverable");
        WriteResult("PASS_PROJECT_ARCHIVE_SURFACE", saveCandidates, pathCandidates);
    }

    private static (int Save, int Path) InspectObject(string label, object instance, int depth)
    {
        var save = 0;
        var path = 0;
        Append("=== " + label + " :: " + instance.GetType().FullName + " ===");
        var members = instance.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => Keywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m is PropertyInfo or FieldInfo or MethodInfo)
            .OrderBy(m => m.MemberType).ThenBy(m => m.Name).Take(350).ToArray();

        foreach (var member in members)
        {
            var lname = member.Name.ToLowerInvariant();
            if (lname.Contains("save") || lname.Contains("copy") || lname.Contains("export")) save++;
            if (lname.Contains("path") || lname.Contains("file") || lname.Contains("project")) path++;

            object? value = null;
            var hasValue = false;
            try
            {
                if (member is PropertyInfo p && p.GetIndexParameters().Length == 0 && p.GetGetMethod(true) != null)
                {
                    value = p.GetValue(instance); hasValue = true;
                }
                else if (member is FieldInfo f)
                {
                    value = f.GetValue(instance); hasValue = true;
                }
            }
            catch (Exception ex)
            {
                Append("MEMBER " + Describe(member) + " value=<getter-threw:" + ex.GetBaseException().GetType().Name + ">");
                continue;
            }

            Append("MEMBER " + Describe(member) + (hasValue ? " value=" + SafeValue(value) : ""));

            if (depth < 1 && value != null && value is not string && value is not IEnumerable &&
                (lname.Contains("project") || lname.Contains("file") || lname.Contains("path")))
            {
                var vt = value.GetType();
                if (!vt.IsPrimitive && !vt.IsEnum)
                {
                    var nested = InspectObject(label + "." + member.Name, value, depth + 1);
                    save += nested.Save; path += nested.Path;
                }
            }
        }
        return (save, path);
    }

    private static (int Save, int Path) Add((int Save, int Path) a, (int Save, int Path) b) => (a.Save + b.Save, a.Path + b.Path);

    private static string Describe(MemberInfo member) => member switch
    {
        PropertyInfo p => $"PROPERTY {Access(p.GetGetMethod(true) ?? p.GetSetMethod(true))} {p.PropertyType.FullName} {p.Name} declared={p.DeclaringType?.FullName}",
        FieldInfo f => $"FIELD {(f.IsPublic ? "public" : "nonpublic")} {f.FieldType.FullName} {f.Name} declared={f.DeclaringType?.FullName}",
        MethodInfo m => $"METHOD {Access(m)} {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))}) declared={m.DeclaringType?.FullName}",
        _ => member.MemberType + " " + member.Name
    };

    private static string SafeValue(object? value)
    {
        if (value == null) return "<null>";
        if (value is string s) return '"' + (s.Length > 160 ? s[..160] + "…" : s) + '"';
        if (value is ICollection c) return $"<{value.GetType().FullName} Count={c.Count}>";
        var type = value.GetType();
        if (type.IsPrimitive || value is decimal || value is Guid || value is TimeSpan || value is Enum) return value.ToString() ?? "<null-string>";
        return "<" + type.FullName + ">";
    }

    private static string Access(MethodBase? method) => method == null ? "none" : method.IsPublic ? "public" : method.IsFamily ? "protected" : "nonpublic";

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, int saveCandidates, int pathCandidates)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "save_copy_export_candidate_count=" + saveCandidates,
            "project_file_path_candidate_count=" + pathCandidates
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
