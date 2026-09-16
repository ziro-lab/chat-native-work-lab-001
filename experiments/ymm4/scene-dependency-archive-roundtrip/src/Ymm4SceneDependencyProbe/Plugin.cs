using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SceneDependencyProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Scene Dependency Archive Roundtrip";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SCENE_DEPENDENCY_DIR");
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
                    var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);
                    var timeline = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (model == null || timeline == null) continue;
                    timer.Stop();
                    await RunAsync(model, timeline);
                    return;
                }
                if (ticks >= 100)
                {
                    timer.Stop();
                    WriteResult("FAIL_TIMEOUT", "");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object model, Timeline main)
    {
        var modelType = model.GetType();
        var scenes = modelType.GetProperty("Scenes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(model) as Scenes
            ?? throw new InvalidOperationException("MainModel.Scenes public surface missing.");
        var createNewScene = modelType.GetMethod("CreateNewScene", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new InvalidOperationException("MainModel.CreateNewScene() public surface missing.");
        var selectScene = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "SelectScene" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(Timeline))
            ?? throw new InvalidOperationException("MainModel.SelectScene(Timeline) public surface missing.");
        var saveProject = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainModel.SaveProject(string) public surface missing.");
        var loadProject = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainModel.LoadProjectFile(string) public surface missing.");

        main.Name = "Main";
        var before = scenes.Timelines;
        Assert(before.Count == 1, "fixture starts with one Main timeline");

        createNewScene.Invoke(model, null);
        await Idle();
        var afterUsed = scenes.Timelines;
        Assert(afterUsed.Count == 2, "second scene was created");
        var used = afterUsed.Single(x => !ReferenceEquals(x, main));
        used.Name = "UsedSub";

        createNewScene.Invoke(model, null);
        await Idle();
        var all = scenes.Timelines;
        Assert(all.Count == 3, "third scene was created");
        var scratch = all.Single(x => !ReferenceEquals(x, main) && !ReferenceEquals(x, used));
        scratch.Name = "Scratch";

        var mainId = GetTimelineId(main);
        var usedId = GetTimelineId(used);
        var scratchId = GetTimelineId(scratch);
        Append($"IDS main={mainId} used={usedId} scratch={scratchId}");
        Assert(mainId != Guid.Empty && usedId != Guid.Empty && scratchId != Guid.Empty, "all timelines expose stable Guid identities");
        Assert(new[] { mainId, usedId, scratchId }.Distinct().Count() == 3, "timeline identities are unique");

        var sceneItem = new SceneItem { SceneId = usedId, Frame = 30, Length = 60, Layer = 5, Remark = "CNWL_SCENE_DEPENDENCY" };
        Assert(main.TryAddItems([sceneItem], sceneItem.Frame, sceneItem.Layer), "SceneItem fixture was added to Main");
        Assert(main.Items.OfType<SceneItem>().Single(x => x.Remark == "CNWL_SCENE_DEPENDENCY").SceneId == usedId, "Main SceneItem points to UsedSub");

        var closure = ComputeClosure(mainId, all);
        Append("CLOSURE " + string.Join(",", closure));
        Assert(closure.SetEquals([mainId, usedId]), "dependency closure contains Main and UsedSub only");

        selectScene.Invoke(model, [main]);
        await Idle();
        scenes.Timelines = all.Where(x => closure.Contains(GetTimelineId(x))).ToImmutableList();
        Assert(scenes.Timelines.Count == 2, "Scratch was removed from archive fixture");
        Assert(scenes.Timelines.All(x => x.Name is "Main" or "UsedSub"), "only Main and UsedSub remain before save");

        var archive = Path.Combine(output, "scene-archive.ymmp");
        saveProject.Invoke(model, [archive]);
        Assert(File.Exists(archive), "archive project file was created");

        var task = loadProject.Invoke(model, [archive]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var loadedProject = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert(loadedProject != null, "archive project reload returned a Project");
        var loadedTimelines = CollectTimelines(loadedProject!).Distinct(ReferenceEqualityComparer.Instance).ToList();
        Append("RELOADED timelines=" + string.Join(",", loadedTimelines.Select(x => x.Name)));
        var named = loadedTimelines.Where(x => x.Name is "Main" or "UsedSub" or "Scratch").ToList();
        Assert(named.Count(x => x.Name == "Main") == 1, "reloaded archive contains Main");
        Assert(named.Count(x => x.Name == "UsedSub") == 1, "reloaded archive contains UsedSub");
        Assert(named.All(x => x.Name != "Scratch"), "reloaded archive does not contain Scratch");

        var loadedMain = named.Single(x => x.Name == "Main");
        var loadedUsed = named.Single(x => x.Name == "UsedSub");
        var loadedRef = loadedMain.Items.OfType<SceneItem>().Single(x => x.Remark == "CNWL_SCENE_DEPENDENCY");
        var loadedUsedId = GetTimelineId(loadedUsed);
        Append($"RELOADED ref={loadedRef.SceneId} used={loadedUsedId}");
        Assert(loadedRef.SceneId == loadedUsedId, "SceneItem reference still resolves to UsedSub after reload");

        WriteResult("PASS_SCENE_DEPENDENCY_ARCHIVE_ROUNDTRIP", archive);
    }

    private static async Task Idle() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static HashSet<Guid> ComputeClosure(Guid rootId, IEnumerable<Timeline> timelines)
    {
        var map = timelines.ToDictionary(GetTimelineId);
        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!result.Add(id)) continue;
            if (!map.TryGetValue(id, out var timeline)) throw new InvalidOperationException("Missing timeline for dependency id " + id);
            foreach (var child in timeline.Items.OfType<SceneItem>().Select(x => x.SceneId))
                if (!result.Contains(child)) queue.Enqueue(child);
        }
        return result;
    }

    private static Guid GetTimelineId(Timeline timeline)
    {
        foreach (var name in new[] { "Id", "SceneId", "Guid" })
        {
            var p = timeline.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.PropertyType == typeof(Guid) && p.GetIndexParameters().Length == 0)
                return (Guid)(p.GetValue(timeline) ?? Guid.Empty);
        }
        var candidate = timeline.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(p => p.PropertyType == typeof(Guid) && p.GetIndexParameters().Length == 0 && p.Name.Contains("id", StringComparison.OrdinalIgnoreCase));
        return candidate == null ? Guid.Empty : (Guid)(candidate.GetValue(timeline) ?? Guid.Empty);
    }

    private static List<Timeline> CollectTimelines(object root)
    {
        var result = new List<Timeline>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root, 0);
        return result;

        void Visit(object? value, int depth)
        {
            if (value == null || depth > 6 || value is string) return;
            if (value is Timeline timeline)
            {
                result.Add(timeline);
                return;
            }
            var type = value.GetType();
            if (!type.IsValueType && !visited.Add(value)) return;
            if (value is IEnumerable enumerable)
            {
                var n = 0;
                foreach (var item in enumerable)
                {
                    Visit(item, depth + 1);
                    if (++n > 2000) break;
                }
                return;
            }
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length != 0 || p.GetMethod == null) continue;
                if (p.PropertyType == typeof(string) || p.PropertyType.IsPrimitive || p.PropertyType.IsEnum) continue;
                object? next;
                try { next = p.GetValue(value); } catch { continue; }
                Visit(next, depth + 1);
            }
        }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "detail=" + detail
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "roundtrip.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
