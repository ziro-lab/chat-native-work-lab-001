using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4PlaybackRateMapProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — PlaybackRateMap Source Time";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_PLAYBACK_RATE_MAP_DIR");
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
            const int fps = 60;
            const int length = 300;
            var offset = TimeSpan.FromSeconds(4);
            var contentLength = TimeSpan.FromSeconds(100);
            var rates = new[] { 50d, 100d, 200d };
            var interiorItemTimes = new[] { 0d, 1d, 2.5d, 4.999d };
            var itemEndSeconds = length / (double)fps;

            var mapProperty = typeof(VideoItem).GetProperty("PlaybackRateMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("BaseItem.PlaybackRateMap property missing.");
            Append($"MAP_PROPERTY declaring={mapProperty.DeclaringType?.FullName} getterPublic={mapProperty.GetMethod?.IsPublic} getterNonPublic={mapProperty.GetGetMethod(true)?.IsPublic == false}");
            Assert(mapProperty.GetGetMethod(true) != null, "PlaybackRateMap getter exists");

            Type? mapType = null;
            MethodInfo? getSourceTime = null;
            MethodInfo? findFirst = null;
            PropertyInfo? isConstant = null;
            PropertyInfo? firstRate = null;

            foreach (var rate in rates)
            {
                var item = new VideoItem { Length = length, ContentOffset = offset, Remark = $"CNWL_MAP_{rate:0}" };
                item.PlaybackRate2.SetFirstValue(rate);
                item.PlaybackRate2.SetAnimationParameters(length, fps);

                var map = mapProperty.GetValue(item) ?? throw new InvalidOperationException("PlaybackRateMap getter returned null.");
                mapType ??= map.GetType();
                Assert(map.GetType() == mapType, "PlaybackRateMap runtime type is stable");

                if (getSourceTime == null)
                {
                    Append("MAP_TYPE " + mapType.AssemblyQualifiedName);
                    foreach (var p in mapType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(x => x.Name))
                        Append($"PROPERTY access={(p.GetMethod?.IsPublic == true ? "public" : "nonpublic")} {p.PropertyType.FullName} {p.Name}");
                    foreach (var m in mapType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => !m.IsSpecialName).OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length))
                        Append($"METHOD access={(m.IsPublic ? "public" : "nonpublic")} {m.ReturnType.FullName} {m.Name}({string.Join(",", m.GetParameters().Select(x => x.ParameterType.FullName + " " + x.Name))})");

                    getSourceTime = mapType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .SingleOrDefault(m => m.Name == "GetSourceTime" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan)]))
                        ?? throw new InvalidOperationException("PlaybackRateMap.GetSourceTime(TimeSpan,int,int,TimeSpan,TimeSpan) missing.");
                    findFirst = mapType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .SingleOrDefault(m => m.Name == "FindFirstTimeForSourceTime" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan)]))
                        ?? throw new InvalidOperationException("PlaybackRateMap.FindFirstTimeForSourceTime(...) missing.");
                    isConstant = mapType.GetProperty("IsConstant", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("PlaybackRateMap.IsConstant missing.");
                    firstRate = mapType.GetProperty("FirstRate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("PlaybackRateMap.FirstRate missing.");
                    Append($"AUTHORITY GetSourceTimePublic={getSourceTime.IsPublic} FindFirstPublic={findFirst.IsPublic} IsConstantPublic={isConstant.GetMethod?.IsPublic} FirstRatePublic={firstRate.GetMethod?.IsPublic}");
                }

                Assert((bool)(isConstant!.GetValue(map) ?? false), $"{rate:0}% map reports IsConstant");
                AssertNear(Convert.ToDouble(firstRate!.GetValue(map), CultureInfo.InvariantCulture), rate, $"{rate:0}% map FirstRate matches PlaybackRate2");

                foreach (var itemSeconds in interiorItemTimes)
                {
                    var itemTime = TimeSpan.FromSeconds(itemSeconds);
                    var expectedSource = TimeSpan.FromSeconds(offset.TotalSeconds + itemSeconds * rate / 100d);
                    var actualSource = (TimeSpan)(getSourceTime!.Invoke(map, [itemTime, length, fps, offset, contentLength])
                        ?? throw new InvalidOperationException("GetSourceTime returned null."));
                    AssertNear(actualSource.TotalSeconds, expectedSource.TotalSeconds, $"{rate:0}% GetSourceTime item={itemSeconds:R}s");

                    var inverse = findFirst!.Invoke(map, [actualSource, length, fps, offset, contentLength]);
                    if (inverse is not TimeSpan inverseTime) throw new InvalidOperationException($"FindFirstTimeForSourceTime returned null for interior rate={rate:R}, item={itemSeconds:R}.");
                    AssertNear(inverseTime.TotalSeconds, itemTime.TotalSeconds, $"{rate:0}% inverse mapping item={itemSeconds:R}s");
                }

                var endTime = TimeSpan.FromSeconds(itemEndSeconds);
                var expectedEndSource = TimeSpan.FromSeconds(offset.TotalSeconds + itemEndSeconds * rate / 100d);
                var actualEndSource = (TimeSpan)(getSourceTime!.Invoke(map, [endTime, length, fps, offset, contentLength])
                    ?? throw new InvalidOperationException("GetSourceTime(end) returned null."));
                AssertNear(actualEndSource.TotalSeconds, expectedEndSource.TotalSeconds, $"{rate:0}% GetSourceTime accepts exact item end");
                var endInverse = findFirst!.Invoke(map, [actualEndSource, length, fps, offset, contentLength]);
                Assert(endInverse == null, $"{rate:0}% inverse lookup treats exact item end as outside half-open item range");
            }

            Assert(getSourceTime!.IsPublic, "PlaybackRateMap.GetSourceTime is public once the map instance is obtained");
            Assert(findFirst!.IsPublic, "PlaybackRateMap.FindFirstTimeForSourceTime is public once the map instance is obtained");
            File.WriteAllLines(Path.Combine(output, "result.txt"),
            [
                "status=PASS_PLAYBACKRATEMAP_SOURCE_TIME",
                "map_property_public=" + (mapProperty.GetMethod?.IsPublic == true),
                "get_source_time_public=" + getSourceTime.IsPublic,
                "find_first_time_public=" + findFirst.IsPublic,
                "constant_rates_verified=50,100,200",
                "content_offset_source_time_seconds=4",
                "inverse_item_domain=half-open"
            ], new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Append("ERROR " + ex);
            File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=FAIL_EXCEPTION", "detail=" + ex.GetBaseException().Message], new UTF8Encoding(false));
        }
    }

    private static void AssertNear(double actual, double expected, string message)
    {
        if (Math.Abs(actual - expected) > 1e-6) throw new InvalidOperationException($"ASSERT FAIL: {message}; actual={actual:R}, expected={expected:R}");
        Append($"ASSERT PASS: {message}; value={actual:R}");
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        Append("ASSERT PASS: " + message);
    }

    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "map.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
