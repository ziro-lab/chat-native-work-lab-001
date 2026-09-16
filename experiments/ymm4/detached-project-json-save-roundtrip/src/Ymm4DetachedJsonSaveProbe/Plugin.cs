using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YmmProject = YukkuriMovieMaker.Project.Project;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace Ymm4DetachedJsonSaveProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Detached Project Json Save Roundtrip";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_DETACHED_JSON_SAVE_DIR");
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
                    WriteResult("FAIL_TIMEOUT", false, false, false, false, false, "");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", false, false, false, false, false, ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object model, Timeline liveTimeline)
    {
        var source = Path.Combine(output, "source.ymmp");
        var archive = Path.Combine(output, "archive.ymmp");
        const string sourceMarker = "CNWL_LIVE_SOURCE_MARKER";
        const string archiveMarker = "CNWL_DETACHED_ARCHIVE_MARKER";
        liveTimeline.Name = sourceMarker;

        var modelType = model.GetType();
        var save = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var load = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var pathProperty = modelType.GetProperty("ProjectFilePath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.ProjectFilePath missing.");
        var savedProperty = modelType.GetProperty("IsProjectFileSaved", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.IsProjectFileSaved missing.");

        Append("CALL MainModel.SaveProject(source)");
        save.Invoke(model, [source]);
        Assert(File.Exists(source), "source.ymmp was created");
        var sourceHashBefore = Hash(source);
        var livePathBefore = (string?)pathProperty.GetValue(model) ?? "";
        var liveSavedBefore = (bool)(savedProperty.GetValue(model) ?? false);
        Assert(SamePath(livePathBefore, source), "live project path points to source before detached work");
        Assert(liveSavedBefore, "live project is saved before detached work");

        Append("CALL MainModel.LoadProjectFile(source) -> detached Project");
        var loadTask = load.Invoke(model, [source]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await loadTask;
        var detachedObject = loadTask.GetType().GetProperty("Result")?.GetValue(loadTask)
            ?? throw new InvalidOperationException("LoadProjectFile returned null Project.");
        if (detachedObject is not YmmProject detached) throw new InvalidOperationException("Detached result is not YukkuriMovieMaker.Project.Project.");
        Assert(!ReferenceEquals(detached, model), "detached Project is a separate object graph");
        var detachedTimeline = FindTimeline(detached, sourceMarker) ?? throw new InvalidOperationException("Detached Timeline marker not found.");
        Assert(!ReferenceEquals(detachedTimeline, liveTimeline), "detached Timeline is not the live Timeline instance");

        Append("MUTATE detached Timeline + detached FilePath only");
        detachedTimeline.Name = archiveMarker;
        detached.FilePath = archive;
        Assert(liveTimeline.Name == sourceMarker, "mutating detached Timeline does not change live Timeline");

        Append("CALL YukkuriMovieMaker.Json.Json.Save(detached, archive)");
        YmmJson.Save(detached, archive);
        Assert(File.Exists(archive), "archive.ymmp was created by Json.Save");
        var archiveHash = Hash(archive);
        Append("HASH archive=" + archiveHash);

        var livePathAfter = (string?)pathProperty.GetValue(model) ?? "";
        var liveSavedAfter = (bool)(savedProperty.GetValue(model) ?? false);
        var liveStateUntouched = SamePath(livePathAfter, source) && liveSavedAfter && liveTimeline.Name == sourceMarker;
        Assert(liveStateUntouched, "Json.Save(detached) leaves live path/saved state/Timeline unchanged");

        var sourceHashAfter = Hash(source);
        var sourceUnchanged = string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.OrdinalIgnoreCase);
        Assert(sourceUnchanged, "Json.Save(detached) leaves original source.ymmp byte-for-byte unchanged");

        Append("CALL Json.Load<Project>(archive)");
        var jsonReload = YmmJson.Load<YmmProject>(archive) ?? throw new InvalidOperationException("Json.Load<Project> returned null.");
        var jsonMarker = FindTimeline(jsonReload, archiveMarker);
        Assert(jsonMarker != null, "Json.Load<Project> reloads detached archive marker");

        Append("CALL MainModel.LoadProjectFile(archive)");
        var archiveTask = load.Invoke(model, [archive]) as Task ?? throw new InvalidOperationException("LoadProjectFile(archive) did not return Task.");
        await archiveTask;
        var archiveLoadedObject = archiveTask.GetType().GetProperty("Result")?.GetValue(archiveTask)
            ?? throw new InvalidOperationException("LoadProjectFile(archive) returned null Project.");
        if (archiveLoadedObject is not YmmProject archiveLoaded) throw new InvalidOperationException("Archive result is not Project.");
        var hostMarker = FindTimeline(archiveLoaded, archiveMarker);
        Assert(hostMarker != null, "MainModel.LoadProjectFile validates archive created by Json.Save");

        Assert(SamePath((string?)pathProperty.GetValue(model) ?? "", source), "detached archive validation still does not switch the live project path");
        Assert((bool)(savedProperty.GetValue(model) ?? false), "detached archive validation leaves live project saved");
        Assert(liveTimeline.Name == sourceMarker, "detached archive validation leaves live Timeline unchanged");

        WriteResult("PASS_DETACHED_PROJECT_JSON_SAVE_ROUNDTRIP", true, liveStateUntouched, sourceUnchanged, jsonMarker != null, hostMarker != null, archiveHash);
    }

    private static Timeline? FindTimeline(object root, string name)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Visit(root, 0);

        Timeline? Visit(object? value, int depth)
        {
            if (value == null || depth > 7 || value is string) return null;
            if (value is Timeline timeline) return timeline.Name == name ? timeline : null;
            var type = value.GetType();
            if (!type.IsValueType && !visited.Add(value)) return null;
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var found = Visit(item, depth + 1);
                    if (found != null) return found;
                }
                return null;
            }
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length != 0 || p.GetMethod == null) continue;
                if (p.PropertyType == typeof(string) || p.PropertyType.IsPrimitive || p.PropertyType.IsEnum) continue;
                object? next;
                try { next = p.GetValue(value); } catch { continue; }
                var found = Visit(next, depth + 1);
                if (found != null) return found;
            }
            return null;
        }
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

    private static void WriteResult(string status, bool archiveExists, bool liveStateUntouched, bool sourceUnchanged, bool jsonReloaded, bool hostReloaded, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "archive_exists=" + archiveExists,
            "live_state_untouched=" + liveStateUntouched,
            "source_byte_unchanged=" + sourceUnchanged,
            "json_reload_marker=" + jsonReloaded,
            "host_reload_marker=" + hostReloaded,
            "detail=" + detail
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "roundtrip.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
