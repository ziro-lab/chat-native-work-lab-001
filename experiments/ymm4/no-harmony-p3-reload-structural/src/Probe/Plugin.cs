using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4P3ReloadStructuralProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL P3 reload structural";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

public sealed class ToolPlugin : IToolPlugin
{
    public Type ViewModelType => typeof(ToolViewModel);
    public Type ViewType => typeof(ToolView);
    public string Name => "CNWL P3 Reload Folder State";
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
    public int DefaultOrder => 9994;
}

public sealed class ToolView : UserControl { }

public sealed class ToolViewModel : IToolViewModel
{
    private string? savedState;
    event EventHandler<CreateNewToolViewRequestedEventArgs>? IToolViewModel.CreateNewToolViewRequested { add { } remove { } }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }
    public string Title => "CNWL P3 Reload Folder State";
    public void LoadState(ToolState stateData) => savedState = stateData.SavedState;
    public ToolState SaveState() => new() { Title = Title, SavedState = savedState };
}

internal static class Probe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P3_RELOAD_STRUCTURAL_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
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
                var window = Application.Current.Windows.Cast<Window>()
                    .FirstOrDefault(x => x.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (window is null) return;
                var root = window.DataContext!;
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
                await Run(window, root);
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

    private static MethodInfo PublicMethod(object target, string name, params Type[] types) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, types)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    private static object? Get(object? target, string name) =>
        target?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

    private static Timeline? GetTimeline(object root)
    {
        var active = Get(root, "ActiveTimelineViewModel");
        if (active is null) return null;
        return Get(active, "Timeline") as Timeline
            ?? active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
    }

    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        var count = 0;
        while (stack.Count > 0)
        {
            if (++count > 20000) throw new InvalidOperationException("Visual tree budget exceeded.");
            var current = stack.Pop();
            if (current is FrameworkElement fe) yield return fe;
            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
                stack.Push(VisualTreeHelper.GetChild(current, i));
        }
    }

    private static object FindArea(object root)
    {
        if (Get(root, "AnchorableAreaViewModels") is not IEnumerable areas)
            throw new InvalidOperationException("AnchorableAreaViewModels missing.");
        return areas.Cast<object>().First(x =>
            x.GetType().GetProperty("ViewModelType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(x) as Type
            == typeof(ToolViewModel));
    }

    private static void SeedArea(object root, string state)
    {
        var area = FindArea(root);
        var load = area.GetType().GetMethod("LoadState", BindingFlags.Instance | BindingFlags.Public, [typeof(ToolState)])
            ?? throw new MissingMethodException(area.GetType().FullName, "LoadState");
        load.Invoke(area, [new ToolState { Title = "CNWL P3 Reload Folder State", SavedState = state }]);
    }

    private static string? ReadArea(object root)
    {
        var area = FindArea(root);
        var save = area.GetType().GetMethod("SaveState", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new MissingMethodException(area.GetType().FullName, "SaveState");
        var state = save.Invoke(area, null) ?? throw new InvalidOperationException("ToolArea SaveState returned null.");
        return state.GetType().GetProperty("SavedState", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state) as string;
    }

    private static FolderDocument MakeDocument(Guid timelineId, Guid folderId, int start, int end, string name) => new()
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
                        End = end,
                        Name = name,
                        IsCollapsed = true
                    }
                ]
            }
        ]
    };

    private static UndoRedoManager GetUndoManager(object root)
    {
        var model = root.GetType().GetField("model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(root)
            ?? throw new MissingMemberException(root.GetType().FullName, "model");
        return Get(model, "UndoRedoManager") as UndoRedoManager
            ?? throw new MissingMemberException(model.GetType().FullName, "UndoRedoManager");
    }

    private static FrameworkElement? FindElementForDataContext(FrameworkElement root, object dataContext) =>
        Elements(root).Where(x => x.IsVisible && ReferenceEquals(x.DataContext, dataContext))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight).FirstOrDefault();

    private static void ExecuteAddLayer(Window window, FrameworkElement timelineView, TimelineViewModel vm, Timeline timeline, int layer)
    {
        var command = CommandSettings.Default[CommandType.AddLayer]
            ?? throw new InvalidOperationException("AddLayer command missing.");
        if (command is not RoutedCommand routed)
            throw new InvalidOperationException("AddLayer is not RoutedCommand.");

        var labelVm = layer >= 0 && layer < vm.LayerLabels.Count ? vm.LayerLabels[layer] : null;
        var label = labelVm is null ? null : FindElementForDataContext(timelineView, labelVm);
        if (label is not null)
        {
            try { label.Focus(); } catch { }
        }
        window.Activate();
        SetForegroundWindow(new WindowInteropHelper(window).Handle);

        var targets = new List<(string Name, IInputElement Target)>();
        if (label is not null) targets.Add(("LayerLabel", label));
        if (Keyboard.FocusedElement is IInputElement focused) targets.Add(("Focused", focused));
        targets.Add(("TimelineView", timelineView));
        targets.Add(("Window", window));

        foreach (var (targetName, target) in targets
            .GroupBy(x => x.Target, ReferenceEqualityComparer.Instance)
            .Select(x => x.First()))
        {
            foreach (var parameter in new object?[] { layer, null, labelVm, timeline })
            {
                bool can;
                try { can = routed.CanExecute(parameter, target); }
                catch { continue; }
                if (!can) continue;
                routed.Execute(parameter, target);
                facts["add_route"] = targetName + ":" + (parameter?.GetType().Name ?? "<null>");
                return;
            }
        }
        throw new InvalidOperationException("No executable AddLayer route.");
    }

    private static async Task Key(byte key, bool ctrl = false)
    {
        if (ctrl) keybd_event(0x11, 0, 0, 0);
        try
        {
            await Task.Delay(60);
            keybd_event(key, 0, 0, 0);
            await Task.Delay(60);
        }
        finally
        {
            keybd_event(key, 0, 2, 0);
            if (ctrl) keybd_event(0x11, 0, 2, 0);
        }
        await Task.Delay(650);
    }

    private sealed class ReloadedFolderBridge : IDisposable
    {
        private readonly FrameworkElement root;
        private readonly UndoRedoManager manager;
        private readonly string timelineKey;
        private readonly ExecutedRoutedEventHandler handler;

        public ReloadedFolderBridge(FrameworkElement root, UndoRedoManager manager, FolderDocument document, string timelineKey)
        {
            this.root = root;
            this.manager = manager;
            this.timelineKey = timelineKey;
            Document = FolderDocumentRules.NormalizeAndValidate(document);
            handler = OnPreview;
            root.AddHandler(CommandManager.PreviewExecutedEvent, handler, true);
        }

        public FolderDocument Document { get; private set; }
        public int AddedUndoCommands { get; private set; }
        public int UndoCallbacks { get; private set; }
        public int RedoCallbacks { get; private set; }

        private void SetFolders(IReadOnlyList<PersistedFolder> folders)
        {
            Document = FolderDocumentRules.ReplaceTimeline(Document, timelineKey, folders);
        }

        private void OnPreview(object sender, ExecutedRoutedEventArgs e)
        {
            var configured = CommandSettings.Default[CommandType.AddLayer];
            if (configured is null || !ReferenceEquals(configured, e.Command) || e.Parameter is not int layer)
                return;

            var before = FolderDocumentRules.FindTimeline(Document, timelineKey)?.Folders.ToArray()
                ?? throw new InvalidOperationException("Restored timeline folder state missing.");
            var ranges = FolderRangeTracker.Apply(
                before.Select(x => new FolderRange(x.Id, x.Start, x.End)),
                new InsertLayers(layer, 1)).Ranges.ToArray();
            var byId = ranges.ToDictionary(x => x.Id);
            var after = before.Select(x =>
            {
                var range = byId[x.Id];
                return x with { Start = range.Start, End = range.End };
            }).ToArray();

            SetFolders(after);
            manager.AddCommand(new UndoRedoActionCommand(
                () => { UndoCallbacks++; SetFolders(before); },
                () => { RedoCallbacks++; SetFolders(after); }));
            AddedUndoCommands++;
        }

        public void Dispose() => root.RemoveHandler(CommandManager.PreviewExecutedEvent, handler);
    }

    private static string Layers(Timeline timeline) =>
        string.Join("|", timeline.Items.Where(x => x.Remark?.StartsWith("P3R_", StringComparison.Ordinal) == true)
            .OrderBy(x => x.Remark).Select(x => $"{x.Remark}:L{x.Layer}"));

    private static (int Start, int End, string Name, bool Collapsed) Folder(FolderDocument document, string key)
    {
        var folder = FolderDocumentRules.FindTimeline(document, key)?.Folders.Single()
            ?? throw new InvalidOperationException("Expected one persisted folder.");
        return (folder.Start, folder.End, folder.Name, folder.IsCollapsed);
    }

    private static async Task Run(Window window, object root)
    {
        ReloadedFolderBridge? bridge = null;
        EventHandler? recordedHandler = null;
        UndoRedoManager? manager = null;
        try
        {
            var timeline = GetTimeline(root) ?? throw new InvalidOperationException("Timeline missing.");
            var originalId = timeline.ID;
            var character = new Character { Name = "P3 Reload" };
            foreach (var layer in new[] { 2, 3, 6 })
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20,
                    Layer = layer,
                    Length = 60,
                    Serif = "P3 reload",
                    Remark = "P3R_L" + layer
                };
                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add failed at L" + layer);
            }
            await Task.Delay(500);

            var folderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var stateA = FolderDocumentCodec.Save(MakeDocument(originalId, folderId, 2, 6, "Reloaded"));
            SeedArea(root, stateA);
            var pathA = Path.Combine(output, "p3-reload-a.ymmp");
            PublicMethod(root, "SaveProject", typeof(string)).Invoke(root, [pathA]);
            Check("project_a_saved", File.Exists(pathA));

            var stateB = FolderDocumentCodec.Save(MakeDocument(originalId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 10, 12, "Other"));
            SeedArea(root, stateB);
            var pathB = Path.Combine(output, "p3-reload-b.ymmp");
            PublicMethod(root, "SaveProject", typeof(string)).Invoke(root, [pathB]);
            Check("project_b_saved", File.Exists(pathB));

            PublicMethod(root, "OpenProject", typeof(string)).Invoke(root, [pathA]);
            await Task.Delay(2200);

            timeline = GetTimeline(root) ?? throw new InvalidOperationException("Reloaded Timeline missing.");
            Check("reload_timeline_id_stable", timeline.ID == originalId);
            Check("reload_fixture_items_present", timeline.Items.Count(x => x.Remark?.StartsWith("P3R_", StringComparison.Ordinal) == true) == 3);
            Check("reload_item_layers_baseline", Layers(timeline) == "P3R_L2:L2|P3R_L3:L3|P3R_L6:L6");

            var restoredText = ReadArea(root);
            var loaded = FolderDocumentCodec.Load(restoredText);
            Check("reload_document_loaded", loaded.Success && loaded.Document is not null);
            var runtime = loaded.Document ?? throw new InvalidOperationException("Restored document null.");
            var key = timeline.ID.ToString("D");
            var baselineFolder = Folder(runtime, key);
            Check("reload_folder_baseline", baselineFolder == (2, 6, "Reloaded", true));

            var timelineView = Elements(window)
                .Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();
            var vm = timelineView.DataContext as TimelineViewModel
                ?? throw new InvalidOperationException("TimelineViewModel missing.");

            manager = GetUndoManager(root);
            var recorded = 0;
            recordedHandler = (_, _) => recorded++;
            manager.Recorded += recordedHandler;

            bridge = new ReloadedFolderBridge(window, manager, runtime, key);
            timeline.LayerSelection.SelectedLayers = ImmutableList.Create(3);
            var recordedBefore = recorded;
            ExecuteAddLayer(window, timelineView, vm, timeline, 3);
            await Task.Delay(800);

            Check("add_native_recorded_once", recorded == recordedBefore + 1);
            Check("add_plugin_undo_added_once", bridge.AddedUndoCommands == 1);
            Check("add_item_layers_shifted", Layers(timeline) == "P3R_L2:L2|P3R_L3:L4|P3R_L6:L7");
            Check("add_folder_tracker_applied", Folder(bridge.Document, key) == (2, 7, "Reloaded", true));
            Check("add_document_serializable", FolderDocumentCodec.Load(FolderDocumentCodec.Save(bridge.Document)).Success);

            await Key(0x5A, ctrl: true); // Ctrl+Z
            Check("undo_item_layers_restored", Layers(timeline) == "P3R_L2:L2|P3R_L3:L3|P3R_L6:L6");
            Check("undo_folder_restored", Folder(bridge.Document, key) == (2, 6, "Reloaded", true));
            Check("undo_callback_once", bridge.UndoCallbacks == 1);

            await Key(0x59, ctrl: true); // Ctrl+Y
            Check("redo_item_layers_forward", Layers(timeline) == "P3R_L2:L2|P3R_L3:L4|P3R_L6:L7");
            Check("redo_folder_forward", Folder(bridge.Document, key) == (2, 7, "Reloaded", true));
            Check("redo_callback_once", bridge.RedoCallbacks == 1);
            Check("metadata_preserved_through_history", Folder(bridge.Document, key).Name == "Reloaded" && Folder(bridge.Document, key).Collapsed);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));

            facts["folder_after_redo"] = $"{Folder(bridge.Document, key).Start}..{Folder(bridge.Document, key).End}";
            facts["recorded"] = recorded.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            if (manager is not null && recordedHandler is not null) manager.Recorded -= recordedHandler;
            bridge?.Dispose();
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
            status = failed ? "FAIL_P3_RELOAD_STRUCTURAL" : "PASS_P3_RELOAD_STRUCTURAL",
            hostVersion = typeof(Timeline).Assembly.GetName().Version?.ToString(),
            checks,
            facts
        };
        var tmp = Path.Combine(output, "result.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, Path.Combine(output, "result.json"), true);
    }
}
