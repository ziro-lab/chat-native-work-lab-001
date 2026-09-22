using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using YukkuriMovieMaker.Plugin;

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

    private readonly FolderPersistenceSession session = new();

    internal static FolderToolViewModel? Current { get; private set; }
    internal static event EventHandler? StateChanged;

    public FolderToolViewModel()
    {
        Current = this;
        RaiseStateChanged();
    }

    public string Title => DisplayTitle;

    public string StatusText =>
        session.IsRecoveryBlocked
            ? "保存済みフォルダ情報を読み込めません。元データは保持されています。"
            : "タイムライン左側のレイヤー名列から操作できます。";

    internal FolderDocument Document => session.Document;
    internal bool IsRecoveryBlocked => session.IsRecoveryBlocked;
    internal FolderDocumentLoadStatus LastLoadStatus => session.LastLoadStatus;
    internal string? LastError => session.LastError;

    public void LoadState(ToolState stateData)
    {
        session.Load(stateData.SavedState);
        RaiseStateChanged();
    }

    public ToolState SaveState() => new()
    {
        Title = Title,
        SavedState = session.Save()
    };

    internal void SyncRaw(string? savedState)
    {
        session.Load(savedState);
        RaiseStateChanged();
    }

    internal void ReplaceDocument(FolderDocument document)
    {
        session.Replace(document);
        RaiseStateChanged();
    }

    internal void ResetDocument()
    {
        session.Reset();
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

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

    internal static void Start()
    {
        if (started) return;
        started = true;

        FolderToolViewModel.StateChanged += OnFolderStateChanged;
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
                Diagnostic("main_root_attached");
            }

            if (HandsOnHostAccess.ActiveTimelineViewModel(currentRoot) is null
                && !smokeProjectRequested
                && string.Equals(
                    Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT"),
                    "1",
                    StringComparison.Ordinal))
            {
                smokeProjectRequested = true;
                currentRoot.GetType()
                    .GetMethod("CreateProject", Type.EmptyTypes)
                    ?.Invoke(currentRoot, null);
                Diagnostic("smoke_create_project_requested");
            }

            EnsureController();
        }
        catch (Exception ex)
        {
            Diagnostic("tick_error=" + ex);
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
        if (root is null)
            return;

        try
        {
            var active = HandsOnHostAccess.ActiveTimelineViewModel(root);
            var timeline = HandsOnHostAccess.TimelineOf(active);
            var tool = FolderToolViewModel.Current;
            if (timeline is null || tool is null)
                return;

            var path = HandsOnHostAccess.ProjectFilePath(root);
            if (string.IsNullOrWhiteSpace(path) && HandsOnHostAccess.IsEmptyProject(root))
            {
                if (timeline.ID != Guid.Empty && timeline.ID != lastProjectTimelineId)
                {
                    HandsOnHostAccess.WriteToolAreaSavedState(root, null);
                    tool.ResetDocument();
                    lastProjectTimelineId = timeline.ID;
                    Diagnostic($"new_project_reset timeline={timeline.ID:D}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(path))
            {
                var raw = HandsOnHostAccess.ReadToolAreaSavedState(root);
                tool.SyncRaw(raw);
                lastProjectTimelineId = timeline.ID;
                Diagnostic($"project_sync path={path} timeline={timeline.ID:D}");
            }

            EnsureController(force: true);
        }
        catch (Exception ex)
        {
            Diagnostic("project_sync_error=" + ex);
        }
    }

    private static void OnFolderStateChanged(object? sender, EventArgs e)
    {
        try
        {
            controller?.RefreshFromDocument();
        }
        catch (Exception ex)
        {
            Diagnostic("state_refresh_error=" + ex);
        }
    }

    private static void EnsureController(bool force = false)
    {
        if (window is null || root is null || FolderToolViewModel.Current is null)
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
                FolderToolViewModel.Current);
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
