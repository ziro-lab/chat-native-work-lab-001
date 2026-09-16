using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YmmJson = YukkuriMovieMaker.Json.Json;
using YmmProject = YukkuriMovieMaker.Project.Project;

namespace Ymm4RecordingArchiveSpineProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Integrated Spine";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_RECORDING_ARCHIVE_SPINE_DIR");
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
                if (ticks >= 120)
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

    private static async Task RunAsync(object model, Timeline main)
    {
        var modelType = model.GetType();
        var scenes = modelType.GetProperty("Scenes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(model) as Scenes
            ?? throw new InvalidOperationException("MainModel.Scenes missing.");
        var createNewScene = modelType.GetMethod("CreateNewScene", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new InvalidOperationException("MainModel.CreateNewScene missing.");
        var selectScene = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SelectScene" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(Timeline));
        var saveProject = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var loadProject = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var livePathProperty = modelType.GetProperty("ProjectFilePath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.ProjectFilePath missing.");
        var liveSavedProperty = modelType.GetProperty("IsProjectFileSaved", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("MainModel.IsProjectFileSaved missing.");

        main.Name = "Main";
        createNewScene.Invoke(model, null);
        await Idle();
        var usedSub = scenes.Timelines.Single(x => !ReferenceEquals(x, main));
        usedSub.Name = "UsedSub";
        createNewScene.Invoke(model, null);
        await Idle();
        var scratch = scenes.Timelines.Single(x => !ReferenceEquals(x, main) && !ReferenceEquals(x, usedSub));
        scratch.Name = "Scratch";
        Assert(scenes.Timelines.Count == 3, "live fixture contains Main, UsedSub, Scratch");

        var usedSubId = GetTimelineId(usedSub);
        Assert(usedSubId != Guid.Empty, "UsedSub exposes stable ID");
        var sceneItem = new SceneItem { SceneId = usedSubId, Frame = 20, Length = 180, Layer = 30, Remark = "CNWL_DEPENDENCY" };
        Assert(main.TryAddItems([sceneItem], sceneItem.Frame, sceneItem.Layer), "Main contains SceneItem -> UsedSub");

        var sourceA = Path.Combine(output, "source-recording-A.mp4");
        var sourceB = Path.Combine(output, "source-recording-B.mp4");
        var clipA = Path.Combine(output, "recordings", "source-recording-A_001.mp4");
        var clipB = Path.Combine(output, "recordings", "source-recording-B_001.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(clipA)!);

        var mainVideo = new VideoItem
        {
            FilePath = sourceA,
            ContentOffset = TimeSpan.FromSeconds(100.25),
            Frame = 60,
            Length = 300,
            Layer = 10,
            Remark = "CNWL_MAIN_VIDEO"
        };
        mainVideo.PlaybackRate2.SetFirstValue(200);
        mainVideo.PlaybackRate2.SetAnimationParameters(mainVideo.Length, 60);
        Assert(main.TryAddItems([mainVideo], mainVideo.Frame, mainVideo.Layer), "Main recording VideoItem added");

        var subVideo = new VideoItem
        {
            FilePath = sourceB,
            ContentOffset = TimeSpan.FromSeconds(300.5),
            Frame = 30,
            Length = 240,
            Layer = 10,
            Remark = "CNWL_SUB_VIDEO"
        };
        subVideo.PlaybackRate2.SetFirstValue(50);
        subVideo.PlaybackRate2.SetAnimationParameters(subVideo.Length, 60);
        Assert(usedSub.TryAddItems([subVideo], subVideo.Frame, subVideo.Layer), "UsedSub recording VideoItem added");

        selectScene.Invoke(model, [main]);
        await Idle();

        var sourceProject = Path.Combine(output, "source.ymmp");
        var archiveProject = Path.Combine(output, "archive.ymmp");
        saveProject.Invoke(model, [sourceProject]);
        Assert(File.Exists(sourceProject), "source project saved");
        var sourceHashBefore = Hash(sourceProject);
        Assert(SamePath((string?)livePathProperty.GetValue(model) ?? "", sourceProject), "live project path points to source");
        Assert((bool)(liveSavedProperty.GetValue(model) ?? false), "live project is saved before detached transform");

        var detachedTask = loadProject.Invoke(model, [sourceProject]) as Task ?? throw new InvalidOperationException("LoadProjectFile(source) did not return Task.");
        await detachedTask;
        var detachedObject = detachedTask.GetType().GetProperty("Result")?.GetValue(detachedTask)
            ?? throw new InvalidOperationException("LoadProjectFile(source) returned null.");
        if (detachedObject is not YmmProject detached) throw new InvalidOperationException("Detached object is not Project.");

        var detachedTimelines = GetProjectTimelines(detached).ToList();
        Assert(detachedTimelines.Count == 3, "detached project contains three fixture timelines");
        var detachedMain = detachedTimelines.Single(x => x.Name == "Main");
        var detachedUsed = detachedTimelines.Single(x => x.Name == "UsedSub");
        var detachedScratch = detachedTimelines.Single(x => x.Name == "Scratch");
        var closure = ComputeClosure(GetTimelineId(detachedMain), detachedTimelines);
        Assert(closure.SetEquals([GetTimelineId(detachedMain), GetTimelineId(detachedUsed)]), "detached closure is Main + UsedSub");
        Assert(!closure.Contains(GetTimelineId(detachedScratch)), "detached closure excludes Scratch");

        var detachedMainVideo = detachedMain.Items.OfType<VideoItem>().Single(x => x.Remark == "CNWL_MAIN_VIDEO");
        var detachedSubVideo = detachedUsed.Items.OfType<VideoItem>().Single(x => x.Remark == "CNWL_SUB_VIDEO");
        detachedMainVideo.FilePath = clipA;
        detachedMainVideo.ContentOffset = TimeSpan.FromSeconds(30.25);
        detachedSubVideo.FilePath = clipB;
        detachedSubVideo.ContentOffset = TimeSpan.FromSeconds(40.5);
        detached.FilePath = archiveProject;

        var archiveDetached = PruneProjectThroughYmmJson(detached, closure, archiveProject);
        var prunedTimelines = GetProjectTimelines(archiveDetached).ToList();
        Assert(prunedTimelines.Count == 2, "YMM Json roundtrip prunes Scratch only");
        Assert(prunedTimelines.All(x => x.Name is "Main" or "UsedSub"), "YMM Json roundtrip preserves Main + UsedSub");

        Assert(mainVideo.FilePath == sourceA && mainVideo.ContentOffset == TimeSpan.FromSeconds(100.25), "detached Main relink does not mutate live Main VideoItem");
        Assert(subVideo.FilePath == sourceB && subVideo.ContentOffset == TimeSpan.FromSeconds(300.5), "detached subscene relink does not mutate live UsedSub VideoItem");
        Assert(scenes.Timelines.Count == 3, "detached scene pruning does not mutate live scene list");

        YmmJson.Save(archiveDetached, archiveProject);
        Assert(File.Exists(archiveProject), "detached Json.Save created archive.ymmp");
        var sourceHashAfter = Hash(sourceProject);
        var sourceUnchanged = sourceHashBefore == sourceHashAfter;
        Assert(sourceUnchanged, "source.ymmp remains byte-for-byte unchanged");
        var liveUnchanged = scenes.Timelines.Count == 3
            && mainVideo.FilePath == sourceA
            && mainVideo.ContentOffset == TimeSpan.FromSeconds(100.25)
            && subVideo.FilePath == sourceB
            && subVideo.ContentOffset == TimeSpan.FromSeconds(300.5)
            && SamePath((string?)livePathProperty.GetValue(model) ?? "", sourceProject)
            && (bool)(liveSavedProperty.GetValue(model) ?? false);
        Assert(liveUnchanged, "live project remains unchanged after integrated detached archive save");

        var archiveTask = loadProject.Invoke(model, [archiveProject]) as Task ?? throw new InvalidOperationException("LoadProjectFile(archive) did not return Task.");
        await archiveTask;
        var archiveObject = archiveTask.GetType().GetProperty("Result")?.GetValue(archiveTask)
            ?? throw new InvalidOperationException("LoadProjectFile(archive) returned null.");
        if (archiveObject is not YmmProject archive) throw new InvalidOperationException("Archive object is not Project.");
        var archiveTimelines = GetProjectTimelines(archive).ToList();
        Assert(archiveTimelines.Count == 2, "archive reload contains exactly Main + UsedSub");
        Assert(archiveTimelines.All(x => x.Name is "Main" or "UsedSub"), "archive reload excludes Scratch");
        var archiveMain = archiveTimelines.Single(x => x.Name == "Main");
        var archiveUsed = archiveTimelines.Single(x => x.Name == "UsedSub");
        var archiveDependency = archiveMain.Items.OfType<SceneItem>().Single(x => x.Remark == "CNWL_DEPENDENCY");
        Assert(archiveDependency.SceneId == GetTimelineId(archiveUsed), "archive SceneItem still resolves to UsedSub");

        var archiveMainVideo = archiveMain.Items.OfType<VideoItem>().Single(x => x.Remark == "CNWL_MAIN_VIDEO");
        var archiveSubVideo = archiveUsed.Items.OfType<VideoItem>().Single(x => x.Remark == "CNWL_SUB_VIDEO");
        Assert(archiveMainVideo.FilePath == clipA && archiveMainVideo.ContentOffset == TimeSpan.FromSeconds(30.25), "archive Main VideoItem relink survives reload");
        Assert(archiveSubVideo.FilePath == clipB && archiveSubVideo.ContentOffset == TimeSpan.FromSeconds(40.5), "archive UsedSub VideoItem relink survives reload");
        archiveMainVideo.PlaybackRate2.SetAnimationParameters(archiveMainVideo.Length, 60);
        archiveSubVideo.PlaybackRate2.SetAnimationParameters(archiveSubVideo.Length, 60);
        AssertNear(archiveMainVideo.PlaybackRate2.GetValue(0, archiveMainVideo.Length, 60), 200, "archive Main playback rate survives reload");
        AssertNear(archiveSubVideo.PlaybackRate2.GetValue(0, archiveSubVideo.Length, 60), 50, "archive UsedSub playback rate survives reload");

        Assert(SamePath((string?)livePathProperty.GetValue(model) ?? "", sourceProject), "archive validation does not switch live project path");
        Assert((bool)(liveSavedProperty.GetValue(model) ?? false), "archive validation leaves live project saved");

        WriteResult("PASS_RECORDING_ARCHIVE_INTEGRATED_SPINE", true, sourceUnchanged, liveUnchanged, true, true, Hash(archiveProject));
    }

    private static YmmProject PruneProjectThroughYmmJson(YmmProject project, HashSet<Guid> keepIds, string archivePath)
    {
        var jsonText = YmmJson.GetJsonText(project);
        var root = JObject.Parse(jsonText);
        var timelines = root["Timelines"] as JArray ?? throw new InvalidOperationException("Serialized Project.Timelines JSON array missing.");
        var before = timelines.Count;
        foreach (var node in timelines.OfType<JObject>().ToList())
        {
            var idText = (node["ID"] ?? node["Id"] ?? node["SceneId"])?.ToString();
            if (!Guid.TryParse(idText, out var id)) throw new InvalidOperationException("Serialized Timeline ID missing or invalid.");
            if (!keepIds.Contains(id)) node.Remove();
        }
        Assert(before == 3 && timelines.Count == 2, "serialized Timeline filter removes exactly one non-dependent scene");
        root["SelectedTimelineIndex"] = 0;
        root["FilePath"] = archivePath;
        var rebuilt = YmmJson.LoadFromText<YmmProject>(root.ToString(Formatting.None))
            ?? throw new InvalidOperationException("YMM Json.LoadFromText<Project> returned null after scene pruning.");
        rebuilt.FilePath = archivePath;
        return rebuilt;
    }

    private static IEnumerable<Timeline> GetProjectTimelines(YmmProject project)
    {
        var property = project.GetType().GetProperty("Timelines", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Project.Timelines missing.");
        return property.GetValue(project) as IEnumerable<Timeline>
            ?? throw new InvalidOperationException("Project.Timelines is not IEnumerable<Timeline>.");
    }

    private static HashSet<Guid> ComputeClosure(Guid root, IEnumerable<Timeline> timelines)
    {
        var map = timelines.ToDictionary(GetTimelineId);
        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!result.Add(id)) continue;
            if (!map.TryGetValue(id, out var timeline)) throw new InvalidOperationException("Missing timeline for scene dependency " + id);
            foreach (var child in timeline.Items.OfType<SceneItem>().Select(x => x.SceneId))
                if (!result.Contains(child)) queue.Enqueue(child);
        }
        return result;
    }

    private static Guid GetTimelineId(Timeline timeline)
    {
        foreach (var name in new[] { "ID", "Id", "SceneId", "Guid" })
        {
            var property = timeline.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.PropertyType == typeof(Guid) && property.GetIndexParameters().Length == 0)
            {
                var value = (Guid)(property.GetValue(timeline) ?? Guid.Empty);
                if (value != Guid.Empty) return value;
            }
        }
        throw new InvalidOperationException("Timeline stable ID missing for " + timeline.Name);
    }

    private static async Task Idle() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

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

    private static void AssertNear(double actual, double expected, string message)
    {
        if (Math.Abs(actual - expected) > 1e-9) throw new InvalidOperationException($"ASSERT FAIL: {message}; actual={actual:R} expected={expected:R}");
        Append($"ASSERT PASS: {message}; value={actual:R}");
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, bool archiveExists, bool sourceUnchanged, bool liveUnchanged, bool sceneClosure, bool relinks, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=" + status,
            "archive_exists=" + archiveExists,
            "source_byte_unchanged=" + sourceUnchanged,
            "live_state_unchanged=" + liveUnchanged,
            "scene_dependency_closure=" + sceneClosure,
            "video_relinks_roundtrip=" + relinks,
            "detail=" + detail
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "spine.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
