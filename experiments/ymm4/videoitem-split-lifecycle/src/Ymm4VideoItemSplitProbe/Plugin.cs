using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VideoItemSplitProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VideoItem Split Lifecycle";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static MethodInfo[] SplitMethods()
        => typeof(Timeline).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "SplitSelectedAndGroupedItems" || m.Name == "SplitItems" || m.Name == "SplitPositionItems")
            .OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length).ToArray();

    private static void DumpSurface(object active)
    {
        var type = active.GetType();
        var lines = new List<string> { "ACTIVE " + type.AssemblyQualifiedName };
        foreach (var m in type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(m => m.Name.Contains("Split", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains("Divide", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains("Separate", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains("Cut", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(m => m.MemberType).ThenBy(m => m.Name))
            lines.Add(m.MemberType + " " + m.Name + " :: " + m);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name?.StartsWith("YukkuriMovieMaker", StringComparison.Ordinal) == true))
        {
            Type[] types;
            try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).Cast<Type>().ToArray(); }
            foreach (var t in types)
                foreach (var m in t.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                             .Where(m => m.Name.Contains("Split", StringComparison.OrdinalIgnoreCase)
                                      || m.Name.Contains("Divide", StringComparison.OrdinalIgnoreCase)
                                      || m.Name.Contains("Separate", StringComparison.OrdinalIgnoreCase))
                             .Take(30))
                    lines.Add("ASSEMBLY " + assembly.GetName().Name + " :: " + t.FullName + " :: " + m.MemberType + " " + m.Name);
        }
        File.WriteAllLines(Path.Combine(output, "surface.txt"), lines.Distinct());
    }

    private static async Task RunAsync(object active)
    {
        DumpSurface(active);
        var timeline = FindTimeline(active);
        int fps = timeline.VideoInfo.FPS;
        string media = Environment.GetEnvironmentVariable("CNWL_SPLIT_MEDIA") ?? throw new InvalidOperationException("Fixture path missing.");
        Check("fixture_exists", File.Exists(media));
        Check("fps_positive", fps > 0);

        var item = new VideoItem
        {
            FilePath = media,
            Frame = 300,
            Length = fps * 10,
            Layer = 2,
            ContentOffset = TimeSpan.FromSeconds(5),
            Remark = "CNWL_SPLIT_SOURCE"
        };
        item.PlaybackRate2.SetFirstValue(100);
        item.PlaybackRate2.SetAnimationParameters(item.Length, fps);
        Check("insert_source", timeline.TryAddItems([item], item.Frame, item.Layer));

        int originalFrame = item.Frame, originalLength = item.Length;
        long originalOffset = item.ContentOffset.Ticks;
        string originalPath = item.FilePath ?? throw new InvalidDataException("Inserted item lost its file path.");
        timeline.SelectedItems = ImmutableList.Create<IItem>(item);
        timeline.CurrentFrame = item.Frame + fps * 4;
        Check("selection_before_split", timeline.SelectedItems.Count == 1 && ReferenceEquals(timeline.SelectedItems[0], item));
        Check("playhead_inside_source", timeline.CurrentFrame == item.Frame + fps * 4);

        var methods = SplitMethods();
        File.WriteAllLines(Path.Combine(output, "split-methods.txt"), methods.Select(m => m.Name + " :: " + m + " :: " + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name))));
        var split = methods.FirstOrDefault(m => m.Name == "SplitSelectedAndGroupedItems" && CanMap(m.GetParameters()));
        Check("split_method_discovered", split != null);
        object?[] args = split!.GetParameters().Select(p => Map(p, timeline, item, fps)).ToArray();
        split.Invoke(timeline, args);
        await Task.Delay(250);

        var derived = timeline.Items.OfType<VideoItem>()
            .Where(v => v.FilePath != null
                     && string.Equals(Path.GetFullPath(v.FilePath), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase)
                     && v.Layer == item.Layer
                     && v.Frame < originalFrame + originalLength
                     && v.Frame + v.Length > originalFrame)
            .OrderBy(v => v.Frame).ToArray();

        var snapshot = derived.Select(v => new
        {
            sameReference = ReferenceEquals(v, item),
            v.Frame,
            v.Length,
            v.Layer,
            offsetSeconds = v.ContentOffset.TotalSeconds,
            rate = v.PlaybackRate2.GetValue(0, v.Length, fps),
            v.FilePath,
            v.Remark
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "split.json"), JsonSerializer.Serialize(new
        {
            method = split!.ToString(),
            parameters = split.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name).ToArray(),
            before = new { frame = originalFrame, length = originalLength, offsetTicks = originalOffset, file = originalPath },
            after = snapshot,
            selection = timeline.SelectedItems.OfType<VideoItem>().Select(v => new { sameReference = ReferenceEquals(v, item), v.Frame, v.Length, offsetSeconds = v.ContentOffset.TotalSeconds }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));

        Check("split_created_two_pieces", derived.Length == 2);
        Check("timeline_partition", derived[0].Frame == originalFrame
            && derived[0].Frame + derived[0].Length == derived[1].Frame
            && derived[1].Frame + derived[1].Length == originalFrame + originalLength);
        Check("file_path_preserved", derived.All(v => v.FilePath != null && string.Equals(Path.GetFullPath(v.FilePath), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase)));
        Check("rate_preserved", derived.All(v => Math.Abs(v.PlaybackRate2.GetValue(0, v.Length, fps) - 100) < 0.000001));
        Check("source_partition_100_percent", Math.Abs(derived[0].ContentOffset.TotalSeconds - 5) < 0.000001
            && Math.Abs(derived[1].ContentOffset.TotalSeconds - 9) < 0.000001
            && derived[0].Length == fps * 4
            && derived[1].Length == fps * 6);
        Check("one_piece_keeps_original_reference", derived.Count(v => ReferenceEquals(v, item)) == 1);
    }

    private static bool CanMap(ParameterInfo[] parameters)
        => parameters.All(p =>
            p.HasDefaultValue
            || p.ParameterType == typeof(int)
            || p.ParameterType == typeof(long)
            || p.ParameterType == typeof(IItem)
            || p.ParameterType == typeof(VideoItem)
            || typeof(IEnumerable<IItem>).IsAssignableFrom(p.ParameterType)
            || p.ParameterType == typeof(ImmutableList<IItem>));

    private static object? Map(ParameterInfo p, Timeline timeline, VideoItem item, int fps)
    {
        if (p.HasDefaultValue) return p.DefaultValue;
        string name = p.Name ?? "";
        if (p.ParameterType == typeof(int))
        {
            if (name.Contains("fps", StringComparison.OrdinalIgnoreCase)) return fps;
            if (name.Contains("layer", StringComparison.OrdinalIgnoreCase)) return item.Layer;
            return timeline.CurrentFrame;
        }
        if (p.ParameterType == typeof(long)) return (long)timeline.CurrentFrame;
        if (p.ParameterType == typeof(IItem) || p.ParameterType == typeof(VideoItem)) return item;
        if (p.ParameterType == typeof(ImmutableList<IItem>)) return timeline.SelectedItems;
        if (typeof(IEnumerable<IItem>).IsAssignableFrom(p.ParameterType)) return timeline.SelectedItems;
        throw new NotSupportedException("Unsupported split parameter: " + p.ParameterType.FullName + " " + p.Name);
    }

    private static void Check(string id, bool passed)
    {
        requirements.Add(new { id, passed });
        File.AppendAllText(Path.Combine(output, "assertions.txt"), $"ASSERT {(passed ? "PASS" : "FAIL")} {id}\n");
        if (!passed) throw new InvalidOperationException("Assertion failed: " + id);
    }

    private static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            schema = "cnwl.videoitem-split-lifecycle.v1",
            status,
            host = "4.56.1.0 Lite",
            sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            requirements,
            error
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
