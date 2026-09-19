using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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

            var item = new VideoItem(fixture) { Length = 180, Frame = 0, Layer = 10, Remark = "CNWL_NONZERO_TS" };
            for (var i = 0; i < 120 && item.ContentLength <= TimeSpan.Zero; i++)
                await Task.Delay(100);

            var duration = item.ContentLength.TotalSeconds;
            var originalDuration = item.OriginalContentLength.TotalSeconds;

            item.PlaybackRate2.SetFirstValue(100);
            item.PlaybackRate2.SetAnimationParameters(item.Length, 60);
            var mapProperty = typeof(VideoItem).GetProperty("PlaybackRateMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException("PlaybackRateMap");
            var map = mapProperty.GetValue(item) ?? throw new InvalidOperationException("map null");
            var getSource = map.GetType().GetMethod("GetSourceTime",
                [typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan)])
                ?? throw new MissingMethodException("GetSourceTime");

            double Source(double itemSeconds, double offset)
            {
                var raw = getSource.Invoke(map,
                    [TimeSpan.FromSeconds(itemSeconds), item.Length, 60, TimeSpan.FromSeconds(offset), item.ContentLength]);
                return raw is TimeSpan value ? value.TotalSeconds : double.NaN;
            }

            var modelSamples = new[] { Source(0, 0), Source(1, 0), Source(2, 0) };
            var offsetOneAnchor = Source(0, 1);
            var modelZeroBased = Math.Abs(modelSamples[0]) < 1e-9
                && Math.Abs(modelSamples[1] - 1) < 1e-7
                && Math.Abs(modelSamples[2] - 2) < 1e-7
                && Math.Abs(offsetOneAnchor - 1) < 1e-7;

            var containerStart = ProbeContainerStart();

            Append("FILEPATH=" + item.FilePath);
            Append("CONTAINER_START=" + containerStart.ToString("R", CultureInfo.InvariantCulture));
            Append("CONTENT_LENGTH=" + duration.ToString("R", CultureInfo.InvariantCulture));
            Append("ORIGINAL_CONTENT_LENGTH=" + originalDuration.ToString("R", CultureInfo.InvariantCulture));
            Append("MODEL_SOURCE_SAMPLES=" + Join(modelSamples));
            Append("MODEL_OFFSET1_ANCHOR=" + offsetOneAnchor.ToString("R", CultureInfo.InvariantCulture));

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_NONZERO_TIMESTAMP_OBSERVATION",
                "container_start=" + containerStart.ToString("R", CultureInfo.InvariantCulture),
                "content_length=" + duration.ToString("R", CultureInfo.InvariantCulture),
                "original_content_length=" + originalDuration.ToString("R", CultureInfo.InvariantCulture),
                "content_length_positive=" + (item.ContentLength > TimeSpan.Zero),
                "playback_map_media_relative_zero_based=" + modelZeroBased,
                "model_source_samples=" + Join(modelSamples),
                "offset_one_anchor=" + offsetOneAnchor.ToString("R", CultureInfo.InvariantCulture)
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

    private static double ProbeContainerStart()
    {
        var ffprobe = Directory.GetFiles(AppContext.BaseDirectory, "ffprobe.exe", SearchOption.AllDirectories).SingleOrDefault()
            ?? throw new FileNotFoundException("bundled ffprobe missing below YMM4 base directory");
        var psi = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[]
        {
            "-v","error","-show_entries","format=start_time",
            "-of","default=noprint_wrappers=1:nokey=1",fixture
        }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new IOException("ffprobe start failed");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException(stderr);
        return double.Parse(stdout.Trim(), CultureInfo.InvariantCulture);
    }

    private static string Join(IEnumerable<double> values) =>
        string.Join(",", values.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "nonzero-timestamp.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
