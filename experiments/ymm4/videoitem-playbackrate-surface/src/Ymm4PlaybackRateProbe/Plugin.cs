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
            Append("VIDEOITEM PlaybackRate=" + item.PlaybackRate.ToString(CultureInfo.InvariantCulture));
            Append("VIDEOITEM ContentLength=" + item.ContentLength);
            Append("VIDEOITEM OriginalContentLength=" + item.OriginalContentLength);

            var p2 = item.PlaybackRate2;
            Assert(p2 != null, "PlaybackRate2 instance exists");
            var type = p2.GetType();
            Append("PLAYBACKRATE2 type=" + type.AssemblyQualifiedName);

            var publicProperties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(x => x.Name).ToArray();
            foreach (var p in publicProperties)
                Append($"PROPERTY {p.PropertyType.FullName} {p.Name} read={p.CanRead} write={p.CanWrite}");

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => !m.IsSpecialName)
                .OrderBy(m => m.Name)
                .ThenBy(m => m.GetParameters().Length)
                .ToArray();
            foreach (var m in methods)
                Append($"METHOD {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))})");

            var getValue = methods.Where(m => m.Name == "GetValue").ToArray();
            var setValue = methods.Where(m => m.Name.Contains("Value", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Constant", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert(getValue.Length > 0, "PlaybackRate2 exposes at least one public GetValue overload");

            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_VIDEOITEM_PLAYBACKRATE_SURFACE",
                "playback_rate_default=" + item.PlaybackRate.ToString(CultureInfo.InvariantCulture),
                "playback_rate2_type=" + type.FullName,
                "public_getvalue_overloads=" + getValue.Length,
                "value_related_methods=" + setValue.Length
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

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "surface.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
