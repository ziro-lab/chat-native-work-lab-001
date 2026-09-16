using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4MediaLengthRateProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — VideoItem Media Length Rate Observation";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_MEDIA_LENGTH_RATE_DIR");
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
                    if (timeline == null) continue;
                    timer.Stop();
                    await RunAsync(timeline);
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

    private static async Task RunAsync(Timeline timeline)
    {
        var media = Environment.GetEnvironmentVariable("CNWL_YMM4_MEDIA_FIXTURE");
        if (string.IsNullOrWhiteSpace(media) || !File.Exists(media)) throw new InvalidOperationException("Media fixture missing.");
        media = Path.GetFullPath(media);
        var fps = GetTimelineFps(timeline);
        Assert(fps > 0, "timeline FPS is positive");
        Append("MEDIA " + media);
        Append("TIMELINE_FPS " + fps);

        var rows = new List<Row>();
        var rates = new[] { 50d, 100d, 200d };
        var offsets = new[] { 0d, 5d };
        var layer = 10;
        foreach (var offset in offsets)
        {
            foreach (var rate in rates)
            {
                var item = new VideoItem(media)
                {
                    Frame = 0,
                    Layer = layer,
                    ContentOffset = TimeSpan.FromSeconds(offset),
                    Remark = $"CNWL_MEDIA_RATE_{rate:0}_OFFSET_{offset:0}"
                };
                item.PlaybackRate2.SetFirstValue(rate);
                item.PlaybackRate2.SetAnimationParameters(Math.Max(item.Length, 1), fps);
                Assert(timeline.TryAddItems([item], item.Frame, item.Layer), $"fixture added rate={rate:0} offset={offset:0}");
                layer += 10;
                await Idle();
                await Idle();

                var original = item.OriginalContentLength.TotalSeconds;
                var content = item.ContentLength.TotalSeconds;
                rows.Add(new Row(rate, offset, original, content));
                Append($"CASE rate={rate:R} offset={offset:R} original={original:R} content={content:R}");
                Assert(original > 0, $"OriginalContentLength is positive for rate={rate:0} offset={offset:0}");
                Assert(content > 0, $"ContentLength is positive for rate={rate:0} offset={offset:0}");
            }
        }

        var originalMin = rows.Min(x => x.Original);
        var originalMax = rows.Max(x => x.Original);
        var contentMin = rows.Min(x => x.Content);
        var contentMax = rows.Max(x => x.Content);
        Assert(originalMax - originalMin < 0.05, "OriginalContentLength is invariant across tested rate/offset fixtures");
        Assert(contentMax - contentMin < 0.05, "ContentLength is invariant across tested rate/offset fixtures");
        Assert(rows.All(x => Math.Abs(x.Content - x.Original) < 0.05), "ContentLength remains approximately equal to media duration in every tested case");

        WriteResult("PASS_MEDIA_LENGTH_RATE_INVARIANCE", $"cases={rows.Count};duration={rows[0].Original:R}");
    }

    private static int GetTimelineFps(Timeline timeline)
    {
        var videoInfo = timeline.GetType().GetProperty("VideoInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(timeline)
            ?? throw new InvalidOperationException("Timeline.VideoInfo missing.");
        var fpsValue = videoInfo.GetType().GetProperty("FPS", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(videoInfo)
            ?? throw new InvalidOperationException("VideoInfo.FPS missing.");
        return Convert.ToInt32(fpsValue, CultureInfo.InvariantCulture);
    }

    private static async Task Idle() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void WriteResult(string status, string detail)
    {
        File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=" + status, "detail=" + detail], new UTF8Encoding(false));
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "mapping.txt"), line + Environment.NewLine, new UTF8Encoding(false));

    private sealed record Row(double Rate, double Offset, double Original, double Content);
}
