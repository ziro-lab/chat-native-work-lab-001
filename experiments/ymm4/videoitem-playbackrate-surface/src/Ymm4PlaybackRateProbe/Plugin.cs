using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4PlaybackRateProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — VideoItem PlaybackRate Surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PLAYBACK_RATE_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.ApplicationIdle);
    }

    private static void Run()
    {
        try
        {
            var item = new VideoItem();
            Append("HOST assembly=" + typeof(VideoItem).Assembly.FullName);

            var legacy = typeof(VideoItem).GetProperty("PlaybackRate", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item);
            Append("VIDEOITEM legacy PlaybackRate via reflection=" + Convert.ToString(legacy, CultureInfo.InvariantCulture));
            Append("VIDEOITEM ContentLength=" + item.ContentLength);
            Append("VIDEOITEM OriginalContentLength=" + item.OriginalContentLength);

            var p2 = item.PlaybackRate2 ?? throw new InvalidOperationException("PlaybackRate2 instance missing.");
            var type = p2.GetType();
            Append("PLAYBACKRATE2 type=" + type.AssemblyQualifiedName);

            var publicProperties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(x => x.Name).ToArray();
            foreach (var p in publicProperties)
            {
                object? value = null;
                var readable = p.CanRead && p.GetIndexParameters().Length == 0;
                if (readable)
                {
                    try { value = p.GetValue(p2); } catch { }
                }
                Append($"PROPERTY {p.PropertyType.FullName} {p.Name} read={p.CanRead} write={p.CanWrite} value={Safe(value)}");
            }

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => !m.IsSpecialName)
                .OrderBy(m => m.Name)
                .ThenBy(m => m.GetParameters().Length)
                .ToArray();
            foreach (var m in methods)
                Append($"METHOD {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))})");

            var getValue = methods.Where(m => m.Name == "GetValue").ToArray();
            Assert(getValue.Any(m => m.ReturnType == typeof(double) && m.GetParameters().Select(x => x.ParameterType).SequenceEqual([typeof(long), typeof(long), typeof(int)])),
                "PlaybackRate2 exposes public double GetValue(long,long,int)");

            var defaultValue = p2.GetValue(0, 1, 60);
            Append("PLAYBACKRATE2 default GetValue(0,1,60)=" + defaultValue.ToString(CultureInfo.InvariantCulture));
            Assert(defaultValue > 0, "PlaybackRate2 default evaluates to a positive rate");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_VIDEOITEM_PLAYBACKRATE_SURFACE",
                "legacy_playback_rate=" + Convert.ToString(legacy, CultureInfo.InvariantCulture),
                "playback_rate2_type=" + type.FullName,
                "playback_rate2_default_value=" + defaultValue.ToString(CultureInfo.InvariantCulture),
                "public_getvalue_overloads=" + getValue.Length
            ], new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=FAIL_EXCEPTION",
                "detail=" + ex.GetBaseException().Message
            ], new UTF8Encoding(false));
        }
    }

    private static string Safe(object? value)
    {
        if (value == null) return "<null>";
        if (value is string s) return '"' + s + '"';
        if (value is IEnumerable<object> seq) return "[" + string.Join(",", seq.Take(8)) + "]";
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null-string>";
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
