using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4SceneArchiveProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Scene Archive Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static readonly string[] HostKeywords = ["scene", "timeline", "project"];
    private static readonly string[] RefKeywords = ["scene", "timeline", "id", "name"];
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SCENE_ARCHIVE_DIR");
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
                    WriteResult("FAIL_TIMEOUT", 0, false, 0);
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", 0, false, 0);
            }
        };
        timer.Start();
    }

    private static void Run(object root, object? model, object active, Timeline timeline)
    {
        Append("HOST root=" + root.GetType().FullName);
        Append("HOST model=" + (model?.GetType().FullName ?? "<null>"));
        Append("HOST active=" + active.GetType().FullName);
        Append("HOST timeline=" + timeline.GetType().FullName + " name=" + timeline.Name);

        var collectionCount = 0;
        collectionCount += InspectObject("MainViewModel", root, 0);
        if (model != null) collectionCount += InspectObject("MainModel", model, 0);
        collectionCount += InspectObject("ActiveTimelineViewModel", active, 0);
        collectionCount += InspectObject("Timeline", timeline, 0);

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .ToArray();
        var sceneItemType = loaded.SelectMany(SafeTypes)
            .FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Project.Items.SceneItem")
            ?? loaded.SelectMany(SafeTypes).FirstOrDefault(t => t.Name == "SceneItem");

        Assert(sceneItemType != null, "SceneItem type exists");
        Append("SCENE_ITEM_TYPE " + sceneItemType!.AssemblyQualifiedName);
        foreach (var ctor in sceneItemType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(x => x.GetParameters().Length))
            Append("SCENE_ITEM_CTOR " + Access(ctor) + " " + ctor);

        var refs = new List<MemberInfo>();
        for (var t = sceneItemType; t != null; t = t.BaseType)
        {
            refs.AddRange(t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(x => RefKeywords.Any(k => x.Name.Contains(k, StringComparison.OrdinalIgnoreCase))));
            refs.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(x => RefKeywords.Any(k => x.Name.Contains(k, StringComparison.OrdinalIgnoreCase))));
        }
        refs = refs.GroupBy(x => x.MemberType + ":" + x.Name).Select(x => x.First()).OrderBy(x => x.Name).ToList();
        foreach (var member in refs) Append("SCENE_ITEM_REF " + Describe(member));

        foreach (var type in loaded.SelectMany(SafeTypes)
            .Where(t => (t.Name.Contains("Scene", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Timeline", StringComparison.OrdinalIgnoreCase))
                     && t.Namespace?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .OrderBy(t => t.FullName).Take(200))
            Append("CANDIDATE_TYPE " + type.FullName);

        Assert(collectionCount > 0, "at least one Project/Scene/Timeline collection-like host member is discoverable");
        Assert(refs.Count > 0, "SceneItem exposes at least one scene/timeline/id/name reference candidate");
        WriteResult("PASS_SCENE_ARCHIVE_SURFACE", collectionCount, true, refs.Count);
    }

    private static int InspectObject(string label, object instance, int depth)
    {
        var count = 0;
        Append("=== " + label + " :: " + instance.GetType().FullName + " ===");
        var members = instance.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => HostKeywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m is PropertyInfo or FieldInfo or MethodInfo)
            .OrderBy(m => m.MemberType).ThenBy(m => m.Name).Take(250).ToArray();

        foreach (var member in members)
        {
            object? value = null;
            var gotValue = false;
            try
            {
                if (member is PropertyInfo p && p.GetIndexParameters().Length == 0 && p.GetGetMethod(true) != null)
                {
                    value = p.GetValue(instance); gotValue = true;
                }
                else if (member is FieldInfo f)
                {
                    value = f.GetValue(instance); gotValue = true;
                }
            }
            catch (Exception ex) { Append("MEMBER " + Describe(member) + " value=<getter-threw:" + ex.GetBaseException().GetType().Name + ">"); continue; }

            Append("MEMBER " + Describe(member) + (gotValue ? " value=" + SafeValue(value) : ""));
            if (value is IEnumerable && value is not string)
            {
                count++;
                Append("COLLECTION_CANDIDATE owner=" + label + " member=" + member.Name + " type=" + value.GetType().FullName + " count=" + TryCount(value));
            }

            if (depth < 1 && value != null && value is not string && HostKeywords.Any(k => member.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                var vt = value.GetType();
                if (!vt.IsPrimitive && !vt.IsEnum && value is not IEnumerable)
                    count += InspectObject(label + "." + member.Name, value, depth + 1);
            }
        }
        return count;
    }

    private static string Describe(MemberInfo member) => member switch
    {
        PropertyInfo p => $"PROPERTY {Access(p.GetGetMethod(true) ?? p.GetSetMethod(true))} {p.PropertyType.FullName} {p.Name} declared={p.DeclaringType?.FullName}",
        FieldInfo f => $"FIELD {(f.IsPublic ? "public" : "nonpublic")} {f.FieldType.FullName} {f.Name} declared={f.DeclaringType?.FullName}",
        MethodInfo m => $"METHOD {Access(m)} {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))}) declared={m.DeclaringType?.FullName}",
        _ => member.MemberType + " " + member.Name
    };

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(x => x != null).Cast<Type>().ToArray(); }
    }

    private static string TryCount(object value)
    {
        if (value is ICollection c) return c.Count.ToString(CultureInfo.InvariantCulture);
        try
        {
            var n = 0;
            foreach (var _ in (IEnumerable)value) { if (++n >= 10000) break; }
            return n.ToString(CultureInfo.InvariantCulture);
        }
        catch { return "?"; }
    }

    private static string SafeValue(object? value)
    {
        if (value == null) return "<null>";
        if (value is string s) return '"' + (s.Length > 120 ? s[..120] + "…" : s) + '"';
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

    private static void WriteResult(string status, int collections, bool sceneItem, int refs)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "collection_candidate_count=" + collections,
            "scene_item_type=" + sceneItem,
            "scene_item_reference_candidate_count=" + refs
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
