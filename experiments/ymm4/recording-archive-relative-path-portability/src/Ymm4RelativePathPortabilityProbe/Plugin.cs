using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YmmProject = YukkuriMovieMaker.Project.Project;

namespace Ymm4RelativePathPortabilityProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Recording Archive Relative Path Portability";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static string fixture = "";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_RELATIVE_PATH_DIR");
        var media = Environment.GetEnvironmentVariable("CNWL_YMM4_RELATIVE_PATH_FIXTURE");
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
        var projectCreated = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    var root = window.DataContext;
                    if (root?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = root.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(root);
                    if (active == null && !projectCreated)
                    {
                        projectCreated = true;
                        root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                        break;
                    }
                    if (active == null) continue;
                    var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(root);
                    var timeline = active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (model == null || timeline == null) continue;
                    timer.Stop();
                    await RunAsync(model, timeline);
                    return;
                }
                if (++ticks >= 120) throw new TimeoutException("YMM4 main model not ready.");
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

    private static async Task RunAsync(object model, Timeline timeline)
    {
        if (!File.Exists(fixture)) throw new FileNotFoundException("fixture missing", fixture);
        var modelType = model.GetType();
        var save = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "SaveProject" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        var load = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m => m.Name == "LoadProjectFile" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));

        var a = Path.Combine(output, "A");
        var b = Path.Combine(output, "B");
        Directory.CreateDirectory(Path.Combine(a, "recordings"));
        File.Copy(fixture, Path.Combine(a, "recordings", "clip.mp4"), true);

        timeline.Name = "RelativePath";
        var relative = Path.Combine("recordings", "clip.mp4");
        var item = new VideoItem
        {
            FilePath = relative,
            Frame = 0,
            Length = 120,
            Layer = 10,
            Remark = "CNWL_RELATIVE_PATH"
        };
        Assert(timeline.TryAddItems([item], 0, 10), "relative VideoItem added");

        var projectA = Path.Combine(a, "project.ymmp");
        save.Invoke(model, [projectA]);
        await Idle();
        Assert(File.Exists(projectA), "project A saved");
        Append("LIVE_AFTER_SAVE_FILEPATH=" + item.FilePath);
        Append("LIVE_AFTER_SAVE_CONTENT_LENGTH=" + item.ContentLength.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));

        CopyDirectory(a, b);
        var projectB = Path.Combine(b, "project.ymmp");
        var mediaA = Path.Combine(a, "recordings", "clip.mp4");
        File.Delete(mediaA);
        Assert(!File.Exists(mediaA) && File.Exists(Path.Combine(b, "recordings", "clip.mp4")), "only moved B media remains");

        var task = load.Invoke(model, [projectB]) as Task ?? throw new InvalidOperationException("LoadProjectFile did not return Task.");
        await task;
        var loaded = task.GetType().GetProperty("Result")?.GetValue(task) as YmmProject
            ?? throw new InvalidOperationException("LoadProjectFile result is not Project.");
        var timelines = GetTimelines(loaded);
        var loadedItem = timelines.SelectMany(t => t.Items).OfType<VideoItem>().Single(v => v.Remark == "CNWL_RELATIVE_PATH");
        await Idle();
        await Idle();

        var loadedPath = loadedItem.FilePath ?? "";
        var isAbsolute = Path.IsPathFullyQualified(loadedPath);
        var projectPath = loaded.FilePath ?? "";
        var candidate = isAbsolute ? loadedPath : Path.GetFullPath(loadedPath, b);
        var candidateExists = File.Exists(candidate);
        var contentPositive = loadedItem.ContentLength > TimeSpan.Zero;
        var getFiles = string.Join("|", loadedItem.GetFiles().Select(x => x?.ToString() ?? "<null>"));

        Append("SAVED_JSON_HAS_RELATIVE=" + File.ReadAllText(projectA).Contains("recordings", StringComparison.OrdinalIgnoreCase));
        Append("LOADED_VIDEO_FILEPATH=" + loadedPath);
        Append("LOADED_VIDEO_FILEPATH_ABSOLUTE=" + isAbsolute);
        Append("LOADED_PROJECT_FILEPATH=" + projectPath);
        Append("MOVED_CANDIDATE=" + candidate);
        Append("MOVED_CANDIDATE_EXISTS=" + candidateExists);
        Append("LOADED_CONTENT_LENGTH=" + loadedItem.ContentLength.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));
        Append("GETFILES=" + getFiles);

        File.WriteAllLines(Path.Combine(output, "result.txt"),
        [
            "status=PASS_RELATIVE_PATH_PORTABILITY_OBSERVATION",
            "relative_preserved=" + (!isAbsolute),
            "moved_candidate_exists=" + candidateExists,
            "content_length_positive_after_move=" + contentPositive,
            "loaded_project_points_to_B=" + SamePath(projectPath, projectB),
            "loaded_video_path=" + loadedPath.Replace("\r", "").Replace("\n", "")
        ], new UTF8Encoding(false));
    }

    private static Timeline[] GetTimelines(YmmProject project)
    {
        var p = project.GetType().GetProperty("Timelines", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Project.Timelines missing.");
        return (p.GetValue(project) as IEnumerable<Timeline>)?.ToArray()
            ?? throw new InvalidOperationException("Project.Timelines type changed.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static async Task Idle() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static void Assert(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + name);
        Append("ASSERT PASS: " + name);
    }
    private static void Append(string line) => File.AppendAllText(Path.Combine(output, "relative-path.txt"), line + Environment.NewLine, new UTF8Encoding(false));
    private static void WriteResult(string status, string detail) =>
        File.WriteAllLines(Path.Combine(output, "result.txt"), ["status=" + status, "detail=" + detail], new UTF8Encoding(false));
}
