using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ZeroEofProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Zero EOF Boundary";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static string fixture = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_ZERO_EOF_DIR");
        var media = Environment.GetEnvironmentVariable("CNWL_YMM4_ZERO_EOF_FIXTURE");
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
            const int fps = 60;
            var item = new VideoItem(fixture) { Frame = 0, Length = fps * 2, Layer = 10, Remark = "CNWL_ZERO_EOF" };

            for (var i = 0; i < 120 && item.ContentLength <= TimeSpan.Zero; i++)
                await Task.Delay(100);

            var duration = item.ContentLength.TotalSeconds;
            Assert(duration > 0, "ContentLength positive");
            item.PlaybackRate2.SetFirstValue(0);
            item.PlaybackRate2.SetAnimationParameters(item.Length, fps);

            var mapProp = typeof(VideoItem).GetProperty("PlaybackRateMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException("PlaybackRateMap");
            var map = mapProp.GetValue(item) ?? throw new InvalidOperationException("map null");
            var mapMethod = map.GetType().GetMethod("GetSourceTime",
                [typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan)])
                ?? throw new MissingMethodException("GetSourceTime");

            double MapSource(double offset, double itemSeconds)
            {
                var raw = mapMethod.Invoke(map,
                    [TimeSpan.FromSeconds(itemSeconds), item.Length, fps, TimeSpan.FromSeconds(offset), item.ContentLength]);
                return raw is TimeSpan value ? value.TotalSeconds : double.NaN;
            }

            var videoSourceType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes)
                .FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Player.Video.Items.VideoSource")
                ?? throw new TypeLoadException("VideoSource missing");
            var calculate = videoSourceType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .SingleOrDefault(m => m.Name == "CalculateSourceTime" && m.GetParameters().Length == 8)
                ?? throw new MissingMethodException("VideoSource.CalculateSourceTime");

            double NativeSource(double offset)
            {
                object?[] args =
                [
                    map, TimeSpan.Zero, item.Length, fps, TimeSpan.FromSeconds(offset),
                    item.ContentLength, false, TimeSpan.FromSeconds(duration)
                ];
                var raw = calculate.Invoke(null, args);
                return raw is TimeSpan value ? value.TotalSeconds : double.NaN;
            }

            var lastFrame = Math.Max(0, duration - 1d / fps);
            foreach (var offset in new[] { lastFrame, duration })
            {
                var s0 = MapSource(offset, 0);
                var s1 = MapSource(offset, 1);
                Append($"CASE offset={offset:R} source0={s0:R} source1={s1:R}");
                Assert(Math.Abs(s0 - s1) < 1e-7, $"0% freezes source at offset={offset:R}");
            }

            var nativeLast = NativeSource(lastFrame);
            var nativeEof = NativeSource(duration);
            Append("NATIVE_VIDEO_SOURCE_LAST=" + nativeLast.ToString("R", CultureInfo.InvariantCulture));
            Append("NATIVE_VIDEO_SOURCE_EOF=" + nativeEof.ToString("R", CultureInfo.InvariantCulture));

            var ffmpeg = FFmpegResourceLocator.GetFFmpegExePath();
            Assert(File.Exists(ffmpeg), "YMM4 bundled ffmpeg exists");
            var lastRows = FrameHashRows(ffmpeg, fixture, lastFrame);
            var eofRows = FrameHashRows(ffmpeg, fixture, duration);
            Append($"FRAMEHASH last_frame_time={lastFrame:R} rows={lastRows}");
            Append($"FRAMEHASH exact_eof_time={duration:R} rows={eofRows}");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_ZERO_EOF_BOUNDARY_OBSERVATION",
                "duration=" + duration.ToString("R", CultureInfo.InvariantCulture),
                "last_frame_hash_rows=" + lastRows,
                "exact_eof_hash_rows=" + eofRows,
                "exact_eof_decodes_frame=" + (eofRows > 0),
                "video_source_calculated_last=" + nativeLast.ToString("R", CultureInfo.InvariantCulture),
                "video_source_calculated_eof=" + nativeEof.ToString("R", CultureInfo.InvariantCulture),
                "video_source_clamps_eof_before_file_source=" + (nativeEof < duration - 1e-9)
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

    private static int FrameHashRows(string ffmpeg, string path, double time)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[]
        {
            "-v","error","-nostdin","-ss",time.ToString("0.#########", CultureInfo.InvariantCulture),
            "-i",path,"-map","0:v:0","-frames:v","1","-an","-f","framehash","-hash","sha256","-"
        }) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new IOException("ffmpeg start failed");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Append($"FFMPEG t={time:R} exit={process.ExitCode} stderr={stderr.Trim()}");
        return stdout.Split('\n').Select(x => x.Trim()).Count(x => x.Length > 0 && !x.StartsWith('#'));
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("ASSERT FAIL: " + name);
        Append("ASSERT PASS: " + name);
    }

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "zero-eof.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
