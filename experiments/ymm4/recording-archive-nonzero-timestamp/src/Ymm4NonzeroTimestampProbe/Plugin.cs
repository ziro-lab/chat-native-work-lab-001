using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NonzeroTimestampProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Nonzero Timestamp";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static string fixture = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_NONZERO_TS_DIR");
        var media = Environment.GetEnvironmentVariable("CNWL_YMM4_NONZERO_TS_FIXTURE");
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
        var lastCreateAttempt = -100;
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
                    if (active == null)
                    {
                        // Startup can expose MainViewModel before its first Timeline is ready.
                        // Retry CreateProject at a bounded cadence rather than assuming one early call must succeed.
                        if (ticks - lastCreateAttempt >= 8)
                        {
                            lastCreateAttempt = ticks;
                            root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                            Append($"CREATE_PROJECT_RETRY tick={ticks}");
                        }
                        break;
                    }

                    var timeline = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (timeline == null) continue;
                    timer.Stop();
                    await RunAsync(timeline);
                    return;
                }

                if (ticks >= 180) throw new TimeoutException("YMM4 timeline did not become ready after bounded CreateProject retries.");
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                File.WriteAllLines(Path.Combine(output, "result.txt"),
                    ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message],
                    new UTF8Encoding(false));
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(Timeline timeline)
    {
        if (!File.Exists(fixture)) throw new FileNotFoundException("fixture missing", fixture);

        var item = new VideoItem(fixture) { Frame = 0, Layer = 10, Remark = "CNWL_NONZERO_TS" };
        if (!timeline.TryAddItems([item], 0, 10)) throw new InvalidOperationException("VideoItem add failed");

        // Let the host media loader run without blocking the dispatcher. This is an observation:
        // zero length is a valid measured result, but the probe itself must not hang forever.
        for (var i = 0; i < 80 && item.ContentLength <= TimeSpan.Zero; i++)
            await Task.Delay(100);

        Append("FILEPATH=" + item.FilePath);
        Append("CONTENT_LENGTH=" + item.ContentLength.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));
        Append("ORIGINAL_CONTENT_LENGTH=" + item.OriginalContentLength.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));

        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=PASS_NONZERO_TIMESTAMP_OBSERVATION",
            "content_length=" + item.ContentLength.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
            "original_content_length=" + item.OriginalContentLength.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
            "content_length_positive=" + (item.ContentLength > TimeSpan.Zero)
        ], new UTF8Encoding(false));
    }

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "nonzero-timestamp.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
