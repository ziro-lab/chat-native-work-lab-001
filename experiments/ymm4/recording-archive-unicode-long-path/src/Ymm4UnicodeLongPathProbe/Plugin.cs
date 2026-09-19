using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YmmProject = YukkuriMovieMaker.Project.Project;

namespace Ymm4UnicodeLongPathProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Unicode Long Path";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static string fixture = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_UNICODE_LONG_DIR");
        var media = Environment.GetEnvironmentVariable("CNWL_YMM4_UNICODE_LONG_FIXTURE");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(media)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        fixture = Path.GetFullPath(media);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        var ticks = 0;
        var created = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    var root = window.DataContext;
                    if (root?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
                    if (active == null && !created)
                    {
                        created = true;
                        root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                        break;
                    }
                    var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);
                    var timeline = active?.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (model == null || timeline == null) continue;
                    timer.Stop();
                    await RunAsync(model, timeline);
                    return;
                }
                if (++ticks > 120) throw new TimeoutException("timeline not ready");
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllLines(Path.Combine(output, "result.txt"),
                    ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message], new UTF8Encoding(false));
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object model, Timeline timeline)
    {
        var segment = "日本語 スペース [記号] 🚀_" + new string('長', 90);
        var dir = Path.Combine(output, segment);
        Directory.CreateDirectory(dir);
        var media = Path.Combine(dir, "録画素材 🚀 [01].mp4");
        File.Copy(fixture, media, true);

        var item = new VideoItem(media) { Frame = 0, Layer = 10, Remark = "CNWL_UNICODE_LONG" };
        if (!timeline.TryAddItems([item], 0, 10)) throw new InvalidOperationException("add failed");
        await Idle(); await Idle();
        if (item.ContentLength <= TimeSpan.Zero) throw new InvalidOperationException("ContentLength zero");

        var project = Path.Combine(dir, "作品 アーカイブ 🚀 [長い名前].ymmp");
        var modelType = model.GetType();
        (modelType.GetMethod("SaveProject", [typeof(string)]) ?? throw new MissingMethodException("SaveProject")).Invoke(model, [project]);
        var load = modelType.GetMethod("LoadProjectFile", [typeof(string)]) ?? throw new MissingMethodException("LoadProjectFile");
        var task = load.Invoke(model, [project]) as Task ?? throw new InvalidOperationException("load task");
        await task;
        var loadedProject = task.GetType().GetProperty("Result")?.GetValue(task) as YmmProject
            ?? throw new InvalidOperationException("project result");
        var timelines = GetTimelines(loadedProject);
        var loaded = timelines.SelectMany(x => x.Items).OfType<VideoItem>().Single(x => x.Remark == "CNWL_UNICODE_LONG");
        await Idle();

        var standardPositive = loaded.ContentLength > TimeSpan.Zero;
        var exactPath = string.Equals(Path.GetFullPath(media), Path.GetFullPath(loaded.FilePath ?? ""), StringComparison.OrdinalIgnoreCase);

        var over260Attempted = false;
        var over260Supported = false;
        var over260MediaLength = 0;
        var over260ProjectLength = 0;
        var over260Error = "";
        try
        {
            var deep = output;
            for (var i = 0; i < 4; i++)
                deep = Path.Combine(deep, $"深い階層_{i}_" + new string('深', 70));
            Directory.CreateDirectory(deep);

            var media2 = Path.Combine(deep, "超長い録画素材 🚀.mp4");
            File.Copy(fixture, media2, true);
            var item2 = new VideoItem(media2) { Frame = 300, Layer = 20, Remark = "CNWL_OVER_260" };
            if (!timeline.TryAddItems([item2], item2.Frame, item2.Layer)) throw new InvalidOperationException("over260 add failed");
            for (var i = 0; i < 80 && item2.ContentLength <= TimeSpan.Zero; i++) await Task.Delay(100);

            var project2 = Path.Combine(deep, "超長い作品 アーカイブ 🚀.ymmp");
            over260Attempted = media2.Length > 260 && project2.Length > 260;
            over260MediaLength = media2.Length;
            over260ProjectLength = project2.Length;

            (modelType.GetMethod("SaveProject", [typeof(string)]) ?? throw new MissingMethodException("SaveProject")).Invoke(model, [project2]);
            var task2 = load.Invoke(model, [project2]) as Task ?? throw new InvalidOperationException("load task2");
            await task2;
            var p2 = task2.GetType().GetProperty("Result")?.GetValue(task2) as YmmProject
                ?? throw new InvalidOperationException("project2 result");
            var loaded2 = GetTimelines(p2).SelectMany(x => x.Items).OfType<VideoItem>().Single(x => x.Remark == "CNWL_OVER_260");
            for (var i = 0; i < 80 && loaded2.ContentLength <= TimeSpan.Zero; i++) await Task.Delay(100);

            over260Supported = over260Attempted
                && loaded2.ContentLength > TimeSpan.Zero
                && string.Equals(Path.GetFullPath(media2), Path.GetFullPath(loaded2.FilePath ?? ""), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            over260Error = ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message;
        }

        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=PASS_UNICODE_LONG_PATH",
            "media_path_chars=" + media.Length,
            "project_path_chars=" + project.Length,
            "content_length_positive=" + standardPositive,
            "exact_path_preserved=" + exactPath,
            "over260_attempted=" + over260Attempted,
            "over260_media_path_chars=" + over260MediaLength,
            "over260_project_path_chars=" + over260ProjectLength,
            "over260_supported=" + over260Supported,
            "over260_error=" + over260Error.Replace("\r", " ").Replace("\n", " ")
        ], new UTF8Encoding(false));
    }

    private static Timeline[] GetTimelines(YmmProject project)
    {
        var property = project.GetType().GetProperty("Timelines", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("Timelines");
        return (property.GetValue(project) as IEnumerable<Timeline>)?.ToArray()
            ?? throw new InvalidOperationException("Timelines type changed");
    }

    private static async Task Idle() =>
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
}
