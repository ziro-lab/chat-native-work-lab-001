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
                    WriteResult("FAIL_TIMEOUT", false, false, false, false, "");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", false, false, false, false, ex.GetBaseException().Message);
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
        var save = rootType.GetMethod("SaveProject", BindingFlags.Instance | BindingFlags.Public, [typeof(string)])
            ?? throw new InvalidOperationException("MainViewModel.SaveProject(string) missing.");
        var keep = rootType.GetProperty("KeepProjectPath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainViewModel.KeepProjectPath missing.");
        var modelPath = modelType.GetProperty("ProjectFilePath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.ProjectFilePath missing.");
        var load = modelType.GetMethod("LoadProjectFile", BindingFlags.Instance | BindingFlags.Public, [typeof(string)])
            ?? throw new InvalidOperationException("MainModel.LoadProjectFile(string) missing.");

        Append("CALL SaveProject(source)");
        save.Invoke(root, [source]);
        Assert(File.Exists(source), "source project was created");
        var activeAfterSource = modelPath.GetValue(model) as string ?? "";
        Append("PATH afterSource=" + activeAfterSource);
        Assert(SamePath(activeAfterSource, source), "active project path points to source after initial save");
        var sourceHashBefore = Hash(source);
        Append("HASH sourceBefore=" + sourceHashBefore);

        var oldKeep = (bool)(keep.GetValue(root) ?? false);
        keep.SetValue(root, true);
        try
        {
            Append("CALL KeepProjectPath=true; SaveProject(archive)");
            save.Invoke(root, [archive]);
        }
        finally { keep.SetValue(root, oldKeep); }

        Assert(File.Exists(archive), "archive project was created");
        var activeAfterArchive = modelPath.GetValue(model) as string ?? "";
        Append("PATH afterArchive=" + activeAfterArchive);
        var keptSourcePath = SamePath(activeAfterArchive, source);
        Assert(keptSourcePath, "archive save kept the source project path active");

        var sourceHashAfter = Hash(source);
        var archiveHash = Hash(archive);
        Append("HASH sourceAfter=" + sourceHashAfter);
        Append("HASH archive=" + archiveHash);
        var sourceUnchanged = string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.OrdinalIgnoreCase);
        Assert(sourceUnchanged, "archive save did not rewrite the source file");

        Append("CALL LoadProjectFile(archive)");
        var task = load.Invoke(model, [archive]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var resultProperty = task.GetType().GetProperty("Result");
        var loadedProject = resultProperty?.GetValue(task);
        Assert(loadedProject != null, "archive project reload returned a Project");
        var markerFound = ContainsTimelineName(loadedProject!, "CNWL_ARCHIVE_ROUNDTRIP_MARKER");
        Assert(markerFound, "archive reload preserved the deterministic Timeline marker");

        WriteResult("PASS_PROJECT_SAVE_COPY_ROUNDTRIP", true, keptSourcePath, sourceUnchanged, markerFound, archiveHash);
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

    private static void WriteResult(string status, bool archiveExists, bool pathKept, bool sourceUnchanged, bool markerReloaded, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "archive_exists=" + archiveExists,
            "active_path_kept_source=" + pathKept,
            "source_byte_unchanged=" + sourceUnchanged,
            "archive_reload_marker=" + markerReloaded,
            "detail=" + detail
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "roundtrip.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
