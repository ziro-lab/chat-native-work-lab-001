using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4ProjectSaveCopyProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Project Save Copy Roundtrip";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PROJECT_SAVE_COPY_DIR");
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
                    await RunAsync(root, model, timeline);
                    return;
                }
                if (ticks >= 100)
                {
                    timer.Stop();
                    WriteResult("FAIL_TIMEOUT", false, false, false, false, false, false, "");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", false, false, false, false, false, false, ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object root, object model, Timeline timeline)
    {
        var source = Path.Combine(output, "source.ymmp");
        var archive = Path.Combine(output, "archive.ymmp");
        timeline.Name = "CNWL_ARCHIVE_ROUNDTRIP_MARKER";

        var rootType = root.GetType();
        var modelType = model.GetType();
        var save = rootType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainViewModel.SaveProject(string) missing.");
        var keep = rootType.GetProperty("KeepProjectPath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainViewModel.KeepProjectPath missing.");
        var modelPath = modelType.GetProperty("ProjectFilePath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.ProjectFilePath missing.");
        var savedState = modelType.GetProperty("IsProjectFileSaved", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.IsProjectFileSaved missing.");
        var changePath = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "ChangeProjectPath" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainModel.ChangeProjectPath(string) missing.");
        var load = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainModel.LoadProjectFile(string) missing.");

        Append("CALL SaveProject(source)");
        save.Invoke(root, [source]);
        Assert(File.Exists(source), "source project was created");
        var activeAfterSource = modelPath.GetValue(model) as string ?? "";
        Append("PATH afterSource=" + activeAfterSource);
        Assert(SamePath(activeAfterSource, source), "active project path points to source after initial save");
        var sourceHashBefore = Hash(source);
        Append("HASH sourceBefore=" + sourceHashBefore);

        Append("CALL LoadProjectFile(source) for detached surface inspection");
        var sourceLoadTask = load.Invoke(model, [source]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await sourceLoadTask;
        var detached = sourceLoadTask.GetType().GetProperty("Result")?.GetValue(sourceLoadTask)
            ?? throw new InvalidOperationException("LoadProjectFile(source) returned null Project.");
        DumpDetachedSaveSurface(detached);

        var oldKeep = (bool)(keep.GetValue(root) ?? false);
        var keepPreservedSource = false;
        try
        {
            keep.SetValue(root, true);
            Append("CALL KeepProjectPath=true; SaveProject(archive)");
            save.Invoke(root, [archive]);
            Assert(File.Exists(archive), "archive project was created");
            var activeAfterArchive = modelPath.GetValue(model) as string ?? "";
            Append("PATH afterArchive=" + activeAfterArchive);
            keepPreservedSource = SamePath(activeAfterArchive, source);
            Append("OBSERVE KeepProjectPath preservedSource=" + keepPreservedSource);
        }
        finally
        {
            keep.SetValue(root, oldKeep);
            Append("CALL ChangeProjectPath(source) in finally");
            changePath.Invoke(model, [source]);
        }

        var activeAfterRestore = modelPath.GetValue(model) as string ?? "";
        Append("PATH afterRestore=" + activeAfterRestore);
        var restoredSourcePath = SamePath(activeAfterRestore, source);
        Assert(restoredSourcePath, "explicit ChangeProjectPath restored the source project path");
        var isSaved = (bool)(savedState.GetValue(model) ?? false);
        Append("OBSERVE IsProjectFileSavedAfterPathRestore=" + isSaved);

        var sourceHashAfter = Hash(source);
        var archiveHash = Hash(archive);
        Append("HASH sourceAfter=" + sourceHashAfter);
        Append("HASH archive=" + archiveHash);
        var sourceUnchanged = string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.OrdinalIgnoreCase);
        Assert(sourceUnchanged, "archive save plus path restore did not rewrite the source file");

        Append("CALL LoadProjectFile(archive)");
        var task = load.Invoke(model, [archive]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var loadedProject = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert(loadedProject != null, "archive project reload returned a Project");
        var markerFound = ContainsTimelineName(loadedProject!, "CNWL_ARCHIVE_ROUNDTRIP_MARKER");
        Assert(markerFound, "archive reload preserved the deterministic Timeline marker");

        WriteResult("PASS_PROJECT_SAVE_COPY_PATH_RESTORE_OBSERVATION", true, keepPreservedSource, restoredSourcePath, sourceUnchanged, markerFound, isSaved, archiveHash);
    }

    private static void DumpDetachedSaveSurface(object project)
    {
        var lines = new List<string>();
        void Add(string text) => lines.Add(text);
        var pt = project.GetType();
        Add("DETACHED_TYPE " + pt.AssemblyQualifiedName);
        foreach (var m in pt.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => ContainsIoKeyword(m.Name)).OrderBy(m => m.Name).Take(300))
            Add("PROJECT_METHOD " + DescribeMethod(m));
        foreach (var p in pt.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => ContainsIoKeyword(p.Name)).OrderBy(p => p.Name).Take(200))
            Add($"PROJECT_PROPERTY {(p.GetGetMethod(true)?.IsPublic == true ? "public" : "nonpublic")} {p.PropertyType.FullName} {p.Name}");

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true))
        {
            foreach (var type in SafeTypes(a).Where(t => ContainsIoKeyword(t.Name) || (t.FullName?.Contains("ProjectFile", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => ContainsIoKeyword(m.Name)).ToArray();
                if (methods.Length == 0) continue;
                Add("CANDIDATE_TYPE " + type.FullName);
                foreach (var m in methods.OrderBy(m => m.Name).Take(100)) Add("CANDIDATE_METHOD " + DescribeMethod(m));
            }
        }
        File.WriteAllLines(Path.Combine(output, "detached-save-surface.txt"), lines, new UTF8Encoding(false));
    }

    private static bool ContainsIoKeyword(string name) => new[] { "save", "write", "serialize", "json", "file", "project", "copy", "export" }
        .Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string DescribeMethod(MethodInfo m) =>
        $"{(m.IsPublic ? "public" : "nonpublic")} {(m.IsStatic ? "static" : "instance")} {m.ReturnType.FullName} {m.DeclaringType?.FullName}.{m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))})";

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(x => x != null).Cast<Type>().ToArray(); }
    }

    private static bool ContainsTimelineName(object project, string expected)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Search(project, expected, 0, visited);
    }

    private static bool Search(object? value, string expected, int depth, HashSet<object> visited)
    {
        if (value == null || depth > 5) return false;
        if (value is string) return false;
        var type = value.GetType();
        if (!type.IsValueType && !visited.Add(value)) return false;
        if (value is Timeline timeline && timeline.Name == expected) return true;
        if (value is System.Collections.IEnumerable enumerable)
        {
            var n = 0;
            foreach (var item in enumerable)
            {
                if (Search(item, expected, depth + 1, visited)) return true;
                if (++n > 1000) break;
            }
            return false;
        }
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetGetMethod() == null) continue;
            if (p.PropertyType == typeof(string) || p.PropertyType.IsPrimitive || p.PropertyType.IsEnum) continue;
            object? next;
            try { next = p.GetValue(value); } catch { continue; }
            if (Search(next, expected, depth + 1, visited)) return true;
        }
        return false;
    }

    private static bool SamePath(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, bool archiveExists, bool keepPreservedSource, bool pathRestored, bool sourceUnchanged, bool markerReloaded, bool savedState, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "archive_exists=" + archiveExists,
            "keep_project_path_preserved_source=" + keepPreservedSource,
            "active_path_restored_source=" + pathRestored,
            "source_byte_unchanged=" + sourceUnchanged,
            "archive_reload_marker=" + markerReloaded,
            "active_saved_state_after_path_restore=" + savedState,
            "detail=" + detail
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "roundtrip.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
