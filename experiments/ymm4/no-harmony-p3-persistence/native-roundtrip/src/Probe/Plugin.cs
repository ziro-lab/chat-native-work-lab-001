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

namespace Ymm4P3ToolStateRoundtrip;

public sealed class RoundtripEntry : ILocalizePlugin
{
    public string Name => "CNWL P3 ToolState roundtrip";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class RoundtripToolPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(RoundtripToolViewModel);
    public Type ViewType => typeof(RoundtripToolView);
    public string Name => "CNWL P3 Folder State";
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
    public int DefaultOrder => 9991;
}

public sealed class RoundtripToolView : UserControl
{
}

public sealed class RoundtripToolViewModel : IToolViewModel
{
    private string? savedState;

    private static readonly object Gate = new();
    public static int LoadCount { get; private set; }
    public static string? LastLoadedSavedState { get; private set; }

    event EventHandler<CreateNewToolViewRequestedEventArgs>? IToolViewModel.CreateNewToolViewRequested { add { } remove { } }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }

    public string Title => "CNWL P3 Folder State";

    public void LoadState(ToolState stateData)
    {
        savedState = stateData.SavedState;
        lock (Gate)
        {
            LoadCount++;
            LastLoadedSavedState = savedState;
        }

        var dir = Environment.GetEnvironmentVariable("CNWL_P3_TOOLSTATE_ROUNDTRIP_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "load-events.txt"),
                $"{DateTime.UtcNow:O}\tpid={Environment.ProcessId}\tcount={LoadCount}\tsaved={savedState ?? "<null>"}{Environment.NewLine}");
        }
    }

    public ToolState SaveState() => new()
    {
        Title = Title,
        SavedState = savedState
    };
}

internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];
    private static bool failed;
    private static INotifyPropertyChanged? projectPathSignal;
    private static PropertyChangedEventHandler? projectPathHandler;
    private static int projectPathSignalCount;
    private static int hostAreaSyncCount;
    private static string? lastHostAreaSavedState;

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_P3_TOOLSTATE_ROUNDTRIP_DIR");
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
                await RunAsync(root);
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

    private static Guid GetTimelineId(object root) =>
        GetTimeline(root)?.ID ?? Guid.Empty;

    private static string? GetProjectFilePath(object root)
    {
        var reactive = PublicProperty(root, "ProjectFilePath");
        return reactive?.GetType()
            .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(reactive) as string;
    }

    private static bool SamePath(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual)
        && string.Equals(
            Path.GetFullPath(actual),
            Path.GetFullPath(expected),
            StringComparison.OrdinalIgnoreCase);

    private static object? TryFindToolArea(object root)
    {
        // Discovery P3.1 proved the host-owned ToolAreaViewModel is present in
        // the public AnchorableAreaViewModels collection even before the tool
        // is shown. ToolMenuItems describes menu entries and is not the state
        // owner used by project SaveState/LoadState.
        if (PublicProperty(root, "AnchorableAreaViewModels") is not IEnumerable areas)
            return null;

        foreach (var area in areas.Cast<object>())
        {
            var viewModelType = area.GetType()
                .GetProperty("ViewModelType", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(area) as Type;
            if (viewModelType == typeof(RoundtripToolViewModel))
                return area;
        }
        return null;
    }

    private static object FindToolArea(object root) =>
        TryFindToolArea(root) ?? throw new InvalidOperationException("Roundtrip ToolAreaViewModel missing.");

    private static void SeedToolArea(object root, string savedState)
    {
        var area = FindToolArea(root);
        var load = area.GetType().GetMethod(
            "LoadState",
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(ToolState)])
            ?? throw new MissingMethodException(area.GetType().FullName, "LoadState(ToolState)");
        load.Invoke(area, [new ToolState { Title = "CNWL P3 Folder State", SavedState = savedState }]);
    }

    private static string? ReadToolAreaState(object root)
    {
        var area = FindToolArea(root);
        var save = area.GetType().GetMethod("SaveState", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new MissingMethodException(area.GetType().FullName, "SaveState()");
        var state = save.Invoke(area, null) ?? throw new InvalidOperationException("ToolArea SaveState returned null.");
        return state.GetType().GetProperty("SavedState", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state) as string;
    }

    private static void AttachProjectStateCoordinator(object root)
    {
        if (projectPathSignal is not null)
            throw new InvalidOperationException("Project-state coordinator already attached.");

        projectPathSignal = PublicProperty(root, "ProjectFilePath") as INotifyPropertyChanged
            ?? throw new InvalidOperationException("Public ProjectFilePath does not expose change notification.");

        projectPathHandler = (_, _) =>
        {
            projectPathSignalCount++;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    lastHostAreaSavedState = ReadToolAreaState(root);
                    hostAreaSyncCount++;
                    File.AppendAllText(
                        Path.Combine(output, "project-events.txt"),
                        $"{DateTime.UtcNow:O}\tpathSignal={projectPathSignalCount}\tsync={hostAreaSyncCount}\tsaved={lastHostAreaSavedState ?? "<null>"}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(
                        Path.Combine(output, "project-events.txt"),
                        $"{DateTime.UtcNow:O}\terror={ex}{Environment.NewLine}");
                }
            }), DispatcherPriority.ContextIdle);
        };
        projectPathSignal.PropertyChanged += projectPathHandler;
    }

    private static void DetachProjectStateCoordinator()
    {
        if (projectPathSignal is not null && projectPathHandler is not null)
            projectPathSignal.PropertyChanged -= projectPathHandler;
        projectPathHandler = null;
        projectPathSignal = null;
    }

    private static async Task Wait(string name, Func<bool> condition, int timeoutMs = 10000)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
                throw new TimeoutException(name);
            await Task.Delay(100);
        }
        await Task.Delay(350);
    }

    private static FolderDocument DocumentFor(Guid timelineId, Guid folderId, string name, int start)
    {
        return new FolderDocument
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
                            Id = folderId,
                            Start = start,
                            End = start + 3,
                            Name = name,
                            IsCollapsed = true
                        }
                    ]
                }
            ]
        };
    }

    private static string? FindEmbeddedSavedState(string projectPath, string expected)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(projectPath));
        if (!doc.RootElement.TryGetProperty("ToolStates", out var toolStates)
            || toolStates.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var entry in toolStates.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object
                || !entry.Value.TryGetProperty("SavedState", out var saved)
                || saved.ValueKind != JsonValueKind.String)
                continue;
            if (saved.GetString() == expected)
            {
                facts["tool_state_key"] = entry.Name;
                return saved.GetString();
            }
        }
        return null;
    }

    private static async Task RunAsync(object root)
    {
        try
        {
            var saveProject = PublicMethod(root, "SaveProject", typeof(string));
            var openProject = PublicMethod(root, "OpenProject", typeof(string));
            AttachProjectStateCoordinator(root);

            var idA = GetTimelineId(root);
            Check("timeline_a_id_nonempty", idA != Guid.Empty);
            Check("timeline_id_public_guid", typeof(Timeline).GetProperty("ID", BindingFlags.Instance | BindingFlags.Public)?.PropertyType == typeof(Guid));
            await Wait("initial ToolArea", () => TryFindToolArea(root) is not null);
            Check("tool_area_found", FindToolArea(root) is not null);

            var stateA = FolderDocumentCodec.Save(DocumentFor(
                idA,
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Project A",
                2));
            SeedToolArea(root, stateA);
            Check("area_seed_a_roundtrip", ReadToolAreaState(root) == stateA);

            var pathA = Path.Combine(output, "p3-toolstate-a.ymmp");
            saveProject.Invoke(root, [pathA]);
            Check("project_a_saved", File.Exists(pathA));
            Check("project_a_toolstate_embedded", FindEmbeddedSavedState(pathA, stateA) == stateA);

            // Project isolation does not require a modal CreateProject route.
            // Save the same host-owned Timeline as two independent project files
            // with different ToolState payloads. New-project lifecycle is a
            // separate P3 gate.
            var idB = idA;
            var stateB = FolderDocumentCodec.Save(DocumentFor(
                idB,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "Project B",
                8));
            SeedToolArea(root, stateB);
            Check("area_seed_b_roundtrip", ReadToolAreaState(root) == stateB);

            var pathB = Path.Combine(output, "p3-toolstate-b.ymmp");
            saveProject.Invoke(root, [pathB]);
            Check("project_b_saved", File.Exists(pathB));
            Check("project_b_toolstate_embedded", FindEmbeddedSavedState(pathB, stateB) == stateB);
            Check("project_files_hold_distinct_toolstate", stateA != stateB);

            var beforeA = RoundtripToolViewModel.LoadCount;
            var beforeAPathSignal = projectPathSignalCount;
            var beforeASync = hostAreaSyncCount;
            openProject.Invoke(root, [pathA]);
            await Wait("open project A project-path sync", () =>
                SamePath(GetProjectFilePath(root), pathA)
                && projectPathSignalCount > beforeAPathSignal
                && hostAreaSyncCount > beforeASync
                && lastHostAreaSavedState == stateA,
                10000);
            var areaAfterA = ReadToolAreaState(root);
            facts["open_a_project_path"] = GetProjectFilePath(root) ?? "<null>";
            facts["open_a_load_count_before"] = beforeA.ToString(CultureInfo.InvariantCulture);
            facts["open_a_load_count_after"] = RoundtripToolViewModel.LoadCount.ToString(CultureInfo.InvariantCulture);
            facts["open_a_project_path_signal_before"] = beforeAPathSignal.ToString(CultureInfo.InvariantCulture);
            facts["open_a_project_path_signal_after"] = projectPathSignalCount.ToString(CultureInfo.InvariantCulture);
            facts["open_a_host_sync_before"] = beforeASync.ToString(CultureInfo.InvariantCulture);
            facts["open_a_host_sync_after"] = hostAreaSyncCount.ToString(CultureInfo.InvariantCulture);
            facts["open_a_area_matches"] = (areaAfterA == stateA).ToString();
            Check("open_a_project_path_applied", SamePath(GetProjectFilePath(root), pathA));
            Check("open_a_timeline_id_stable", GetTimelineId(root) == idA);
            Check("open_a_project_path_signal_observed", projectPathSignalCount > beforeAPathSignal);
            Check("open_a_host_area_synced", hostAreaSyncCount > beforeASync
                && lastHostAreaSavedState == stateA);
            Check("open_a_loadstate_not_required", RoundtripToolViewModel.LoadCount == beforeA);
            Check("open_a_area_state_restored", areaAfterA == stateA);
            var loadA = FolderDocumentCodec.Load(stateA);
            Check("open_a_document_valid", loadA.Success && loadA.Document is not null
                && FolderDocumentRules.FindTimeline(loadA.Document, idA.ToString("D"))?.Folders.Single().Name == "Project A");

            var beforeB = RoundtripToolViewModel.LoadCount;
            var beforeBPathSignal = projectPathSignalCount;
            var beforeBSync = hostAreaSyncCount;
            openProject.Invoke(root, [pathB]);
            await Wait("open project B project-path sync", () =>
                SamePath(GetProjectFilePath(root), pathB)
                && projectPathSignalCount > beforeBPathSignal
                && hostAreaSyncCount > beforeBSync
                && lastHostAreaSavedState == stateB,
                10000);
            var areaAfterB = ReadToolAreaState(root);
            facts["open_b_project_path"] = GetProjectFilePath(root) ?? "<null>";
            facts["open_b_load_count_before"] = beforeB.ToString(CultureInfo.InvariantCulture);
            facts["open_b_load_count_after"] = RoundtripToolViewModel.LoadCount.ToString(CultureInfo.InvariantCulture);
            facts["open_b_project_path_signal_before"] = beforeBPathSignal.ToString(CultureInfo.InvariantCulture);
            facts["open_b_project_path_signal_after"] = projectPathSignalCount.ToString(CultureInfo.InvariantCulture);
            facts["open_b_host_sync_before"] = beforeBSync.ToString(CultureInfo.InvariantCulture);
            facts["open_b_host_sync_after"] = hostAreaSyncCount.ToString(CultureInfo.InvariantCulture);
            facts["open_b_area_matches"] = (areaAfterB == stateB).ToString();
            Check("open_b_project_path_applied", SamePath(GetProjectFilePath(root), pathB));
            Check("open_b_timeline_id_stable", GetTimelineId(root) == idB);
            Check("open_b_project_path_signal_observed", projectPathSignalCount > beforeBPathSignal);
            Check("open_b_host_area_synced", hostAreaSyncCount > beforeBSync
                && lastHostAreaSavedState == stateB);
            Check("open_b_loadstate_not_required", RoundtripToolViewModel.LoadCount == beforeB);
            Check("open_b_area_state_restored", areaAfterB == stateB);
            Check("project_state_isolated", stateA != stateB && areaAfterA == stateA && areaAfterB == stateB);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));

            facts["timeline_a"] = idA.ToString("D");
            facts["timeline_b"] = idB.ToString("D");
            facts["load_count"] = RoundtripToolViewModel.LoadCount.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            DetachProjectStateCoordinator();
        }
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
            status = failed ? "FAIL_P3_TOOLSTATE_ROUNDTRIP" : "PASS_P3_TOOLSTATE_ROUNDTRIP",
            hostVersion = typeof(Timeline).Assembly.GetName().Version?.ToString(),
            checks,
            facts
        };
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path.Combine(output, "result.json"), true);
    }
}
