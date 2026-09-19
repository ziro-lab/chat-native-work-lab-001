using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4HighlightMemoSceneProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Highlight Memo Scene";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private const string MemoSceneName = "見どころメモ";
    private static bool scheduled;
    private static string output = "";
    private static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_HIGHLIGHT_MEMO_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        var ticks = 0;
        var created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                ticks++;
                var root = Application.Current.Windows.Cast<Window>()
                    .Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (root == null) return;

                var active = root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
                if (active == null)
                {
                    if (!created)
                    {
                        created = true;
                        root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                    }
                    return;
                }

                var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);
                var main = GetTimeline(active);
                if (model == null || main == null) return;

                timer.Stop();
                await RunAsync(root, model, main);
                Write("PASS_HIGHLIGHT_MEMO_SCENE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_HIGHLIGHT_MEMO_SCENE", ex.ToString());
            }

            if (ticks > 180)
            {
                timer.Stop();
                Write("FAIL_HIGHLIGHT_MEMO_SCENE", "Timeout while waiting for active project.");
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object root, object model, Timeline main)
    {
        string media = Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_HIGHLIGHT_MEMO_MEDIA")
            ?? throw new InvalidOperationException("Fixture media path missing."));
        Check("fixture_exists", File.Exists(media));
        int fps = main.VideoInfo.FPS;
        Check("fps_positive", fps > 0);

        var modelType = model.GetType();
        var scenes = modelType.GetProperty("Scenes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(model) as Scenes
            ?? throw new MissingMemberException("MainModel.Scenes public surface missing.");
        var createNewScene = modelType.GetMethod("CreateNewScene", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new MissingMemberException("MainModel.CreateNewScene() public surface missing.");
        var selectScene = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "SelectScene" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(Timeline))
            ?? throw new MissingMemberException("MainModel.SelectScene(Timeline) public surface missing.");
        var saveProject = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new MissingMemberException("MainModel.SaveProject(string) public surface missing.");
        var loadProject = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new MissingMemberException("MainModel.LoadProjectFile(string) public surface missing.");
        Check("public_scene_surfaces", createNewScene.IsPublic && selectScene.IsPublic && saveProject.IsPublic && loadProject.IsPublic);

        main.Name = "Main";
        var sourceItem = NewVideo(media, frame: 600, layer: 4, length: fps * 60, offsetSeconds: 10, remark: "CNWL_LONG_REVIEW_SOURCE", fps);
        Check("main_source_insert", main.TryAddItems([sourceItem], sourceItem.Frame, sourceItem.Layer));
        var mainBefore = Snapshot(sourceItem, fps);
        int mainItemCountBefore = main.Items.Count;

        int sceneCountBefore = scenes.Timelines.Count;
        var memo = GetOrCreateMemoScene(model, scenes, createNewScene);
        Check("memo_scene_created_once", scenes.Timelines.Count == sceneCountBefore + 1 && memo.Name == MemoSceneName);
        Guid memoId = GetTimelineId(memo);
        Check("memo_scene_id_nonempty", memoId != Guid.Empty);

        selectScene.Invoke(model, [main]);
        await Idle();
        Check("main_reselected", ReferenceEquals(GetActiveTimeline(root), main));

        var memoAgain = GetOrCreateMemoScene(model, scenes, createNewScene);
        Check("memo_scene_reused", ReferenceEquals(memoAgain, memo) && scenes.Timelines.Count == sceneCountBefore + 1);
        Check("memo_scene_name_unique", scenes.Timelines.Count(t => t.Name == MemoSceneName) == 1);

        var requested = new[]
        {
            new { Seconds = 30, Offset = 15d, Remark = "見どころナビ｜戦闘開始" },
            new { Seconds = 30, Offset = 55d, Remark = "見どころナビ｜MAP切替" },
            new { Seconds = 12, Offset = 95d, Remark = "見どころナビ｜戦闘開始 / 大きな画面変化" }
        };

        var added = new List<VideoItem>();
        for (int i = 0; i < requested.Length; i++)
        {
            int layer = i + 1;
            var spec = requested[i];
            var item = NewVideo(media, frame: 0, layer, length: fps * spec.Seconds, offsetSeconds: spec.Offset, remark: spec.Remark, fps);
            Check($"memo_add_{i + 1}", memo.TryAddItems([item], 0, layer));
            added.Add(item);
            Check($"main_stays_active_{i + 1}", ReferenceEquals(GetActiveTimeline(root), main));
        }

        var memoItems = memo.Items.OfType<VideoItem>().OrderBy(v => v.Layer).ToArray();
        Check("memo_count_three", memoItems.Length == 3);
        Check("memo_all_frame_zero", memoItems.All(v => v.Frame == 0));
        Check("memo_layers_consecutive", memoItems.Select(v => v.Layer).SequenceEqual([1, 2, 3]));
        Check("memo_durations_configurable", memoItems.Select(v => v.Length).SequenceEqual([fps * 30, fps * 30, fps * 12]));
        Check("memo_offsets_preserved", memoItems.Select(v => v.ContentOffset.TotalSeconds).SequenceEqual([15d, 55d, 95d]));
        Check("memo_remarks_preserved", memoItems.Select(v => v.Remark).SequenceEqual(requested.Select(x => x.Remark)));
        Check("memo_paths_preserved", memoItems.All(v => SamePath(v.FilePath, media)));
        Check("memo_rates_preserved", memoItems.All(v => Math.Abs(v.PlaybackRate2.GetValue(0, v.Length, fps) - 100) < 1e-6));

        Check("main_item_count_unchanged", main.Items.Count == mainItemCountBefore);
        Check("main_source_same_reference", main.Items.Any(x => ReferenceEquals(x, sourceItem)));
        Check("main_source_properties_unchanged", Snapshot(sourceItem, fps) == mainBefore);

        string project = Path.Combine(output, "highlight-memo-roundtrip.ymmp");
        saveProject.Invoke(model, [project]);
        Check("project_saved", File.Exists(project));

        var loadTask = loadProject.Invoke(model, [project]) as Task
            ?? throw new InvalidOperationException("LoadProjectFile(string) did not return Task.");
        await loadTask;
        var loadedProject = loadTask.GetType().GetProperty("Result")?.GetValue(loadTask)
            ?? throw new InvalidOperationException("LoadProjectFile returned null Project.");

        var loadedTimelines = CollectTimelines(loadedProject).DistinctBy(GetTimelineId).ToArray();
        var loadedMemo = loadedTimelines.SingleOrDefault(t => t.Name == MemoSceneName)
            ?? throw new InvalidOperationException("Reloaded project has no memo scene.");
        var loadedMain = loadedTimelines.SingleOrDefault(t => t.Name == "Main")
            ?? throw new InvalidOperationException("Reloaded project has no Main scene.");

        Check("reload_memo_single", loadedTimelines.Count(t => t.Name == MemoSceneName) == 1);
        Check("reload_memo_id_stable", GetTimelineId(loadedMemo) == memoId);

        var loadedMemoItems = loadedMemo.Items.OfType<VideoItem>().OrderBy(v => v.Layer).ToArray();
        Check("reload_memo_count_three", loadedMemoItems.Length == 3);
        Check("reload_memo_frame_zero", loadedMemoItems.All(v => v.Frame == 0));
        Check("reload_memo_layers", loadedMemoItems.Select(v => v.Layer).SequenceEqual([1, 2, 3]));
        Check("reload_memo_lengths", loadedMemoItems.Select(v => v.Length).SequenceEqual([fps * 30, fps * 30, fps * 12]));
        Check("reload_memo_offsets", loadedMemoItems.Select(v => v.ContentOffset.TotalSeconds).SequenceEqual([15d, 55d, 95d]));
        Check("reload_memo_remarks", loadedMemoItems.Select(v => v.Remark).SequenceEqual(requested.Select(x => x.Remark)));
        Check("reload_memo_paths", loadedMemoItems.All(v => SamePath(v.FilePath, media)));

        var loadedSource = loadedMain.Items.OfType<VideoItem>().SingleOrDefault(v => v.Remark == "CNWL_LONG_REVIEW_SOURCE")
            ?? throw new InvalidOperationException("Reloaded Main source item missing.");
        Check("reload_main_source_semantics", loadedSource.Frame == 600 && loadedSource.Layer == 4
            && loadedSource.Length == fps * 60 && Math.Abs(loadedSource.ContentOffset.TotalSeconds - 10) < 1e-6
            && SamePath(loadedSource.FilePath, media));

        File.WriteAllText(Path.Combine(output, "memo-layout.json"), JsonSerializer.Serialize(new
        {
            fps,
            memoSceneId = memoId,
            memoSceneName = MemoSceneName,
            activeSceneWhileAdding = "Main",
            items = memoItems.Select(v => new
            {
                v.Frame,
                v.Layer,
                v.Length,
                offsetSeconds = v.ContentOffset.TotalSeconds,
                v.Remark,
                v.FilePath
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Timeline GetOrCreateMemoScene(object model, Scenes scenes, MethodInfo createNewScene)
    {
        var matches = scenes.Timelines.Where(t => t.Name == MemoSceneName).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Multiple memo scenes already exist.");
        if (matches.Length == 1) return matches[0];

        var before = scenes.Timelines.ToArray();
        createNewScene.Invoke(model, null);
        var after = scenes.Timelines.ToArray();
        var created = after.Single(t => !before.Any(x => ReferenceEquals(x, t)));
        created.Name = MemoSceneName;
        return created;
    }

    private static VideoItem NewVideo(string path, int frame, int layer, int length, double offsetSeconds, string remark, int fps)
    {
        var item = new VideoItem
        {
            FilePath = path,
            Frame = frame,
            Layer = layer,
            Length = length,
            ContentOffset = TimeSpan.FromSeconds(offsetSeconds),
            Remark = remark
        };
        item.PlaybackRate2.SetFirstValue(100);
        item.PlaybackRate2.SetAnimationParameters(length, fps);
        return item;
    }

    private static Timeline? GetTimeline(object active)
        => active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;

    private static Timeline? GetActiveTimeline(object root)
    {
        var active = root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
        return active == null ? null : GetTimeline(active);
    }

    private static Guid GetTimelineId(Timeline timeline)
    {
        var p = timeline.GetType().GetProperty("ID", BindingFlags.Instance | BindingFlags.Public)
            ?? timeline.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
        return p?.GetValue(timeline) is Guid id ? id : Guid.Empty;
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
                int n = 0;
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

    private static async Task Idle()
        => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static bool SamePath(string? a, string b)
        => a != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private sealed record VideoSnapshot(int Frame, int Layer, int Length, double Offset, double Rate, string FilePath, string Remark);

    private static VideoSnapshot Snapshot(VideoItem v, int fps)
        => new(v.Frame, v.Layer, v.Length, v.ContentOffset.TotalSeconds, v.PlaybackRate2.GetValue(0, v.Length, fps),
            Path.GetFullPath(v.FilePath ?? ""), v.Remark ?? "");

    private static void Check(string id, bool passed)
    {
        requirements.Add(new { id, passed });
        File.AppendAllText(Path.Combine(output, "assertions.txt"), $"ASSERT {(passed ? "PASS" : "FAIL")} {id}\n");
        if (!passed) throw new InvalidOperationException("Assertion failed: " + id);
    }

    private static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            schema = "cnwl.highlight-memo-scene.v1",
            status,
            host = "4.56.1.0 Lite",
            sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            requirements,
            error
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
