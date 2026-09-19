using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4SceneItemBehaviorProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive SceneItem Time Behavior";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static string fixture = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_SCENEITEM_BEHAVIOR_DIR");
        var media = Environment.GetEnvironmentVariable("CNWL_YMM4_SCENEITEM_BEHAVIOR_FIXTURE");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(media)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        fixture = Path.GetFullPath(media);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    private static void Start()
    {
        var ticks = 0;
        var lastCreateAttempt = -100;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window window in Application.Current.Windows)
                {
                    var root = window.DataContext;
                    if (root?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
                    if (active == null)
                    {
                        if (ticks - lastCreateAttempt >= 8)
                        {
                            lastCreateAttempt = ticks;
                            root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                        }
                        break;
                    }

                    var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);
                    var main = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (model == null || main == null) continue;

                    timer.Stop();
                    await RunAsync(model, main);
                    return;
                }

                if (ticks >= 180) throw new TimeoutException("YMM4 timeline did not become ready.");
            }
            catch (Exception ex)
            {
                timer.Stop();
                Append("ERROR " + ex);
                WriteResult("FAIL_EXCEPTION", ex.GetBaseException().Message);
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(object model, Timeline main)
    {
        if (!File.Exists(fixture)) throw new FileNotFoundException("fixture missing", fixture);

        var type = model.GetType();
        var scenes = type.GetProperty("Scenes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(model) as Scenes
            ?? throw new MissingMemberException("MainModel.Scenes");
        var create = type.GetMethod("CreateNewScene", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new MissingMethodException("CreateNewScene");

        main.Name = "Main";
        create.Invoke(model, null);
        await Idle();

        var child = scenes.Timelines.Single(t => !ReferenceEquals(t, main));
        child.Name = "Child";
        var fps = child.VideoInfo.FPS;
        Assert(fps > 0, "child FPS positive");

        var childVideo = new VideoItem(fixture)
        {
            Frame = 0,
            Layer = 10,
            Length = fps * 3,
            Remark = "CNWL_CHILD_VIDEO"
        };
        Assert(child.TryAddItems([childVideo], 0, 10), "child VideoItem added");
        for (var i = 0; i < 120 && childVideo.ContentLength <= TimeSpan.Zero; i++)
            await Task.Delay(100);
        Assert(childVideo.ContentLength > TimeSpan.Zero, "child media metadata loaded");

        // Keep the child scene exactly three seconds long in its timeline domain.
        childVideo.Length = fps * 3;
        await Idle();

        var childId = GetTimelineId(child);
        Assert(childId != Guid.Empty, "child exposes stable ID");

        var sceneItem = new SceneItem
        {
            SceneId = childId,
            Frame = 0,
            Length = fps,
            Layer = 20,
            ContentOffset = TimeSpan.FromSeconds(0.5),
            Remark = "CNWL_SCENEITEM_BEHAVIOR"
        };
        sceneItem.PlaybackRate2.SetFirstValue(100);
        sceneItem.PlaybackRate2.SetAnimationParameters(sceneItem.Length, fps);
        Assert(main.TryAddItems([sceneItem], 0, 20), "parent SceneItem added");

        for (var i = 0; i < 40 && sceneItem.ContentLength <= TimeSpan.Zero; i++)
            await Task.Delay(100);

        var childLength = sceneItem.ContentLength.TotalSeconds;
        Append("SCENEITEM_CONTENT_LENGTH=" + childLength.ToString("R", CultureInfo.InvariantCulture));
        Assert(childLength >= 2.9 && childLength <= 3.1, "SceneItem ContentLength tracks child scene duration");

        var mapProp = typeof(SceneItem).GetProperty("PlaybackRateMap", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("PlaybackRateMap");
        var map = mapProp.GetValue(sceneItem) ?? throw new InvalidOperationException("PlaybackRateMap null");
        var getSource = map.GetType().GetMethod("GetSourceTime",
            [typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan), typeof(bool).MakeByRefType()])
            ?? throw new MissingMethodException("GetSourceTime(...,out bool)");
        var getRange = map.GetType().GetMethod("GetConsumedContentRange", [typeof(int), typeof(int)])
            ?? throw new MissingMethodException("GetConsumedContentRange");

        (double Time, bool EndOrigin) SourceAt(double itemSeconds)
        {
            object[] args =
            [
                TimeSpan.FromSeconds(itemSeconds), sceneItem.Length, fps,
                sceneItem.ContentOffset, sceneItem.ContentLength, false
            ];
            var value = getSource.Invoke(map, args) is TimeSpan time
                ? time.TotalSeconds : double.NaN;
            return (value, args[5] is true);
        }

        var s0 = SourceAt(0);
        var sHalf = SourceAt(0.5);
        var sEndFrame = SourceAt((sceneItem.Length - 1d) / fps);
        Append($"RATE100 source0={s0.Time:R} sourceHalf={sHalf.Time:R} sourceLast={sEndFrame.Time:R} endOrigin={s0.EndOrigin}");
        Assert(!s0.EndOrigin, "100% SceneItem starts from child front");
        Assert(Math.Abs(s0.Time - 0.5) < 0.002, "100% SceneItem applies ContentOffset");
        Assert(Math.Abs(sHalf.Time - 1.0) < 0.002, "100% SceneItem advances child time one-to-one");

        var consumed100 = getRange.Invoke(map, [sceneItem.Length, fps]) ?? throw new InvalidOperationException("consumed100 null");
        var min100 = ReadTime(consumed100, "Minimum");
        var max100 = ReadTime(consumed100, "Maximum");
        Append($"RATE100 consumedMin={min100:R} consumedMax={max100:R}");

        sceneItem.PlaybackRate2.SetFirstValue(200);
        sceneItem.PlaybackRate2.SetAnimationParameters(sceneItem.Length, fps);
        map = mapProp.GetValue(sceneItem) ?? throw new InvalidOperationException("PlaybackRateMap null after 200%");
        getSource = map.GetType().GetMethod("GetSourceTime",
            [typeof(TimeSpan), typeof(int), typeof(int), typeof(TimeSpan), typeof(TimeSpan), typeof(bool).MakeByRefType()])
            ?? throw new MissingMethodException("GetSourceTime after 200%");
        getRange = map.GetType().GetMethod("GetConsumedContentRange", [typeof(int), typeof(int)])
            ?? throw new MissingMethodException("GetConsumedContentRange after 200%");

        var p0 = SourceAt(0);
        var pHalf = SourceAt(0.5);
        Append($"RATE200 source0={p0.Time:R} sourceHalf={pHalf.Time:R} endOrigin={p0.EndOrigin}");
        Assert(Math.Abs(p0.Time - 0.5) < 0.002, "200% SceneItem keeps ContentOffset anchor");
        Assert(Math.Abs(pHalf.Time - 1.5) < 0.002, "200% SceneItem maps parent half-second to child +1 second");

        var consumed200 = getRange.Invoke(map, [sceneItem.Length, fps]) ?? throw new InvalidOperationException("consumed200 null");
        var min200 = ReadTime(consumed200, "Minimum");
        var max200 = ReadTime(consumed200, "Maximum");
        Append($"RATE200 consumedMin={min200:R} consumedMax={max200:R}");
        Assert(max200 > max100 + 0.9, "200% consumed child range is wider than 100%");

        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=PASS_SCENEITEM_TIME_BEHAVIOR",
            "child_duration=" + childLength.ToString("R", CultureInfo.InvariantCulture),
            "rate100_source0=" + s0.Time.ToString("R", CultureInfo.InvariantCulture),
            "rate100_source_half=" + sHalf.Time.ToString("R", CultureInfo.InvariantCulture),
            "rate100_consumed_min=" + min100.ToString("R", CultureInfo.InvariantCulture),
            "rate100_consumed_max=" + max100.ToString("R", CultureInfo.InvariantCulture),
            "rate200_source0=" + p0.Time.ToString("R", CultureInfo.InvariantCulture),
            "rate200_source_half=" + pHalf.Time.ToString("R", CultureInfo.InvariantCulture),
            "rate200_consumed_min=" + min200.ToString("R", CultureInfo.InvariantCulture),
            "rate200_consumed_max=" + max200.ToString("R", CultureInfo.InvariantCulture)
        ], new UTF8Encoding(false));
    }

    private static double ReadTime(object value, string name)
    {
        var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(value.GetType().FullName, name);
        return property.GetValue(value) is TimeSpan time
            ? time.TotalSeconds : throw new InvalidOperationException(name + " is not TimeSpan");
    }

    private static Guid GetTimelineId(Timeline timeline)
    {
        var property = timeline.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(p => p.PropertyType == typeof(Guid) && p.GetIndexParameters().Length == 0
                && p.Name.Contains("id", StringComparison.OrdinalIgnoreCase));
        return property == null ? Guid.Empty : (Guid)(property.GetValue(timeline) ?? Guid.Empty);
    }

    private static async Task Idle() =>
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("ASSERT FAIL: " + name);
        Append("ASSERT PASS: " + name);
    }

    private static void WriteResult(string status, string detail) =>
        File.WriteAllLines(Path.Combine(output, "result.txt"),
            ["status=" + status, "detail=" + detail], new UTF8Encoding(false));

    private static void Append(string line) =>
        File.AppendAllText(Path.Combine(output, "sceneitem-behavior.txt"), line + Environment.NewLine, new UTF8Encoding(false));
}
