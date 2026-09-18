using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
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
        int ticks = 0;
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                ticks++;
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

    private static IEnumerable<(string Name, ICommand Command)> SplitCommands(object active)
    {
        static bool Relevant(string name) =>
            name.Contains("Split", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Divide", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Separate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cut", StringComparison.OrdinalIgnoreCase);

        var seen = new HashSet<ICommand>(ReferenceEqualityComparer.Instance);
        foreach (var property in active.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!Relevant(property.Name) || property.GetIndexParameters().Length != 0) continue;
            try
            {
                if (property.GetValue(active) is ICommand command && seen.Add(command))
                    yield return ("property:" + property.Name, command);
            }
            catch { }
        }
        foreach (var field in active.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!Relevant(field.Name)) continue;
            try
            {
                if (field.GetValue(active) is ICommand command && seen.Add(command))
                    yield return ("field:" + field.Name, command);
            }
            catch { }
        }
    }

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
        string originalPath = item.FilePath;
        timeline.SelectedItems = ImmutableList.Create<IItem>(item);
        timeline.CurrentFrame = item.Frame + fps * 4;
        Check("selection_before_split", timeline.SelectedItems.Count == 1 && ReferenceEquals(timeline.SelectedItems[0], item));
        Check("playhead_inside_source", timeline.CurrentFrame == item.Frame + fps * 4);

        var commands = SplitCommands(active).ToArray();
        File.WriteAllLines(Path.Combine(output, "commands.txt"), commands.Select(c => c.Name + " can(null)=" + SafeCan(c.Command, null) + " can(item)=" + SafeCan(c.Command, item)));
        var executable = commands.FirstOrDefault(c => SafeCan(c.Command, null));
        object? parameter = null;
        if (executable.Command == null)
        {
            executable = commands.FirstOrDefault(c => SafeCan(c.Command, item));
            parameter = item;
        }
        Check("split_command_discovered", executable.Command != null);
        executable.Command.Execute(parameter);
        await Task.Delay(250);

        var derived = timeline.Items.OfType<VideoItem>()
            .Where(v => string.Equals(Path.GetFullPath(v.FilePath), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase)
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
            rate = v.PlaybackRate2.GetValue(0),
            v.FilePath,
            v.Remark
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "split.json"), JsonSerializer.Serialize(new
        {
            command = executable.Name,
            parameter = parameter == null ? "null" : "source-item",
            before = new { frame = originalFrame, length = originalLength, offsetTicks = originalOffset, file = originalPath },
            after = snapshot,
            selection = timeline.SelectedItems.OfType<VideoItem>().Select(v => new { sameReference = ReferenceEquals(v, item), v.Frame, v.Length, offsetSeconds = v.ContentOffset.TotalSeconds }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));

        Check("split_created_two_pieces", derived.Length == 2);
        Check("timeline_partition", derived[0].Frame == originalFrame
            && derived[0].Frame + derived[0].Length == derived[1].Frame
            && derived[1].Frame + derived[1].Length == originalFrame + originalLength);
        Check("file_path_preserved", derived.All(v => string.Equals(Path.GetFullPath(v.FilePath), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase)));
        Check("rate_preserved", derived.All(v => Math.Abs(v.PlaybackRate2.GetValue(0) - 100) < 0.000001));
        Check("source_partition_100_percent", Math.Abs(derived[0].ContentOffset.TotalSeconds - 5) < 0.000001
            && Math.Abs(derived[1].ContentOffset.TotalSeconds - 9) < 0.000001
            && derived[0].Length == fps * 4
            && derived[1].Length == fps * 6);
        Check("one_piece_keeps_original_reference", derived.Count(v => ReferenceEquals(v, item)) == 1);
    }

    private static bool SafeCan(ICommand command, object? parameter)
    {
        try { return command.CanExecute(parameter); } catch { return false; }
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
