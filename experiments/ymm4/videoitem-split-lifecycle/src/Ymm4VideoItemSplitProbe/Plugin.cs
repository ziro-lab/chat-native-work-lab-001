using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
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
    private static bool scheduled;
    private static string output = "";
    private static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        string? value = Environment.GetEnvironmentVariable("CNWL_SPLIT_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(value)) return;
        scheduled = true;
        output = Path.GetFullPath(value);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    private static void Start()
    {
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                var main = Application.Current.Windows.Cast<Window>().Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (main == null) return;
                var active = main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                if (active == null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                    }
                    return;
                }
                timer.Stop();
                await RunAsync(active);
                Write("PASS_VIDEOITEM_SPLIT_LIFECYCLE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VIDEOITEM_SPLIT_LIFECYCLE", ex.ToString());
            }
        };
        timer.Start();
    }

    private static Timeline FindTimeline(object active)
    {
        foreach (var property in active.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!typeof(Timeline).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length != 0) continue;
            try
            {
                if (property.GetValue(active) is Timeline timeline) return timeline;
            }
            catch { }
        }
        foreach (var field in active.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (typeof(Timeline).IsAssignableFrom(field.FieldType) && field.GetValue(active) is Timeline timeline) return timeline;
        throw new MissingMemberException("Timeline was not found on ActiveTimelineViewModel.");
    }

    private static MethodInfo[] SplitMethods()
        => typeof(Timeline).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name.Contains("Split", StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name).ThenBy(m => m.GetParameters().Length).ToArray();

    private static string Describe(MethodInfo m)
        => $"{m.Attributes} {m.ReturnType.FullName} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name + (p.HasDefaultValue ? "=" + (p.DefaultValue ?? "null") : "")))})";

    private static async Task RunAsync(object active)
    {
        var timeline = FindTimeline(active);
        int fps = timeline.VideoInfo.FPS;
        string media = Environment.GetEnvironmentVariable("CNWL_SPLIT_MEDIA") ?? throw new InvalidOperationException("Fixture path missing.");
        Check("fixture_exists", File.Exists(media));
        Check("fps_positive", fps > 0);

        var methods = SplitMethods();
        File.WriteAllLines(Path.Combine(output, "split-methods.txt"), methods.Select(Describe));
        var candidates = methods.Where(m => m.Name == "SplitSelectedAndGroupedItems").ToArray();
        Check("split_method_discovered", candidates.Length > 0);

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

        MethodInfo? split = candidates.FirstOrDefault(m => TryArguments(m.GetParameters(), timeline, item, fps, out _));
        if (split == null)
        {
            File.WriteAllText(Path.Combine(output, "unsupported-signature.txt"), string.Join(Environment.NewLine, candidates.Select(Describe)));
            throw new NotSupportedException("SplitSelectedAndGroupedItems exists, but this probe does not yet map its parameters.");
        }
        _ = TryArguments(split.GetParameters(), timeline, item, fps, out object?[] args);
        File.WriteAllText(Path.Combine(output, "invocation.txt"), Describe(split) + Environment.NewLine + string.Join(Environment.NewLine, split.GetParameters().Zip(args).Select(x => x.First.Name + " => " + (x.Second?.ToString() ?? "null"))));
        split.Invoke(timeline, args);
        await Task.Delay(300);

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
            method = Describe(split),
            before = new { frame = originalFrame, length = originalLength, offsetTicks = originalOffset, file = originalPath },
            after = snapshot,
            selection = timeline.SelectedItems.OfType<VideoItem>().Select(v => new
            {
                sameReference = ReferenceEquals(v, item),
                v.Frame,
                v.Length,
                offsetSeconds = v.ContentOffset.TotalSeconds
            }).ToArray()
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

    private static bool TryArguments(ParameterInfo[] parameters, Timeline timeline, VideoItem item, int fps, out object?[] args)
    {
        args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            string name = p.Name ?? "";
            if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }
            if (p.ParameterType == typeof(int))
            {
                args[i] = name.Contains("fps", StringComparison.OrdinalIgnoreCase) ? fps
                    : name.Contains("layer", StringComparison.OrdinalIgnoreCase) ? item.Layer
                    : timeline.CurrentFrame;
                continue;
            }
            if (p.ParameterType == typeof(long)) { args[i] = (long)timeline.CurrentFrame; continue; }
            if (p.ParameterType.IsEnum)
            {
                Array values = Enum.GetValues(p.ParameterType);
                if (values.Length == 0) { args = []; return false; }
                args[i] = values.GetValue(0);
                continue;
            }
            if (p.ParameterType == typeof(IItem) || p.ParameterType == typeof(VideoItem)) { args[i] = item; continue; }
            if (p.ParameterType == typeof(ImmutableList<IItem>)) { args[i] = timeline.SelectedItems; continue; }
            if (p.ParameterType.IsAssignableFrom(timeline.SelectedItems.GetType())) { args[i] = timeline.SelectedItems; continue; }
            args = [];
            return false;
        }
        return true;
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
