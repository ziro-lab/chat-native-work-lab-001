using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
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

    private static async void Start()
    {
        try
        {
            if (!File.Exists(fixture)) throw new FileNotFoundException("fixture missing", fixture);
            var item = new VideoItem(fixture) { Length = 120, Frame = 0, Layer = 10, Remark = "CNWL_NONZERO_TS" };

            // Content metadata is populated asynchronously by the real host. No Timeline is needed
            // for this question; avoiding project creation keeps startup state out of the observation.
            for (var i = 0; i < 120 && item.ContentLength <= TimeSpan.Zero; i++)
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
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"),
                ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message],
                new UTF8Encoding(false));
        }
    }

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "nonzero-timestamp.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
