using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class P4MinimumWorkflowEntry : ILocalizePlugin
{
    public string Name => "CNWL P4 minimum workflow";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static readonly Guid FolderId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_P4_MINIMUM_WORKFLOW_DIR");
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
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (++ticks > 120) throw new TimeoutException("Bootstrap");
                var window = Application.Current.Windows.Cast<Window>()
                    .FirstOrDefault(x => x.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (window is null) return;

                var root = window.DataContext!;
                var vm = Host.Get(root, "ActiveTimelineViewModel");
                if (vm is null)
                {
                    if (!created)
                    {
                        created = true;
                        root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                    }
                    return;
                }

                timer.Stop();
                await Run(window);
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

    private static void Log(string value) =>
        File.AppendAllText(Path.Combine(output, "progress.txt"), $"{DateTime.UtcNow:O}\t{value}{Environment.NewLine}");

    private static void Check(string name, bool pass)
    {
        checks[name] = pass;
        failed |= !pass;
        Log($"assert {name}={pass}");
    }

    private static void Fact(string name, object? value) =>
        facts[name] = value?.ToString() ?? "<null>";

    private static void Fail(Exception ex)
    {
        failed = true;
        facts["error"] = ex.ToString();
        Log("failure=" + ex);
    }

    private static void Finish()
    {
        var result = new
        {
            status = failed ? "FAIL_P4_MINIMUM_WORKFLOW" : "PASS_P4_MINIMUM_WORKFLOW",
            hostVersion = typeof(Timeline).Assembly.GetName().Version?.ToString(),
            checks,
            facts
        };
        var tmp = Path.Combine(output, "result.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, Path.Combine(output, "result.json"), true);
    }

    private static string? ItemsBindingPath(FrameworkElement element)
    {
        if (element.GetType().Name != "FastCanvasItemsControl")
            return null;
        var property = element.GetType().GetField(
            "ItemsProperty",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as DependencyProperty;
        return property is null ? null : BindingOperations.GetBinding(element, property)?.Path?.Path;
    }

    private static FrameworkElement FindLabels(Window window)
    {
        return Host.Elements(window)
            .Where(x => x.IsVisible && ItemsBindingPath(x) == "LayerLabels")
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("LayerLabels FastCanvasItemsControl not found.");
    }

    private static int? LayerId(object? dataContext)
    {
        if (dataContext is null || dataContext.GetType().Name != "TimelineLayerLabelItemViewModel")
            return null;
        return dataContext.GetType()
            .GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(dataContext) as int?;
    }

    private static DependencyObject? Parent(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private static FrameworkElement FindLayerElement(FrameworkElement labels, int layer)
    {
        return Host.Elements(labels)
            .Where(x => x.IsVisible && LayerId(x.DataContext) == layer && x.ActualWidth > 20 && x.ActualHeight > 8)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Layer label row not realized: " + layer);
    }

    private static FrameworkElement FindLayerContextOwner(FrameworkElement labels, int layer)
    {
        var direct = Host.Elements(labels)
            .Where(x => x.IsVisible && LayerId(x.DataContext) == layer && x.ContextMenu is not null)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();
        if (direct is not null)
            return direct;

        DependencyObject? current = FindLayerElement(labels, layer);
        while (current is not null)
        {
            if (current is FrameworkElement { ContextMenu: not null } fe)
                return fe;
            current = Parent(current);
        }

        throw new InvalidOperationException("ContextMenu owner not found for layer " + layer);
    }

    private static async Task<Point> FindContextMenuPoint(
        FrameworkElement labels,
        int layer,
        ContextMenu menu)
    {
        var row = FindLayerElement(labels, layer);
        var rowRect = Host.ScreenRect(row);
        var labelsRect = Host.ScreenRect(labels);
        var left = Math.Max(rowRect.Left + 3, labelsRect.Left + 3);
        var right = Math.Min(rowRect.Right - 3, labelsRect.Right - 3);
        var y = Math.Max(rowRect.Top + 4, Math.Min(rowRect.Bottom - 4, rowRect.Y + rowRect.Height / 2));

        foreach (var x in new[]
        {
            left + 6,
            left + (right - left) * 0.25,
            left + (right - left) * 0.50,
            left + (right - left) * 0.75,
            right - 6
        }.Where(x => x >= left && x <= right))
        {
            if (menu.IsOpen)
                await Native.Key(0x1B);

            var point = new Point(x, y);
            await Native.Click(point, right: true);
            await Task.Delay(180);
            if (menu.IsOpen)
            {
                await Native.Key(0x1B);
                return point;
            }
        }

        throw new InvalidOperationException($"No native context-menu point found for L{layer}");
    }

    private sealed class WorkflowAdorner : Adorner
    {
        private readonly VisualCollection children;
        private readonly DirectDisplay display;
        private readonly PersistedFolder folder;
        private readonly Border header;

        internal WorkflowAdorner(UIElement adorned, DirectDisplay display, PersistedFolder folder)
            : base(adorned)
        {
            this.display = display;
            this.folder = folder;

            header = new Border
            {
                Width = 70,
                Height = Math.Max(18, display.Height - 6),
                CornerRadius = new CornerRadius(3),
                Background = Brushes.DimGray,
                Child = new TextBlock
                {
                    Text = (folder.IsCollapsed ? "▶ " : "▼ ") + folder.Name,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                },
                IsHitTestVisible = false
            };

            children = new VisualCollection(this) { header };
            IsHitTestVisible = false;
        }

        internal Rect ToggleRect =>
            new(
                2,
                display.Layout.VisualRowOfLogical(folder.Start) * display.Height + 3,
                header.Width,
                header.Height);

        protected override int VisualChildrenCount => children.Count;
        protected override Visual GetVisualChild(int index) => children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            header.Measure(new Size(header.Width, header.Height));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            header.Arrange(ToggleRect);
            return finalSize;
        }
    }

    private sealed class WorkflowController : IDisposable
    {
        private const string Tag = "CNWL.P4.Workflow";
        private readonly Window window;
        private readonly FrameworkElement labels;
        private readonly AdornerLayer adornerLayer;
        private readonly DirectDisplay display;
        private readonly Timeline timeline;
        private readonly string timelineKey;
        private readonly MouseButtonEventHandler mouseHandler;

        private bool disposed;
        private WorkflowAdorner? adorner;

        internal FolderDocument Document { get; private set; } = FolderDocument.Empty;
        internal int CreateClicks { get; private set; }
        internal int ToggleClicks { get; private set; }
        internal int RenameClicks { get; private set; }
        internal int UngroupClicks { get; private set; }
        internal int RightMaps { get; private set; }
        internal int LastLogicalLayer { get; private set; } = -1;
        internal int LastNativeMenuCount { get; private set; }
        internal MenuItem? LastCreateItem { get; private set; }
        internal MenuItem? LastRenameItem { get; private set; }
        internal MenuItem? LastUngroupItem { get; private set; }

        internal WorkflowController(
            Window window,
            FrameworkElement labels,
            DirectDisplay display,
            Timeline timeline)
        {
            this.window = window;
            this.labels = labels;
            this.display = display;
            this.timeline = timeline;
            timelineKey = timeline.ID.ToString("D");
            adornerLayer = AdornerLayer.GetAdornerLayer(labels)
                ?? throw new InvalidOperationException("LayerLabels has no AdornerLayer.");
            mouseHandler = OnPreviewMouseDown;
            window.AddHandler(Mouse.PreviewMouseDownEvent, mouseHandler, true);
            Refresh();
        }

        internal PersistedFolder? Folder =>
            FolderDocumentRules.FindTimeline(Document, timelineKey)?.Folders
                .FirstOrDefault(x => x.Id == FolderId);

        internal bool HasAdorner => adorner is not null;

        internal Point TogglePoint
        {
            get
            {
                if (adorner is null)
                    throw new InvalidOperationException("Folder adorner missing.");
                var rect = adorner.ToggleRect;
                return labels.PointToScreen(new Point(
                    rect.X + rect.Width / 2,
                    rect.Y + rect.Height / 2));
            }
        }

        private static void RemoveTagged(ContextMenu menu)
        {
            foreach (var item in menu.Items.OfType<FrameworkElement>()
                .Where(x => Equals(x.Tag, Tag)).ToArray())
                menu.Items.Remove(item);
        }

        private MenuItem Action(string header, Action action)
        {
            var item = new MenuItem { Header = header, Tag = Tag };
            item.Click += (_, _) => action();
            return item;
        }

        private void PrepareMenu(ContextMenu menu, int logical)
        {
            RemoveTagged(menu);
            LastCreateItem = null;
            LastRenameItem = null;
            LastUngroupItem = null;
            LastNativeMenuCount = menu.Items.Count;

            menu.Items.Add(new Separator { Tag = Tag });

            var containing = FolderDocumentRules.FindTimeline(Document, timelineKey)?.Folders
                .Where(x => x.Start <= logical && logical <= x.End)
                .OrderBy(x => x.End - x.Start)
                .FirstOrDefault();

            if (containing is null)
            {
                var decision = FolderUxCommands.EvaluateCreate(
                    Document,
                    timelineKey,
                    timeline.LayerSelection.SelectedLayers,
                    FolderId);

                LastCreateItem = Action(
                    decision.Allowed && decision.Start is not null && decision.End is not null
                        ? $"Create Folder L{decision.Start}-L{decision.End}"
                        : $"Create Folder ({decision.Status})",
                    () =>
                    {
                        if (!decision.Allowed)
                            return;
                        Document = FolderUxCommands.CreateFolder(
                            Document,
                            timelineKey,
                            timeline.LayerSelection.SelectedLayers,
                            FolderId,
                            "Folder 1");
                        CreateClicks++;
                        Refresh();
                    });
                LastCreateItem.IsEnabled = decision.Allowed;
                menu.Items.Add(LastCreateItem);
            }
            else
            {
                LastRenameItem = Action("Rename Folder", () =>
                {
                    Document = FolderUxCommands.RenameFolder(
                        Document, timelineKey, containing.Id, "Renamed");
                    RenameClicks++;
                    Refresh();
                });
                LastUngroupItem = Action("Ungroup Folder", () =>
                {
                    Document = FolderUxCommands.Ungroup(
                        Document, timelineKey, containing.Id);
                    UngroupClicks++;
                    Refresh();
                });
                menu.Items.Add(LastRenameItem);
                menu.Items.Add(LastUngroupItem);
            }

            RoutedEventHandler? closed = null;
            closed = (_, _) =>
            {
                menu.Closed -= closed;
                RemoveTagged(menu);
            };
            menu.Closed += closed;
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (disposed)
                return;

            var point = e.GetPosition(labels);
            if (point.X < 0 || point.X >= labels.ActualWidth
                || point.Y < 0 || point.Y >= labels.ActualHeight)
                return;

            if (e.ChangedButton == MouseButton.Left && adorner?.ToggleRect.Contains(point) == true)
            {
                e.Handled = true;
                var folder = Folder ?? throw new InvalidOperationException("Folder missing on toggle.");
                Document = FolderUxCommands.ToggleCollapsed(
                    Document, timelineKey, folder.Id);
                ToggleClicks++;
                Refresh();
                return;
            }

            if (e.ChangedButton != MouseButton.Right)
                return;

            var logical = display.Layout.DisplayYToLogical(point.Y, display.Height);
            var owner = FindLayerContextOwner(labels, logical);
            var menu = owner.ContextMenu
                ?? throw new InvalidOperationException("Mapped row ContextMenu missing.");

            PrepareMenu(menu, logical);
            e.Handled = true;
            menu.PlacementTarget = owner;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;

            RightMaps++;
            LastLogicalLayer = logical;
            Log($"workflow_right display={point} logical={logical}");
        }

        private void Refresh()
        {
            var state = FolderDocumentRules.FindTimeline(Document, timelineKey);
            var collapsed = state?.Folders
                .Where(x => x.IsCollapsed)
                .Select(x => new CollapsedSpan(x.Start, x.End))
                .ToArray() ?? [];
            display.SetSpans(collapsed);

            if (adorner is not null)
            {
                adornerLayer.Remove(adorner);
                adorner = null;
            }

            if (Folder is { } folder)
            {
                adorner = new WorkflowAdorner(labels, display, folder);
                adornerLayer.Add(adorner);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            window.RemoveHandler(Mouse.PreviewMouseDownEvent, mouseHandler);
            if (adorner is not null)
                adornerLayer.Remove(adorner);
            adorner = null;
        }
    }

    private static Point Center(FrameworkElement element)
    {
        var rect = Host.ScreenRect(element);
        if (rect.Width < 4 || rect.Height < 4)
            throw new InvalidOperationException("Element has no clickable rect: " + element.GetType().Name);
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static async Task Run(Window window)
    {
        DirectDisplay? display = null;
        WorkflowController? controller = null;
        try
        {
            window.WindowState = WindowState.Normal;
            window.Left = 0;
            window.Top = 0;
            window.Width = 1100;
            window.Height = 720;
            await Task.Delay(900);

            var view = Host.Elements(window)
                .Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();
            var vm = view.DataContext as TimelineViewModel
                ?? throw new InvalidOperationException("TimelineViewModel missing.");
            var timeline = Host.Get(vm, "Timeline") as Timeline
                ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline
                ?? throw new InvalidOperationException("Timeline missing.");
            var scroll = Host.Elements(view).OfType<ScrollViewer>()
                .Where(x => x.IsVisible && x.ActualHeight > 50)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();
            var host = new Host(
                window,
                timeline,
                vm,
                view,
                scroll.Content as FrameworkElement ?? throw new InvalidOperationException("Timeline content missing."),
                scroll);

            host.Activate();
            Native.Release();

            var character = new Character { Name = "CNWL_P4_WORKFLOW" };
            var fixtures = new List<IItem>();
            for (var layer = 0; layer <= 8; layer++)
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20,
                    Layer = layer,
                    Length = 60,
                    Serif = "L" + layer,
                    Remark = "CNWL_P4_WORKFLOW_L" + layer
                };
                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add L" + layer);
                fixtures.Add(item);
            }
            await Task.Delay(700);

            var baselineLayers = fixtures.ToDictionary(x => x, x => x.Layer, ReferenceEqualityComparer.Instance);
            timeline.LayerSelection.SelectedLayers = ImmutableList.Create(2, 3, 4);

            var labels = FindLabels(window);
            Check("layer_labels_found", labels.IsVisible);

            var row2Owner = FindLayerContextOwner(labels, 2);
            var row2Menu = row2Owner.ContextMenu
                ?? throw new InvalidOperationException("Layer 2 native menu missing.");
            var row2Point = await FindContextMenuPoint(labels, 2, row2Menu);
            Check("identity_native_menu_control", row2Point.X >= 0);

            display = new DirectDisplay(host, Log);
            controller = new WorkflowController(window, labels, display, timeline);
            await Task.Delay(350);

            // CREATE through the real YMM4 row menu.
            await Native.Click(row2Point, right: true);
            await Task.Delay(450);
            Check("create_menu_visible",
                controller.LastCreateItem is { IsEnabled: true, IsVisible: true }
                && controller.LastNativeMenuCount > 0);
            if (controller.LastCreateItem is null)
                throw new InvalidOperationException("Create menu item missing.");
            await Native.Click(Center(controller.LastCreateItem));
            await Task.Delay(500);

            var created = controller.Folder;
            Check("create_action_clicked", controller.CreateClicks == 1);
            Check("folder_created_exact_range",
                created is { Start: 2, End: 4, Name: "Folder 1", IsCollapsed: false });
            Check("folder_overlay_present", controller.HasAdorner);
            Check("create_document_serializable",
                FolderDocumentCodec.Load(FolderDocumentCodec.Save(controller.Document)).Success);
            Check("create_starts_expanded", !display.Layout.IsHidden(3));

            // COLLAPSE / EXPAND through the visible folder overlay.
            await Native.Click(controller.TogglePoint);
            await Task.Delay(450);
            Check("collapse_action_clicked",
                controller.ToggleClicks == 1
                && controller.Folder?.IsCollapsed == true
                && display.Layout.IsHidden(3)
                && display.Layout.IsHidden(4));

            await Native.Click(controller.TogglePoint);
            await Task.Delay(450);
            Check("expand_action_clicked",
                controller.ToggleClicks == 2
                && controller.Folder?.IsCollapsed == false
                && !display.Layout.IsHidden(3)
                && !display.Layout.IsHidden(4));

            // RENAME through the same mapped native ContextMenu surface.
            await Native.Click(row2Point, right: true);
            await Task.Delay(450);
            Check("rename_menu_visible", controller.LastRenameItem is { IsVisible: true });
            if (controller.LastRenameItem is null)
                throw new InvalidOperationException("Rename menu item missing.");
            await Native.Click(Center(controller.LastRenameItem));
            await Task.Delay(450);
            Check("rename_action_clicked",
                controller.RenameClicks == 1
                && controller.Folder?.Name == "Renamed");
            Check("rename_document_serializable",
                FolderDocumentCodec.Load(FolderDocumentCodec.Save(controller.Document)).Success);

            // UNGROUP means metadata only: Timeline rows/items are untouched.
            await Native.Click(row2Point, right: true);
            await Task.Delay(450);
            Check("ungroup_menu_visible", controller.LastUngroupItem is { IsVisible: true });
            if (controller.LastUngroupItem is null)
                throw new InvalidOperationException("Ungroup menu item missing.");
            await Native.Click(Center(controller.LastUngroupItem));
            await Task.Delay(500);

            Check("ungroup_action_clicked", controller.UngroupClicks == 1);
            Check("folder_metadata_removed", controller.Folder is null && !controller.HasAdorner);
            Check("ungroup_restores_identity_display",
                !display.Layout.IsHidden(2)
                && !display.Layout.IsHidden(3)
                && !display.Layout.IsHidden(4));
            Check("timeline_items_preserved",
                timeline.Items.Count == fixtures.Count
                && fixtures.All(x => timeline.Items.Any(y => ReferenceEquals(x, y))));
            Check("timeline_layers_preserved",
                fixtures.All(x => baselineLayers.TryGetValue(x, out var layer) && x.Layer == layer));
            Check("final_document_serializable",
                FolderDocumentCodec.Load(FolderDocumentCodec.Save(controller.Document)).Success);
            Check("right_clicks_mapped", controller.RightMaps == 3);
            Check("no_display_failure_or_reentry", display.Failure is null && display.Reentries == 0);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));

            Fact("final_document", FolderDocumentCodec.Save(controller.Document));

            controller.Dispose();
            controller = null;
            display.Dispose();
            Check("display_subscriptions_released", display.SubscriptionCount == 0);
            display = null;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                controller?.Dispose();
                display?.Dispose();
                Native.Release();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            Finish();
        }
    }
}
