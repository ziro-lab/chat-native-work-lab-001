using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ConstantRateProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — VideoItem Constant PlaybackRate2 Roundtrip";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_CONSTANT_RATE_DIR");
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

    private static async Task RunAsync(object model, Timeline timeline)
    {
        timeline.Name = "ConstantRateMain";
        var fps = GetTimelineFps(timeline);
        Assert(fps > 0, "timeline FPS is positive");
        Append("FPS " + fps);

        var expectedOffset = TimeSpan.FromSeconds(12.345);
        var expected = new[] { 50d, 100d, 200d };
        for (var i = 0; i < expected.Length; i++)
        {
            var rate = expected[i];
            var item = new VideoItem
            {
                Frame = 120,
                Length = 600,
                Layer = 10 + i * 10,
                ContentOffset = expectedOffset,
                Remark = $"CNWL_RATE_{rate:0}"
            };
            item.PlaybackRate2.SetFirstValue(rate);
            item.PlaybackRate2.SetAnimationParameters(item.Length, fps);
            AssertNear(item.PlaybackRate2.GetValue(0, item.Length, fps), rate, $"{rate:0}% evaluates at start before save");
            AssertNear(item.PlaybackRate2.GetValue(item.Length / 2, item.Length, fps), rate, $"{rate:0}% evaluates at middle before save");
            AssertNear(item.PlaybackRate2.GetValue(item.Length - 1, item.Length, fps), rate, $"{rate:0}% evaluates at end before save");
            Assert(timeline.TryAddItems([item], item.Frame, item.Layer), $"{rate:0}% fixture added");
            var timelineSeconds = item.Length / (double)fps;
            var expectedSourceSeconds = timelineSeconds * rate / 100d;
            Append($"EXPECTED rate={rate:0} timelineSeconds={timelineSeconds:R} sourceSecondsIfRateRatio={expectedSourceSeconds:R}");
        }

        var modelType = model.GetType();
        var save = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainModel.SaveProject(string) public surface missing.");
        var load = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string))
            ?? throw new InvalidOperationException("MainModel.LoadProjectFile(string) public surface missing.");

        var projectPath = Path.Combine(output, "constant-rate.ymmp");
        save.Invoke(model, [projectPath]);
        Assert(File.Exists(projectPath), "native project save created fixture");

        var task = load.Invoke(model, [projectPath]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var loadedProject = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert(loadedProject != null, "native project reload returned Project");

        var loadedTimelines = CollectTimelines(loadedProject!).DistinctBy(GetTimelineIdOrName).ToList();
        var loadedTimeline = loadedTimelines.Single(x => x.Name == "ConstantRateMain");
        var loadedFps = GetTimelineFps(loadedTimeline);
        Assert(loadedFps == fps, "timeline FPS survives roundtrip");

        foreach (var rate in expected)
        {
            var item = loadedTimeline.Items.OfType<VideoItem>().Single(x => x.Remark == $"CNWL_RATE_{rate:0}");
            Assert(item.ContentOffset == expectedOffset, $"{rate:0}% ContentOffset survives roundtrip");
            Assert(item.Length == 600, $"{rate:0}% Length survives roundtrip");
            item.PlaybackRate2.SetAnimationParameters(item.Length, loadedFps);
            AssertNear(item.PlaybackRate2.GetValue(0, item.Length, loadedFps), rate, $"{rate:0}% evaluates at start after reload");
            AssertNear(item.PlaybackRate2.GetValue(item.Length / 2, item.Length, loadedFps), rate, $"{rate:0}% evaluates at middle after reload");
            AssertNear(item.PlaybackRate2.GetValue(item.Length - 1, item.Length, loadedFps), rate, $"{rate:0}% evaluates at end after reload");
        }

        WriteResult("PASS_CONSTANT_PLAYBACKRATE2_ROUNDTRIP", projectPath);
    }

    private static int GetTimelineFps(Timeline timeline)
    {
        var videoInfo = timeline.GetType().GetProperty("VideoInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(timeline)
            ?? throw new InvalidOperationException("Timeline.VideoInfo missing.");
        var fpsValue = videoInfo.GetType().GetProperty("FPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(videoInfo)
            ?? throw new InvalidOperationException("VideoInfo.FPS missing.");
        return Convert.ToInt32(fpsValue, CultureInfo.InvariantCulture);
    }

    private static string GetTimelineIdOrName(Timeline timeline)
    {
        foreach (var name in new[] { "ID", "Id", "SceneId", "Guid" })
        {
            var p = timeline.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.PropertyType == typeof(Guid) && p.GetIndexParameters().Length == 0)
            {
                var value = (Guid)(p.GetValue(timeline) ?? Guid.Empty);
                if (value != Guid.Empty) return value.ToString("D");
            }
        }
        return "name:" + timeline.Name;
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

    private static void WriteResult(string status, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=" + status, "detail=" + detail], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "roundtrip.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
