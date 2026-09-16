using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NavigationContextProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Navigation Context Bootstrap";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class NavigationTool : IToolPlugin
{
    public string Name => "CNWL Navigation Context";
    public Type ViewModelType => typeof(NavigationModel);
    public Type ViewType => typeof(NavigationView);
    public bool AllowMultipleInstances => false;
}

public sealed class NavigationView : UserControl
{
    public NavigationView() => Content = new TextBlock { Text = "Navigation context probe", Margin = new Thickness(12) };
}

public sealed class NavigationModel : ITimelineToolViewModel, IToolViewModel, IDisposable
{
    public string Title => "CNWL Navigation Context";
    public bool CanSuspend => false;
    public void SetTimelineToolInfo(TimelineToolInfo info) => Probe.Info = info;
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public void Dispose() { }
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}

internal static class Probe
{
    internal static TimelineToolInfo? Info;
    private static bool scheduled;
    private static string output = "";
    private static string fpsPath = "";
    private static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var value = Environment.GetEnvironmentVariable("CNWL_NAV_CONTEXT_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(value)) return;
        scheduled = true;
        output = Path.GetFullPath(value);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    private static void Start()
    {
        int ticks = 0;
        bool created = false, opened = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>().Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (main != null)
                {
                    var active = main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                    if (active == null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                    }
                    else if (active != null && !opened)
                    {
                        opened = OpenTool(main);
                    }
                }
                if (Info != null)
                {
                    timer.Stop();
                    Run(Info);
                    Write("PASS_NAVIGATION_CONTEXT", null);
                    return;
                }
                if (ticks >= 180) throw new TimeoutException("Real TimelineToolInfo callback not received.");
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_NAVIGATION_CONTEXT", ex.ToString());
            }
        };
        timer.Start();
    }

    private static bool OpenTool(object main)
    {
        var roots = main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) as IEnumerable;
        if (roots == null) return false;
        bool Visit(object item, int depth)
        {
            if (depth > 6) return false;
            var type = item.GetType();
            var header = type.GetProperty("Header")?.GetValue(item)?.ToString() ?? "";
            if (header.Contains("CNWL Navigation Context", StringComparison.Ordinal))
            {
                if (type.GetProperty("Command")?.GetValue(item) is ICommand command)
                {
                    var parameter = type.GetProperty("CommandParameter")?.GetValue(item);
                    if (command.CanExecute(parameter)) { command.Execute(parameter); return true; }
                }
            }
            foreach (var name in new[] { "Items", "Children", "MenuItems" })
                if (type.GetProperty(name)?.GetValue(item) is IEnumerable children)
                    foreach (var child in children) if (child != null && Visit(child, depth + 1)) return true;
            return false;
        }
        foreach (var root in roots) if (root != null && Visit(root, 0)) return true;
        return false;
    }

    private static object? PublicValue(object obj, string name)
        => obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj)
        ?? obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);

    private static int ReadFps(TimelineToolInfo info, Timeline timeline)
    {
        foreach (var (obj, prefix) in new (object, string)[] { (info, "TimelineToolInfo"), (timeline, "Timeline") })
        {
            var videoInfo = PublicValue(obj, "VideoInfo");
            var value = videoInfo == null ? PublicValue(obj, "FPS") : PublicValue(videoInfo, "FPS");
            if (value != null)
            {
                var fps = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsFinite(fps) || fps <= 0 || fps != Math.Truncate(fps)) throw new InvalidDataException("Expected a positive integer FPS context.");
                fpsPath = prefix + (videoInfo == null ? ".FPS" : ".VideoInfo.FPS");
                return checked((int)fps);
            }
        }
        throw new MissingMemberException("No public FPS context. info=" + string.Join(",", info.GetType().GetMembers().Select(m => m.Name).Distinct()) + "; timeline=" + string.Join(",", timeline.GetType().GetMembers().Select(m => m.Name).Distinct()));
    }

    private static void Run(TimelineToolInfo info)
    {
        var timeline = info.Timeline ?? throw new InvalidOperationException("Timeline missing.");
        Check("real_tool_callback", Info != null);
        Check("public_timeline", typeof(TimelineToolInfo).GetProperty("Timeline")?.GetMethod?.IsPublic == true);
        int fps = ReadFps(info, timeline);
        Check("public_fps", fps > 0);
        string media = Environment.GetEnvironmentVariable("CNWL_NAV_CONTEXT_MEDIA") ?? throw new InvalidOperationException("Fixture path missing.");
        Check("fixture_exists", File.Exists(media));
        var a = new VideoItem { FilePath = media, Frame = 137, Length = fps * 3, Layer = 1, ContentOffset = TimeSpan.FromSeconds(2), Remark = "CNWL_NAV_A" };
        var b = new VideoItem { FilePath = media, Frame = 733, Length = fps * 4, Layer = 2, ContentOffset = TimeSpan.FromSeconds(3), Remark = "CNWL_NAV_B" };
        a.PlaybackRate2.SetFirstValue(100);
        b.PlaybackRate2.SetFirstValue(100);
        a.PlaybackRate2.SetAnimationParameters(a.Length, fps);
        b.PlaybackRate2.SetAnimationParameters(b.Length, fps);
        Check("insert_a", timeline.TryAddItems([a], a.Frame, a.Layer));
        Check("insert_b", timeline.TryAddItems([b], b.Frame, b.Layer));
        string Signature() => JsonSerializer.Serialize(new[] { a, b }.Select(x => new { x.FilePath, x.Frame, x.Length, x.Layer, offset = x.ContentOffset.Ticks, x.Remark }));
        var before = Signature();
        var events = new List<string?>();
        PropertyChangedEventHandler listener = (_, e) => events.Add(e.PropertyName);
        var notify = (INotifyPropertyChanged)timeline;
        notify.PropertyChanged += listener;
        try
        {
            timeline.SelectedItems = ImmutableList.Create<IItem>(a, b);
            var snapshot = timeline.SelectedItems.OfType<VideoItem>().ToArray();
            Check("selected_videoitems", snapshot.Length == 2 && snapshot.Any(x => ReferenceEquals(x, a)) && snapshot.Any(x => ReferenceEquals(x, b)));
            Check("separate_occurrences", !ReferenceEquals(a, b) && a.FilePath == b.FilePath && a.Frame != b.Frame);
            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            Check("snapshot_survives_selection", snapshot.Length == 2 && timeline.SelectedItems.Count == 0 && snapshot.All(x => timeline.Items.Any(y => ReferenceEquals(x, y))));
            Check("selection_event", events.Any(x => x == "SelectedItems" || x == "SelectedItem"));
            timeline.CurrentFrame = a.Frame + 1;
            Check("integer_seek", timeline.CurrentFrame == a.Frame + 1);
            timeline.CurrentFrame = b.Frame + fps;
            Check("second_occurrence_seek", timeline.CurrentFrame == b.Frame + fps);
            var mapProperty = typeof(VideoItem).GetProperty("PlaybackRateMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingMemberException("PlaybackRateMap");
            var map = mapProperty.GetValue(a) ?? throw new InvalidOperationException("Null map.");
            var signature = new[] { typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan) };
            var forward = map.GetType().GetMethod("GetSourceTime", signature) ?? throw new MissingMethodException("GetSourceTime");
            var inverse = map.GetType().GetMethod("FindFirstTimeForSourceTime", signature) ?? throw new MissingMethodException("FindFirstTimeForSourceTime");
            var itemTime = TimeSpan.FromSeconds(0.5173);
            var source = (TimeSpan)(forward.Invoke(map, [itemTime, a.Length, fps, a.ContentOffset, TimeSpan.FromSeconds(30)]) ?? throw new InvalidOperationException());
            var back = (TimeSpan)(inverse.Invoke(map, [source, a.Length, fps, a.ContentOffset, TimeSpan.FromSeconds(30)]) ?? throw new InvalidOperationException());
            Check("fractional_item_time", Math.Abs(back.TotalSeconds - itemTime.TotalSeconds) < 0.000002);
            int localFrame = (int)Math.Ceiling(back.TotalSeconds * fps - 1e-9);
            timeline.CurrentFrame = a.Frame + localFrame;
            Check("fractional_target_integer_seek", timeline.CurrentFrame == a.Frame + localFrame && localFrame >= 0 && localFrame < a.Length);
            Check("item_state_unchanged", Signature() == before);
            Check("reference_membership", timeline.Items.Any(x => ReferenceEquals(x, a)) && timeline.Items.Any(x => ReferenceEquals(x, b)));
            File.WriteAllText(Path.Combine(output, "context.json"), JsonSerializer.Serialize(new { fps, fpsPath, fractionalItemSeconds = back.TotalSeconds, chosenLocalFrame = localFrame, policy = "product-ceiling-not-host-rounding", identity = "session-object-reference-only" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { notify.PropertyChanged -= listener; }
    }

    private static void Check(string id, bool passed)
    {
        requirements.Add(new { id, passed });
        File.AppendAllText(Path.Combine(output, "assertions.txt"), $"ASSERT {(passed ? "PASS" : "FAIL")} {id}\n");
        if (!passed) throw new InvalidOperationException("Assertion failed: " + id);
    }

    private static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { schema = "cnwl.navigation-context.v1", status, host = "4.56.1.0 Lite", sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"), requirements, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
