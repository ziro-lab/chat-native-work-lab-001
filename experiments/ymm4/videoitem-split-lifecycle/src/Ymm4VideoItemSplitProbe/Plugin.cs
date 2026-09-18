using System.Collections.Immutable;
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
    private sealed record Piece(bool SameReference, int Frame, int Length, int Layer, double OffsetSeconds, double Rate, string FilePath, string Remark);
    private sealed record SplitObservation(double Rate, int SplitFrames, Piece[] Pieces, int SelectionCount, bool OriginalStillPresent);

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
            try { if (property.GetValue(active) is Timeline timeline) return timeline; } catch { }
        }
        foreach (var field in active.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (typeof(Timeline).IsAssignableFrom(field.FieldType) && field.GetValue(active) is Timeline timeline) return timeline;
        throw new MissingMemberException("Timeline was not found on ActiveTimelineViewModel.");
    }

    private static MethodInfo RequireMethod(string name, params Type[] parameterTypes)
        => typeof(Timeline).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null)
            ?? throw new MissingMethodException("Timeline." + name);

    private static object FirstSplitSelectionMode(MethodInfo split)
    {
        Type enumType = split.GetParameters()[1].ParameterType;
        if (!enumType.IsEnum) throw new InvalidDataException("Split selection mode is not an enum.");
        Array values = Enum.GetValues(enumType);
        if (values.Length == 0) throw new InvalidDataException("Split selection mode enum is empty.");
        File.WriteAllText(Path.Combine(output, "selection-modes.txt"), string.Join(Environment.NewLine, Enum.GetNames(enumType)));
        return values.GetValue(0)!;
    }

    private static async Task RunAsync(object active)
    {
        var timeline = FindTimeline(active);
        int fps = timeline.VideoInfo.FPS;
        string media = Environment.GetEnvironmentVariable("CNWL_SPLIT_MEDIA") ?? throw new InvalidOperationException("Fixture path missing.");
        Check("fixture_exists", File.Exists(media));
        Check("fps_positive", fps > 0);

        var canSplit = RequireMethod("CanSplitSelectedAndGroupedItems", typeof(int));
        var splitMethods = typeof(Timeline).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "SplitSelectedAndGroupedItems").ToArray();
        File.WriteAllLines(Path.Combine(output, "split-methods.txt"), splitMethods.Select(m => m.ToString() ?? m.Name));
        var split = splitMethods.Single(m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(int) && m.GetParameters()[1].ParameterType.IsEnum);
        object mode = FirstSplitSelectionMode(split);
        Check("split_surface_discovered", canSplit.ReturnType == typeof(bool) && split.ReturnType == typeof(void));

        var observations = new List<SplitObservation>();
        foreach (double rate in new[] { 50d, 100d, 200d })
        {
            int layer = rate == 50 ? 2 : rate == 100 ? 3 : 4;
            int startFrame = rate == 50 ? 300 : rate == 100 ? 1200 : 2100;
            observations.Add(await SplitOnce(timeline, split, canSplit, mode, media, fps, startFrame, layer, rate, 4));
        }

        File.WriteAllText(Path.Combine(output, "split.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));

        foreach (var observation in observations)
        {
            string key = ((int)observation.Rate).ToString(CultureInfo.InvariantCulture);
            Check("split_" + key + "_two_pieces", observation.Pieces.Length == 2);
            Check("split_" + key + "_original_replaced", !observation.OriginalStillPresent && observation.Pieces.All(p => !p.SameReference));
            Check("split_" + key + "_timeline_partition", observation.Pieces[0].Length == fps * 4 && observation.Pieces[1].Length == fps * 6
                && observation.Pieces[0].Frame + observation.Pieces[0].Length == observation.Pieces[1].Frame);
            Check("split_" + key + "_path_rate_preserved", observation.Pieces.All(p => p.FilePath == Path.GetFullPath(media) && Math.Abs(p.Rate - observation.Rate) < 1e-6));
            double expectedRightOffset = 5 + 4 * observation.Rate / 100d;
            Check("split_" + key + "_source_partition", Math.Abs(observation.Pieces[0].OffsetSeconds - 5) < 1e-6
                && Math.Abs(observation.Pieces[1].OffsetSeconds - expectedRightOffset) < 1e-6);
        }

        // A realistic highlight isolation: split the right 100% piece once more two seconds later.
        var middleCase = observations.Single(x => x.Rate == 100);
        var right = timeline.Items.OfType<VideoItem>().Single(v => v.Layer == 3 && v.Frame == middleCase.Pieces[1].Frame);
        int secondSplitFrame = right.Frame + fps * 2;
        timeline.SelectedItems = ImmutableList.Create<IItem>(right);
        Check("second_split_can_execute", (bool)(canSplit.Invoke(timeline, [secondSplitFrame]) ?? false));
        split.Invoke(timeline, [secondSplitFrame, mode]);
        await Task.Delay(250);
        var finalPieces = timeline.Items.OfType<VideoItem>().Where(v => v.Layer == 3 && v.FilePath != null && Path.GetFullPath(v.FilePath) == Path.GetFullPath(media))
            .OrderBy(v => v.Frame).ToArray();
        Check("double_split_three_pieces", finalPieces.Length == 3);
        Check("double_split_source_ranges", finalPieces[0].ContentOffset.TotalSeconds == 5
            && finalPieces[1].ContentOffset.TotalSeconds == 9
            && finalPieces[2].ContentOffset.TotalSeconds == 11
            && finalPieces[0].Length == fps * 4 && finalPieces[1].Length == fps * 2 && finalPieces[2].Length == fps * 4);
        Check("double_split_previous_left_survives", finalPieces[0].Frame == 1200 && finalPieces[0].Length == fps * 4);
        Check("double_split_target_reference_replaced", !timeline.Items.Any(x => ReferenceEquals(x, right)));

        File.WriteAllText(Path.Combine(output, "double-split.json"), JsonSerializer.Serialize(finalPieces.Select(v => new
        {
            v.Frame, v.Length, offsetSeconds = v.ContentOffset.TotalSeconds,
            rate = v.PlaybackRate2.GetValue(0, v.Length, fps),
            v.Layer, v.Remark
        }), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<SplitObservation> SplitOnce(Timeline timeline, MethodInfo split, MethodInfo canSplit, object mode,
        string media, int fps, int startFrame, int layer, double rate, int splitSeconds)
    {
        var item = new VideoItem
        {
            FilePath = media,
            Frame = startFrame,
            Length = fps * 10,
            Layer = layer,
            ContentOffset = TimeSpan.FromSeconds(5),
            Remark = "CNWL_SPLIT_" + rate.ToString("0", CultureInfo.InvariantCulture)
        };
        item.PlaybackRate2.SetFirstValue(rate);
        item.PlaybackRate2.SetAnimationParameters(item.Length, fps);
        if (!timeline.TryAddItems([item], item.Frame, item.Layer)) throw new InvalidOperationException("Fixture insertion failed.");

        int splitFrame = item.Frame + fps * splitSeconds;
        timeline.SelectedItems = ImmutableList.Create<IItem>(item);
        timeline.CurrentFrame = splitFrame;
        if (!ReferenceEquals(timeline.SelectedItems.Single(), item)) throw new InvalidOperationException("Selection was not established.");
        if (!(bool)(canSplit.Invoke(timeline, [splitFrame]) ?? false)) throw new InvalidOperationException("Host reports split unavailable.");

        split.Invoke(timeline, [splitFrame, mode]);
        await Task.Delay(250);

        string full = Path.GetFullPath(media);
        var pieces = timeline.Items.OfType<VideoItem>()
            .Where(v => v.Layer == layer && v.FilePath != null && Path.GetFullPath(v.FilePath) == full
                     && v.Frame < startFrame + fps * 10 && v.Frame + v.Length > startFrame)
            .OrderBy(v => v.Frame).ToArray();

        var snapshot = pieces.Select(v => new Piece(
            ReferenceEquals(v, item), v.Frame, v.Length, v.Layer, v.ContentOffset.TotalSeconds,
            v.PlaybackRate2.GetValue(0, v.Length, fps), Path.GetFullPath(v.FilePath!), v.Remark ?? "")).ToArray();

        return new(rate, fps * splitSeconds, snapshot, timeline.SelectedItems.Count, timeline.Items.Any(x => ReferenceEquals(x, item)));
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
