using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4P3NewProjectProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL P3 new-project lifecycle";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class ToolPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(ToolViewModel);
    public Type ViewType => typeof(ToolView);
    public string Name => "CNWL P3 New Project State";
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
    public int DefaultOrder => 9992;
}

public sealed class ToolView : UserControl { }

public sealed class ToolViewModel : IToolViewModel
{
    private string? savedState;
    public static int LoadCount { get; private set; }
    public static string? LastLoaded { get; private set; }

    event EventHandler<CreateNewToolViewRequestedEventArgs>? IToolViewModel.CreateNewToolViewRequested { add { } remove { } }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }

    public string Title => "CNWL P3 New Project State";

    public void LoadState(ToolState stateData)
    {
        savedState = stateData.SavedState;
        LoadCount++;
        LastLoaded = savedState;
    }

    public ToolState SaveState() => new() { Title = Title, SavedState = savedState };
}

internal static class Probe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];

    private static void Progress(string value) =>
        File.AppendAllText(Path.Combine(output, "progress.txt"), $"{DateTime.UtcNow:O}\t{value}{Environment.NewLine}");

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_P3_NEW_PROJECT_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path)) return;
        scheduled = true;
        output = Path.GetFullPath(path);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static void Bootstrap()
    {
        var ticks = 0;
        var created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (++ticks > 120) throw new TimeoutException("Bootstrap");
                var root = Application.Current.Windows.Cast<Window>()
                    .Select(x => x.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (root is null) return;

                if (GetTimeline(root) is null)
                {
                    if (!created)
                    {
                        created = true;
                        PublicMethod(root, "CreateProject", Type.EmptyTypes).Invoke(root, null);
                    }
                    return;
                }

                timer.Stop();
                await Run(root);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Fail(ex);
                Finish();
            }
        };
        timer.Start();
    }

    private static MethodInfo PublicMethod(object target, string name, params Type[] parameterTypes) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, parameterTypes)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    private static object? PublicProperty(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    private static Timeline? GetTimeline(object root)
    {
        var active = PublicProperty(root, "ActiveTimelineViewModel");
        if (active is null) return null;
        return PublicProperty(active, "Timeline") as Timeline
            ?? active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
    }

    private static string? ProjectPath(object root)
    {
        var reactive = PublicProperty(root, "ProjectFilePath");
        return reactive?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(reactive) as string;
    }

    private static object FindArea(object root)
    {
        if (PublicProperty(root, "AnchorableAreaViewModels") is not IEnumerable areas)
            throw new InvalidOperationException("AnchorableAreaViewModels missing.");
        foreach (var area in areas.Cast<object>())
        {
            var type = area.GetType().GetProperty("ViewModelType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(area) as Type;
            if (type == typeof(ToolViewModel)) return area;
        }
        throw new InvalidOperationException("ToolArea missing.");
    }

    private static void SeedArea(object root, string saved)
    {
        var area = FindArea(root);
        var load = area.GetType().GetMethod("LoadState", BindingFlags.Instance | BindingFlags.Public, [typeof(ToolState)])
            ?? throw new MissingMethodException(area.GetType().FullName, "LoadState(ToolState)");
        load.Invoke(area, [new ToolState { Title = "CNWL P3 New Project State", SavedState = saved }]);
    }

    private static string? ReadArea(object root)
    {
        var area = FindArea(root);
        var save = area.GetType().GetMethod("SaveState", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new MissingMethodException(area.GetType().FullName, "SaveState()");
        var state = save.Invoke(area, null) ?? throw new InvalidOperationException("SaveState returned null.");
        return state.GetType().GetProperty("SavedState", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state) as string;
    }

    private static string Document(Guid timelineId) => FolderDocumentCodec.Save(new FolderDocument
    {
        Timelines =
        [
            new TimelineFolderState
            {
                TimelineKey = timelineId.ToString("D"),
                Folders =
                [
                    new PersistedFolder
                    {
                        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        Start = 2,
                        End = 5,
                        Name = "Old Project",
                        IsCollapsed = true
                    }
                ]
            }
        ]
    });

    private static async Task Run(object root)
    {
        try
        {
            Progress("run_start");
            var timeline = GetTimeline(root) ?? throw new InvalidOperationException("Timeline missing.");
            var oldId = timeline.ID;
            Check("old_timeline_id_nonempty", oldId != Guid.Empty);

            var seeded = Document(oldId);
            SeedArea(root, seeded);
            Check("old_area_seeded", ReadArea(root) == seeded);

            var oldPath = Path.Combine(output, "p3-new-project-old.ymmp");
            Progress("save_old_before");
            PublicMethod(root, "SaveProject", typeof(string)).Invoke(root, [oldPath]);
            Progress("save_old_after");
            await Task.Delay(800);
            Check("old_project_saved", File.Exists(oldPath));
            Check("old_project_reports_saved", PublicProperty(root, "IsSaved") is true);
            facts["before_project_path"] = ProjectPath(root) ?? "<null>";
            facts["before_timeline_id"] = oldId.ToString("D");
            facts["before_load_count"] = ToolViewModel.LoadCount.ToString(CultureInfo.InvariantCulture);

            Progress("create_project_before");
            PublicMethod(root, "CreateProject", Type.EmptyTypes).Invoke(root, null);
            Progress("create_project_after");
            await Task.Delay(2500);

            var current = GetTimeline(root);
            var newId = current?.ID ?? Guid.Empty;
            var areaState = ReadArea(root);
            facts["after_project_path"] = ProjectPath(root) ?? "<null>";
            facts["after_timeline_id"] = newId.ToString("D");
            facts["after_area_state"] = areaState ?? "<null>";
            facts["after_load_count"] = ToolViewModel.LoadCount.ToString(CultureInfo.InvariantCulture);
            facts["is_empty_project"] = (PublicProperty(root, "IsEmptyProject")?.ToString() ?? "<null>");
            facts["is_saved"] = (PublicProperty(root, "IsSaved")?.ToString() ?? "<null>");

            Check("new_project_has_timeline", current is not null && newId != Guid.Empty);
            Check("new_project_identity_is_fresh", newId != oldId);
            Check("new_project_does_not_inherit_folder_state", areaState != seeded);
            Check("new_project_folder_state_empty", string.IsNullOrWhiteSpace(areaState));
            Check("old_project_file_survives", File.Exists(oldPath));
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
        }
        catch (Exception ex) { Progress("failure=" + ex.GetType().Name); Fail(ex); }
        Progress("finish");
        Finish();
    }

    private static void Check(string name, bool pass)
    {
        checks[name] = pass;
        failed |= !pass;
    }

    private static void Fail(Exception ex)
    {
        failed = true;
        facts["error"] = ex.ToString();
    }

    private static void Finish()
    {
        var result = new
        {
            status = failed ? "FAIL_P3_NEW_PROJECT" : "PASS_P3_NEW_PROJECT",
            hostVersion = typeof(Timeline).Assembly.GetName().Version?.ToString(),
            checks,
            facts
        };
        var tmp = Path.Combine(output, "result.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, Path.Combine(output, "result.json"), true);
    }
}
