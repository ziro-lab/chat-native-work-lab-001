using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class HandsOnEntry : ILocalizePlugin
{
    public string Name => "レイヤーフォルダ no-Harmony Hands-on";
    public void SetCulture(CultureInfo cultureInfo) => HandsOnRuntime.Start();
}

public sealed class FolderToolPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(FolderToolViewModel);
    public Type ViewType => typeof(FolderToolView);
    public string Name => FolderToolViewModel.DisplayTitle;
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
    public int DefaultOrder => 9985;
}

public sealed class FolderToolView : UserControl
{
    public FolderToolView()
    {
        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock
                {
                    Text = "レイヤーフォルダ（no-Harmony Hands-on）",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                new TextBlock
                {
                    Text = "通常操作はタイムライン左側のレイヤー名列から行います。\n" +
                           "連続した2レイヤー以上を選択して右クリックしてください。\n" +
                           "このパネルを開いておく必要はありません。",
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}

public sealed class FolderToolViewModel : IToolViewModel
{
    internal const string DisplayTitle = "レイヤーフォルダ (no-Harmony)";

    private static FolderStateStore Store => FolderStateStore.Shared;

    public string Title => DisplayTitle;

    public string StatusText =>
        Store.IsRecoveryBlocked
            ? "保存済みフォルダ情報を読み込めません。元データは保持されています。"
            : "タイムライン左側のレイヤー名列から操作できます。";

    public void LoadState(ToolState stateData)
    {
        Store.LoadRaw(stateData.SavedState);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
    }

    public ToolState SaveState() => new()
    {
        Title = Title,
        SavedState = Store.SaveRaw()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested
    {
        add { }
        remove { }
    }
}

internal static class HandsOnRuntime
{
    private static bool started;
    private static DispatcherTimer? timer;
    private static Window? window;
    private static object? root;
    private static INotifyPropertyChanged? projectPathSignal;
    private static PropertyChangedEventHandler? projectPathHandler;
    private static HandsOnController? controller;
    private static Guid lastProjectTimelineId;
    private static bool smokeProjectRequested;
    private static bool smokeTimelinePrepared;
    private static bool synchronizingToolArea;
    private static string? lastProjectSignature;

    internal static void Start()
    {
        if (started) return;
        started = true;

        FolderStateStore.Shared.Changed += OnFolderStateChanged;
        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        timer.Tick += (_, _) => Tick();
        timer.Start();

        Application.Current.Dispatcher.BeginInvoke(
            new Action(Tick),
            DispatcherPriority.ApplicationIdle);
    }

    internal static void Diagnostic(string message)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "runtime.log"),
                $"{DateTime.UtcNow:O}\t{message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void Tick()
    {
        try
        {
            var currentWindow = HandsOnHostAccess.MainWindow();
            var currentRoot = currentWindow?.DataContext;
            if (currentWindow is null || currentRoot is null)
            {
                DetachController();
                return;
            }

            if (!ReferenceEquals(root, currentRoot))
            {
                DetachProjectSignal();
                DetachController();
                window = currentWindow;
                root = currentRoot;
                AttachProjectSignal(currentRoot);
                lastProjectSignature = null;
                Diagnostic("main_root_attached");
            }

            var smokeEnabled = string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT"),
                "1",
                StringComparison.Ordinal);

            if (HandsOnHostAccess.ActiveTimelineViewModel(currentRoot) is null
                && !smokeProjectRequested
                && smokeEnabled)
            {
                smokeProjectRequested = true;
                currentRoot.GetType()
                    .GetMethod("CreateProject", Type.EmptyTypes)
                    ?.Invoke(currentRoot, null);
                Diagnostic("smoke_create_project_requested");
            }

            TrySynchronizeCurrentProjectState();

            if (smokeEnabled && !smokeTimelinePrepared)
                TryPrepareSmokeTimeline(currentWindow, currentRoot);

            EnsureController();
        }
        catch (Exception ex)
        {
            Diagnostic("tick_error=" + ex);
        }
    }

    private static void TryPrepareSmokeTimeline(Window currentWindow, object currentRoot)
    {
        var active = HandsOnHostAccess.ActiveTimelineViewModel(currentRoot);
        var timeline = HandsOnHostAccess.TimelineOf(active);
        if (timeline is null || timeline.ID == Guid.Empty)
            return;

        try
        {
            currentWindow.WindowState = System.Windows.WindowState.Normal;
            currentWindow.Left = 0;
            currentWindow.Top = 0;
            currentWindow.Width = 1100;
            currentWindow.Height = 720;

            var resourceFreeIntegrationSmoke =
                string.Equals(
                    Environment.GetEnvironmentVariable("CNWL_P4_S0_INTEGRATION_SMOKE"),
                    "1",
                    StringComparison.Ordinal)
                || string.Equals(
                    Environment.GetEnvironmentVariable("CNWL_P4_S1_INTEGRATION_SMOKE"),
                    "1",
                    StringComparison.Ordinal)
                || string.Equals(
                    Environment.GetEnvironmentVariable("CNWL_P4_S2_METADATA_SURFACE_SMOKE"),
                    "1",
                    StringComparison.Ordinal)
                || string.Equals(
                    Environment.GetEnvironmentVariable("CNWL_P4_S2_INTEGRATION_SMOKE"),
                    "1",
                    StringComparison.Ordinal);

            // The ordinary startup smoke keeps its VoiceItem fixture. Structural
            // history gates stay resource-free: an artificial VoiceItem without
            // a configured speaker makes YMM4's resource refresh fail when the
            // UndoRedoManager records a history boundary.
            if (!resourceFreeIntegrationSmoke
                && !timeline.Items.Any(x =>
                    string.Equals(
                        x.Remark,
                        "CNWL_P4_HANDS_ON_SMOKE",
                        StringComparison.Ordinal)))
            {
                var character = new Character { Name = "CNWL_P4_HANDS_ON_SMOKE" };
                for (var layer = 0; layer <= 2; layer++)
                {
                    var item = new VoiceItem(character)
                    {
                        Frame = 20,
                        Layer = layer,
                        Length = 60,
                        Serif = "smoke",
                        Remark = "CNWL_P4_HANDS_ON_SMOKE"
                    };
                    if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                        throw new InvalidOperationException(
                            "Smoke fixture item could not be added at layer " + layer);
                }
            }

            smokeTimelinePrepared = true;
            Diagnostic(
                $"smoke_timeline_prepared timeline={timeline.ID:D} " +
                $"resource_free={resourceFreeIntegrationSmoke}");
        }
        catch (Exception ex)
        {
            Diagnostic("smoke_prepare_retry=" + ex.Message);
        }
    }

    private static void AttachProjectSignal(object currentRoot)
    {
        projectPathSignal = HandsOnHostAccess.ProjectFilePathSignal(currentRoot);
        projectPathHandler = (_, _) =>
        {
            Application.Current.Dispatcher.BeginInvoke(
                new Action(SynchronizeProjectState),
                DispatcherPriority.ContextIdle);
        };
        projectPathSignal.PropertyChanged += projectPathHandler;
    }

    private static void DetachProjectSignal()
    {
        if (projectPathSignal is not null && projectPathHandler is not null)
            projectPathSignal.PropertyChanged -= projectPathHandler;
        projectPathSignal = null;
        projectPathHandler = null;
        root = null;
        window = null;
    }

    private static void SynchronizeProjectState()
    {
        try
        {
            lastProjectSignature = null;
            TrySynchronizeCurrentProjectState(force: true);
            EnsureController(force: true);
        }
        catch (Exception ex)
        {
            Diagnostic("project_sync_error=" + ex);
        }
    }

    private static void TrySynchronizeCurrentProjectState(bool force = false)
    {
        if (root is null)
            return;

        var active = HandsOnHostAccess.ActiveTimelineViewModel(root);
        var timeline = HandsOnHostAccess.TimelineOf(active);
        if (timeline is null || timeline.ID == Guid.Empty)
            return;

        var path = HandsOnHostAccess.ProjectFilePath(root);
        var signature =
            (string.IsNullOrWhiteSpace(path) ? "<unsaved>" : path)
            + "|" + timeline.ID.ToString("D");

        if (!force && string.Equals(signature, lastProjectSignature, StringComparison.Ordinal))
            return;

        try
        {
            synchronizingToolArea = true;

            if (string.IsNullOrWhiteSpace(path)
                && (HandsOnHostAccess.IsEmptyProject(root)
                    || timeline.ID != lastProjectTimelineId))
            {
                HandsOnHostAccess.WriteToolAreaSavedState(root, null);
                FolderStateStore.Shared.ResetDocument();
                lastProjectTimelineId = timeline.ID;
                Diagnostic($"new_project_reset timeline={timeline.ID:D}");
            }
            else if (!string.IsNullOrWhiteSpace(path))
            {
                var raw = HandsOnHostAccess.ReadToolAreaSavedState(root);
                FolderStateStore.Shared.LoadRaw(raw);
                lastProjectTimelineId = timeline.ID;
                Diagnostic($"project_sync path={path} timeline={timeline.ID:D}");
            }

            lastProjectSignature = signature;
        }
        catch (Exception ex)
        {
            Diagnostic("project_state_retry=" + ex.Message);
        }
        finally
        {
            synchronizingToolArea = false;
        }
    }

    private static void OnFolderStateChanged(object? sender, EventArgs e)
    {
        try
        {
            controller?.RefreshFromDocument();

            if (root is null || synchronizingToolArea)
                return;

            synchronizingToolArea = true;
            HandsOnHostAccess.WriteToolAreaSavedState(
                root,
                FolderStateStore.Shared.SaveRaw());
        }
        catch (Exception ex)
        {
            Diagnostic("state_refresh_error=" + ex);
        }
        finally
        {
            synchronizingToolArea = false;
        }
    }

    private static void EnsureController(bool force = false)
    {
        if (window is null || root is null)
        {
            DetachController();
            return;
        }

        var active = HandsOnHostAccess.ActiveTimelineViewModel(root);
        var timeline = HandsOnHostAccess.TimelineOf(active);
        if (active is null || timeline is null)
        {
            DetachController();
            return;
        }

        if (!force && controller is not null && controller.Matches(timeline.ID))
            return;

        DetachController();

        try
        {
            var host = HandsOnHostAccess.CreateHost(window, active);
            var labels = HandsOnHostAccess.FindLabels(window);
            var manager = HandsOnHostAccess.UndoManager(root);
            controller = new HandsOnController(
                window,
                labels,
                host,
                manager,
                FolderStateStore.Shared);
            lastProjectTimelineId = timeline.ID;
            Diagnostic($"controller_attached timeline={timeline.ID:D}");

            var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, "ready.txt"),
                    $"{DateTime.UtcNow:O}\t{timeline.ID:D}");
            }
        }
        catch (Exception ex)
        {
            Diagnostic("controller_attach_retry=" + ex.Message);
        }
    }

    private static void DetachController()
    {
        if (controller is null)
            return;
        try
        {
            controller.Dispose();
        }
        catch (Exception ex)
        {
            Diagnostic("controller_dispose_error=" + ex);
        }
        controller = null;
    }
}
