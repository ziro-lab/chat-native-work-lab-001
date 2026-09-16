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
            evidence.AppendLine($"ANIMATION_DEFAULTS Default={animation.DefaultValue} Min={animation.MinValue} Max={animation.MaxValue} Loop={animation.Loop} First={animation.GetFirstValue()} Type={animation.AnimationType}");
            evidence.AppendLine("ANIMATION_TYPE_ENUM=" + string.Join(",", Enum.GetNames(animation.AnimationType.GetType())));
            if (animation.KeyFrames != null) DumpType(evidence, "KEYFRAMES", animation.KeyFrames.GetType());
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
                var getSourceTime = GetSourceMethod(map);
                var consumed = GetConsumedRange(map, video.Length, fps);
                evidence.AppendLine($"RATE {rate}");
                evidence.AppendLine($"  IsConstant={Read(map, "IsConstant")}");
                evidence.AppendLine($"  FirstRate={Read(map, "FirstRate")}");
                if (consumed != null) DumpObject(evidence, "  ConsumedContentRange", consumed);
                foreach (var seconds in new[] { 0d, 1d, 2.5d, 4.9d })
                {
                    var source = Source(getSourceTime, map, seconds, 10);
                    evidence.AppendLine($"  t={seconds:F3} source={source:F9}");
                    if (rate == 0) Check(Math.Abs(source - 10) < 1e-9, $"zero rate freezes source time at offset for t={seconds}");
                }
            }

            animation.SetFirstValue(0);
            animation.SetAnimationParameters(video.Length, fps);
            var zeroMap = mapProperty.GetValue(video)!;
            var zeroGet = GetSourceMethod(zeroMap);
            Check(Math.Abs((Source(zeroGet, zeroMap, 2, 10) - Source(zeroGet, zeroMap, 2, 3)) - 7) < 1e-9,
                "content offset is additive at zero rate");

            evidence.AppendLine("=== NEGATIVE_RATE_ATTEMPT ===");
            try
            {
                animation.SetFirstValue(-100);
                animation.SetAnimationParameters(video.Length, fps);
                evidence.AppendLine($"negative First={animation.GetFirstValue()} HasErrors={animation.HasErrors} Min={animation.MinValue}");
                var negativeMap = mapProperty.GetValue(video)!;
                evidence.AppendLine($"negative map IsConstant={Read(negativeMap, "IsConstant")} FirstRate={Read(negativeMap, "FirstRate")}");
                var negGet = GetSourceMethod(negativeMap);
                foreach (var seconds in new[] { 0d, 1d, 2.5d, 4.9d })
                    evidence.AppendLine($"negative t={seconds:F3} source={Source(negGet, negativeMap, seconds, 10):F9}");
                var consumed = GetConsumedRange(negativeMap, video.Length, fps);
                if (consumed != null) DumpObject(evidence, "negative ConsumedContentRange", consumed);
            }
            catch (Exception ex)
            {
                evidence.AppendLine("negative rejected: " + ex.GetBaseException().GetType().FullName + ": " + ex.GetBaseException().Message);
            }

            evidence.AppendLine("=== ANIMATION_TYPES_FROM50_TO200 ===");
            var typeProperty = animation.GetType().GetProperty("AnimationType") ?? throw new Exception("AnimationType property missing");
            foreach (var enumValue in Enum.GetValues(animation.AnimationType.GetType()).Cast<object>())
            {
                try
                {
                    animation.SetFirstValue(100);
                    animation.From = 50;
                    animation.To = 200;
                    typeProperty.SetValue(animation, enumValue);
                    animation.SetAnimationParameters(video.Length, fps);
                    evidence.AppendLine($"ANIM {enumValue} values=" + string.Join(",", new[] { 0L, 60L, 150L, 240L, 299L }.Select(f => animation.GetValue(f, video.Length, fps).ToString("F6", CultureInfo.InvariantCulture))));
                    var map = mapProperty.GetValue(video)!;
                    evidence.AppendLine($"  map IsConstant={Read(map, "IsConstant")} FirstRate={Read(map, "FirstRate")}");
                    var consumed = GetConsumedRange(map, video.Length, fps);
                    if (consumed != null) DumpObject(evidence, "  ConsumedContentRange", consumed);
                    var get = GetSourceMethod(map);
                    foreach (var seconds in new[] { 0d, 1d, 2.5d, 4.9d })
                    {
                        var at10 = Source(get, map, seconds, 10);
                        var at3 = Source(get, map, seconds, 3);
                        evidence.AppendLine($"  t={seconds:F3} source10={at10:F9} source3={at3:F9} delta={at10 - at3:F9}");
                        Check(Math.Abs((at10 - at3) - 7) < 1e-7, $"content offset stays additive for animation {enumValue} at t={seconds}");
                    }
                }
                catch (Exception ex)
                {
                    evidence.AppendLine($"ANIM {enumValue} ERROR {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                }
            }

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

    private static MethodInfo GetSourceMethod(object map) => map.GetType().GetMethod("GetSourceTime", new[] { typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan) })
        ?? throw new Exception("GetSourceTime missing");

    private static double Source(MethodInfo method, object map, double itemSeconds, double offsetSeconds) =>
        ((TimeSpan)(method.Invoke(map, new object[] { TimeSpan.FromSeconds(itemSeconds), 300, 60, TimeSpan.FromSeconds(offsetSeconds), TimeSpan.FromSeconds(1000) })
            ?? throw new Exception("source time missing"))).TotalSeconds;

    private static object? GetConsumedRange(object map, int length, int fps) =>
        map.GetType().GetMethod("GetConsumedContentRange", new[] { typeof(int), typeof(int) })?.Invoke(map, new object[] { length, fps });

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
