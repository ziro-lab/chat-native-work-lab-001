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
    public string Name => "Chat Native Work Lab — VideoItem Media Length Rate Mapping";
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

        var cases = new List<Row>();
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
                var normalizedSource = content * rate / 100d;
                var expectedRemaining = original - offset;
                var row = new Row(rate, offset, original, content, normalizedSource, expectedRemaining);
                cases.Add(row);
                Append($"CASE rate={rate:R} offset={offset:R} original={original:R} content={content:R} normalizedSource={normalizedSource:R} expectedRemaining={expectedRemaining:R}");
                Assert(original > 0, $"original media length is positive for rate={rate:0} offset={offset:0}");
                Assert(content > 0, $"content length is positive for rate={rate:0} offset={offset:0}");
            }
        }

        var originalMin = cases.Min(x => x.Original);
        var originalMax = cases.Max(x => x.Original);
        Assert(originalMax - originalMin < 0.05, "OriginalContentLength is consistent across rate/offset fixtures");

        foreach (var offset in offsets)
        {
            var group = cases.Where(x => Math.Abs(x.Offset - offset) < 1e-9).OrderBy(x => x.Rate).ToArray();
            Assert(group[0].Content > group[1].Content && group[1].Content > group[2].Content,
                $"ContentLength decreases as rate increases at offset={offset:0}");
        }
        foreach (var rate in rates)
        {
            var zero = cases.Single(x => Math.Abs(x.Rate - rate) < 1e-9 && Math.Abs(x.Offset) < 1e-9);
            var five = cases.Single(x => Math.Abs(x.Rate - rate) < 1e-9 && Math.Abs(x.Offset - 5) < 1e-9);
            Assert(five.Content < zero.Content, $"5s ContentOffset shortens ContentLength at rate={rate:0}");
        }

        foreach (var row in cases)
        {
            var error = Math.Abs(row.NormalizedSource - row.ExpectedRemaining);
            Append($"MAPPING rate={row.Rate:R} offset={row.Offset:R} errorSeconds={error:R}");
            Assert(error <= 0.10, $"rate/100 duration relation holds within 100ms at rate={row.Rate:0} offset={row.Offset:0}");
        }

        WriteResult("PASS_MEDIA_LENGTH_RATE_MAPPING", $"cases={cases.Count}");
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

    private sealed record Row(double Rate, double Offset, double Original, double Content, double NormalizedSource, double ExpectedRemaining);
}
