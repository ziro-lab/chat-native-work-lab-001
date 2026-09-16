using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ArchiveRateModesProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Rate Modes Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var output = Environment.GetEnvironmentVariable("CNWL_YMM4_ARCHIVE_RATE_MODES_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var evidence = new StringBuilder();
        var assertions = 0;
        try
        {
            var video = new VideoItem { Length = 300, ContentOffset = TimeSpan.FromSeconds(10) };
            const int fps = 60;
            var animation = video.PlaybackRate2;
            DumpType(evidence, "ANIMATION", animation.GetType());
            DumpMatchingMembers(evidence, "VIDEOITEM_DIRECTION_MEMBERS", typeof(VideoItem),
                "reverse", "backward", "direction", "loop", "ping", "playback");

            var mapProperty = typeof(VideoItem).GetProperty("PlaybackRateMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("PlaybackRateMap property missing");

            foreach (var rate in new[] { 0d, 50d, 100d, 200d })
            {
                animation.SetFirstValue(rate);
                animation.SetAnimationParameters(video.Length, fps);
                var map = mapProperty.GetValue(video) ?? throw new Exception("map missing");
                if (rate == 0) DumpType(evidence, "PLAYBACK_RATE_MAP", map.GetType());
                var getSourceTime = map.GetType().GetMethod("GetSourceTime", new[] { typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan) })
                    ?? throw new Exception("GetSourceTime missing");
                var consumed = map.GetType().GetMethod("GetConsumedContentRange", new[] { typeof(int), typeof(int) })?.Invoke(map, new object[] { video.Length, fps });
                evidence.AppendLine($"RATE {rate}");
                evidence.AppendLine($"  IsConstant={Read(map, "IsConstant")}");
                evidence.AppendLine($"  FirstRate={Read(map, "FirstRate")}");
                if (consumed != null) DumpObject(evidence, "  ConsumedContentRange", consumed);
                foreach (var seconds in new[] { 0d, 1d, 2.5d, 4.9d })
                {
                    var source = (TimeSpan)(getSourceTime.Invoke(map, new object[] { TimeSpan.FromSeconds(seconds), video.Length, fps, video.ContentOffset, TimeSpan.FromSeconds(100) })
                        ?? throw new Exception("source time missing"));
                    evidence.AppendLine($"  t={seconds:F3} source={source.TotalSeconds:F9}");
                    if (rate == 0)
                    {
                        Check(Math.Abs(source.TotalSeconds - 10) < 1e-9, $"zero rate freezes source time at offset for t={seconds}");
                    }
                }
            }

            animation.SetFirstValue(0);
            animation.SetAnimationParameters(video.Length, fps);
            var zeroMap = mapProperty.GetValue(video)!;
            var zeroGet = zeroMap.GetType().GetMethod("GetSourceTime", new[] { typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan) })!;
            static double Get(MethodInfo m, object map, TimeSpan offset) => ((TimeSpan)m.Invoke(map,
                new object[] { TimeSpan.FromSeconds(2), 300, 60, offset, TimeSpan.FromSeconds(100) })!).TotalSeconds;
            Check(Math.Abs((Get(zeroGet, zeroMap, TimeSpan.FromSeconds(10)) - Get(zeroGet, zeroMap, TimeSpan.FromSeconds(3))) - 7) < 1e-9,
                "content offset is additive at zero rate");

            File.WriteAllText(Path.Combine(output, "rate-modes.txt"), evidence.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "result.txt"), $"status=PASS_ARCHIVE_RATE_MODES_SURFACE\nassertions={assertions}\n", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            evidence.AppendLine(ex.ToString());
            File.WriteAllText(Path.Combine(output, "rate-modes.txt"), evidence.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "result.txt"), "status=FAIL\n" + ex.GetBaseException().Message, new UTF8Encoding(false));
        }

        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("ASSERT FAIL: " + name);
            assertions++;
            evidence.AppendLine("PASS: " + name);
        }
    }

    private static object? Read(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);

    private static void DumpType(StringBuilder text, string title, Type type)
    {
        text.AppendLine($"=== {title} {type.FullName} ===");
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            text.AppendLine("CTOR " + Describe(ctor));
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                     .Where(m => m.MemberType is MemberTypes.Method or MemberTypes.Property or MemberTypes.Field)
                     .OrderBy(m => m.MemberType).ThenBy(m => m.Name))
            text.AppendLine(member.MemberType.ToString().ToUpperInvariant() + " " + Describe(member));
    }

    private static void DumpMatchingMembers(StringBuilder text, string title, Type type, params string[] terms)
    {
        text.AppendLine($"=== {title} ===");
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                     .Where(m => terms.Any(t => m.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(m => m.Name))
            text.AppendLine((member is MethodBase mb && mb.IsPublic ? "PUBLIC " : "") + Describe(member));
    }

    private static void DumpObject(StringBuilder text, string title, object value)
    {
        var type = value.GetType();
        text.AppendLine($"{title} type={type.FullName} value={value}");
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object? current;
            try { current = property.GetValue(value); } catch { continue; }
            text.AppendLine($"    {property.Name}={current}");
        }
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            text.AppendLine($"    {field.Name}={field.GetValue(value)}");
    }

    private static string Describe(MemberInfo member) => member switch
    {
        MethodInfo m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})",
        ConstructorInfo c => $"{c.DeclaringType?.Name}({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})",
        PropertyInfo p => $"{p.PropertyType.Name} {p.Name}",
        FieldInfo f => $"{f.FieldType.Name} {f.Name}",
        _ => member.Name
    };
}
