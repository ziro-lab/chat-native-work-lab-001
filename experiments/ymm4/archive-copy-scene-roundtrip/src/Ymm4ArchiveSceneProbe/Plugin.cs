using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ArchiveSceneProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Archive Copy Scene Roundtrip";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_ARCHIVE_SCENE_DIR");
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

    private static async Task RunAsync(object root, object model, Timeline main)
    {
        var source = Path.Combine(output, "source.ymmp");
        var archive = Path.Combine(output, "archive.ymmp");
        main.Name = "Main";

        var modelType = model.GetType();
        var scenes = modelType.GetProperty("Scenes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(model)
            ?? throw new InvalidOperationException("MainModel.Scenes missing.");
        var timelinesProperty = scenes.GetType().GetProperty("Timelines", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Scenes.Timelines missing.");
        var createNewScene = modelType.GetMethod("CreateNewScene", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.CreateNewScene missing.");
        var selectScene = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SelectScene" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(Timeline));
        var save = root.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var load = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));

        createNewScene.Invoke(model, null);
        var afterDep = GetTimelines(timelinesProperty.GetValue(scenes));
        Assert(afterDep.Count == 2, "first CreateNewScene produced a second Timeline");
        var dependency = afterDep.Single(x => !ReferenceEquals(x, main));
        dependency.Name = "Dependency";

        createNewScene.Invoke(model, null);
        var all = GetTimelines(timelinesProperty.GetValue(scenes));
        Assert(all.Count == 3, "second CreateNewScene produced a third Timeline");
        var unused = all.Single(x => !ReferenceEquals(x, main) && !ReferenceEquals(x, dependency));
        unused.Name = "Unused";

        var dependencyId = GetTimelineId(dependency);
        var unusedId = GetTimelineId(unused);
        var mainId = GetTimelineId(main);
        Append($"IDS main={mainId} dependency={dependencyId} unused={unusedId}");
        Assert(mainId != dependencyId && dependencyId != unusedId && mainId != unusedId, "three Timeline IDs are distinct");

        var sceneItem = new SceneItem
        {
            SceneId = dependencyId,
            Frame = 0,
            Length = 30,
            Layer = 0,
            Remark = "CNWL_DEPENDENCY_SCENE_ITEM"
        };
        main.Items = main.Items.Add(sceneItem);
        main.RefreshTimelineLengthAndMaxLayer();
        selectScene.Invoke(model, [main]);

        save.Invoke(root, [source]);
        Assert(File.Exists(source), "source Project was saved");
        var sourceHashBefore = Hash(source);
        File.Copy(source, archive, overwrite: true);
        Assert(Hash(source) == Hash(archive), "archive copy initially equals source bytes");

        var rootJson = JsonNode.Parse(File.ReadAllText(archive))?.AsObject()
            ?? throw new InvalidOperationException("archive copy is not a JSON object.");
        var timelinesJson = rootJson["Timelines"]?.AsArray()
            ?? throw new InvalidOperationException("archive copy has no Timelines array.");
        Append("JSON timelines before=" + timelinesJson.Count);
        var removed = 0;
        for (var i = timelinesJson.Count - 1; i >= 0; i--)
        {
            if (timelinesJson[i] is not JsonObject obj) continue;
            var name = obj["Name"]?.GetValue<string>();
            if (name == "Unused") { timelinesJson.RemoveAt(i); removed++; }
        }
        Assert(removed == 1, "offline archive transform removed exactly the Unused Timeline");
        Assert(timelinesJson.Count == 2, "offline archive copy retains exactly two Timelines");

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(archive, rootJson.ToJsonString(options), new UTF8Encoding(false));
        var sourceUnchanged = string.Equals(sourceHashBefore, Hash(source), StringComparison.OrdinalIgnoreCase);
        Assert(sourceUnchanged, "offline archive transform left source Project bytes unchanged");

        var task = load.Invoke(model, [archive]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var loadedProject = task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new InvalidOperationException("archive LoadProjectFile returned null.");
        var loadedTimelines = FindTimelines(loadedProject);
        Append("LOADED timelines=" + string.Join(",", loadedTimelines.Select(x => x.Name + ":" + GetTimelineId(x))));
        Assert(loadedTimelines.Count == 2, "native loader accepted archive with exactly two Timelines");
        var loadedMain = loadedTimelines.Single(x => x.Name == "Main");
        var loadedDependency = loadedTimelines.Single(x => x.Name == "Dependency");
        var loadedDependencyId = GetTimelineId(loadedDependency);
        var references = loadedMain.Items.OfType<SceneItem>().Where(x => x.Remark == "CNWL_DEPENDENCY_SCENE_ITEM").ToArray();
        Assert(references.Length == 1, "native loader preserved the dependency SceneItem");
        var dependencyResolved = references[0].SceneId == loadedDependencyId;
        Assert(dependencyResolved, "SceneItem.SceneId resolves to retained Dependency Timeline after archive pruning");
        Assert(loadedTimelines.All(x => x.Name != "Unused"), "Unused Timeline is absent after native reload");

        WriteResult("PASS_ARCHIVE_COPY_SCENE_ROUNDTRIP", true, sourceUnchanged, dependencyResolved, true, Hash(archive));
    }

    private static List<Timeline> GetTimelines(object? value)
    {
        if (value is not IEnumerable enumerable) throw new InvalidOperationException("Scenes.Timelines is not enumerable.");
        return enumerable.Cast<object>().OfType<Timeline>().ToList();
    }

    private static Guid GetTimelineId(Timeline timeline)
    {
        var p = timeline.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public)
            ?? timeline.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
        if (p?.GetValue(timeline) is Guid id) return id;
        throw new InvalidOperationException("Timeline public ID/Id property missing.");
    }

    private static List<Timeline> FindTimelines(object project)
    {
        foreach (var p in project.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetGetMethod() == null) continue;
            object? value;
            try { value = p.GetValue(project); } catch { continue; }
            if (value is not IEnumerable enumerable || value is string) continue;
            var list = enumerable.Cast<object>().OfType<Timeline>().ToList();
            if (list.Count > 0) return list;
        }
        throw new InvalidOperationException("Loaded Project exposes no public Timeline collection.");
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

    private static void WriteResult(string status, bool archiveLoaded, bool sourceUnchanged, bool dependencyResolved, bool unusedRemoved, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "archive_loaded=" + archiveLoaded,
            "source_byte_unchanged=" + sourceUnchanged,
            "dependency_resolved=" + dependencyResolved,
            "unused_removed=" + unusedRemoved,
            "detail=" + detail
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "roundtrip.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
