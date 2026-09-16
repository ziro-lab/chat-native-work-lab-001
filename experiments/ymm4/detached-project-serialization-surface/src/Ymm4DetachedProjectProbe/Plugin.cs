using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4DetachedProjectProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Detached Project Serialization Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly string[] Keywords = ["save", "write", "serial", "json", "file", "project", "copy", "clone", "load", "read", "deserial"];

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_DETACHED_PROJECT_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        var ticks = 0;
        var projectCreated = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += async (_, _) =>
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
                    var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);
                    if (timeline == null || model == null) continue;
                    timer.Stop();
                    await RunAsync(model, timeline);
                    return;
                }
                if (ticks >= 100)
                {
                    timer.Stop();
                    WriteResult("FAIL_TIMEOUT", 0, 0, "");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", 0, 0, ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object model, Timeline timeline)
    {
        timeline.Name = "CNWL_DETACHED_PROJECT_MARKER";
        var modelType = model.GetType();
        var save = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var load = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var source = Path.Combine(output, "detached-source.ymmp");
        save.Invoke(model, [source]);
        Assert(File.Exists(source), "source project file created");

        var task = load.Invoke(model, [source]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var detached = task.GetType().GetProperty("Result")?.GetValue(task) ?? throw new InvalidOperationException("LoadProjectFile returned null Project.");
        var projectType = detached.GetType();
        Append("PROJECT_TYPE " + projectType.AssemblyQualifiedName);

        Append("=== PROJECT MEMBERS ===");
        foreach (var member in projectType.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => Keywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.MemberType).ThenBy(m => m.Name))
            Append("PROJECT_MEMBER " + Describe(member));

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true)
            .ToArray();

        var jsonType = assemblies.SelectMany(SafeTypes).FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Json.Json");
        Assert(jsonType != null, "YukkuriMovieMaker.Json.Json type exists");
        Append("JSON_TYPE " + jsonType!.AssemblyQualifiedName);
        var jsonMethods = jsonType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length).ToArray();
        var jsonProperties = jsonType.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(p => p.Name).ToArray();
        var jsonFields = jsonType.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(f => f.Name).ToArray();
        Append("=== JSON HELPER MEMBERS ===");
        foreach (var p in jsonProperties) Append("JSON_MEMBER " + Describe(p));
        foreach (var f in jsonFields) Append("JSON_MEMBER " + Describe(f));
        foreach (var m in jsonMethods) Append("JSON_MEMBER " + Describe(m));

        var serializerCandidates = jsonMethods.Where(m => Keywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToArray();
        Append("=== JSON SERIALIZATION CANDIDATES ===");
        foreach (var m in serializerCandidates) Append("JSON_CANDIDATE " + Describe(m));
        Assert(serializerCandidates.Length > 0, "Json helper exposes at least one serialization/read/write candidate");

        var methodCandidates = new List<MethodBase>();
        var typeCandidates = new List<Type>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeTypes(assembly))
            {
                if (type.Namespace?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) != true) continue;
                if (Keywords.Any(k => type.Name.Contains(k, StringComparison.OrdinalIgnoreCase))) typeCandidates.Add(type);

                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var nameInteresting = Keywords.Any(k => method.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
                    var projectRelated = method.ReturnType == projectType || method.GetParameters().Any(p => p.ParameterType == projectType);
                    if (nameInteresting && projectRelated) methodCandidates.Add(method);
                }
                foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (ctor.GetParameters().Any(p => p.ParameterType == projectType)) methodCandidates.Add(ctor);
                }
            }
        }

        Append("=== DIRECT PROJECT SERIALIZATION CANDIDATES ===");
        foreach (var method in methodCandidates.Distinct().OrderBy(m => m.DeclaringType?.FullName).ThenBy(m => m.Name)) Append("METHOD_CANDIDATE " + Describe(method));

        Assert(ContainsTimelineMarker(detached, "CNWL_DETACHED_PROJECT_MARKER"), "detached Project contains saved timeline marker");
        WriteResult("PASS_DETACHED_PROJECT_SERIALIZATION_DISCOVERY", methodCandidates.Distinct().Count(), serializerCandidates.Length, projectType.FullName ?? "");
    }

    private static bool ContainsTimelineMarker(object root, string marker)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Visit(root, 0);
        bool Visit(object? value, int depth)
        {
            if (value == null || depth > 6 || value is string) return false;
            if (value is Timeline timeline && timeline.Name == marker) return true;
            var type = value.GetType();
            if (!type.IsValueType && !visited.Add(value)) return false;
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable) if (Visit(item, depth + 1)) return true;
                return false;
            }
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length != 0 || p.GetMethod == null || p.PropertyType == typeof(string) || p.PropertyType.IsPrimitive || p.PropertyType.IsEnum) continue;
                object? next;
                try { next = p.GetValue(value); } catch { continue; }
                if (Visit(next, depth + 1)) return true;
            }
            return false;
        }
    }

    private static string Describe(MemberInfo member) => member switch
    {
        MethodInfo m => $"METHOD access={(m.IsPublic ? "public" : "nonpublic")} static={m.IsStatic} generic={m.IsGenericMethodDefinition} {m.ReturnType.FullName} {m.DeclaringType?.FullName}::{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))})",
        ConstructorInfo c => $"CTOR access={(c.IsPublic ? "public" : "nonpublic")} {c.DeclaringType?.FullName}({string.Join(",", c.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))})",
        PropertyInfo p => $"PROPERTY access={(p.GetMethod?.IsPublic == true ? "public" : "nonpublic")} static={p.GetMethod?.IsStatic == true} {p.PropertyType.FullName} {p.Name}",
        FieldInfo f => $"FIELD access={(f.IsPublic ? "public" : "nonpublic")} static={f.IsStatic} {f.FieldType.FullName} {f.Name}",
        _ => member.MemberType + " " + member.DeclaringType?.FullName + "::" + member.Name
    };

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, int directProjectMethods, int jsonCandidates, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=" + status, "direct_project_method_candidate_count=" + directProjectMethods, "json_candidate_count=" + jsonCandidates, "detail=" + detail], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
