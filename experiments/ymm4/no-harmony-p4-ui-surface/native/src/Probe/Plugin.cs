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
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class P4UiSurfaceEntry : ILocalizePlugin
{
    public string Name => "CNWL P4 folder UI surface";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_P4_UI_SURFACE_DIR");
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

    private static void Fact(string name, object? value) => facts[name] = value?.ToString() ?? "<null>";

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
            status = failed ? "FAIL_P4_UI_SURFACE" : "PASS_P4_UI_SURFACE",
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

    private static FrameworkElement FindLabels(Window window, object activeTimeline)
    {
        var candidates = Host.Elements(window)
            .Where(x => x.IsVisible && ItemsBindingPath(x) == "LayerLabels")
            .ToArray();
        Fact("layer_label_candidates", candidates.Length);
        return candidates.FirstOrDefault()
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

    private static FrameworkElement FindLayerElement(FrameworkElement labels, int layer)
    {
        return Host.Elements(labels)
            .Where(x => x.IsVisible && LayerId(x.DataContext) == layer && x.ActualWidth > 20 && x.ActualHeight > 8)
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Layer label row not realized: " + layer);
    }

    private sealed class FolderMenuLease : IDisposable
    {
        private const string Tag = "CNWL.P4.Menu";
        private readonly FrameworkElement labels;
        private readonly ContextMenuEventHandler opening;
        private bool disposed;

        internal int Openings { get; private set; }
        internal int Clicks { get; private set; }
        internal int LastLayer { get; private set; } = -1;
        internal int LastOriginalCount { get; private set; }
        internal ContextMenu? LastMenu { get; private set; }
        internal MenuItem? LastItem { get; private set; }

        internal FolderMenuLease(FrameworkElement labels)
        {
            this.labels = labels;
            opening = OnOpening;
            labels.AddHandler(FrameworkElement.ContextMenuOpeningEvent, opening, true);
        }

        private static DependencyObject? Parent(DependencyObject current) =>
            current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);

        private static int? FindLayer(DependencyObject? source)
        {
            for (var current = source; current is not null; current = Parent(current))
                if (current is FrameworkElement fe && LayerId(fe.DataContext) is int layer)
                    return layer;
            return null;
        }

        private static FrameworkElement? FindMenuOwner(DependencyObject? source)
        {
            for (var current = source; current is not null; current = Parent(current))
                if (current is FrameworkElement { ContextMenu: not null } fe)
                    return fe;
            return null;
        }

        private static void RemoveTagged(ContextMenu menu)
        {
            foreach (var item in menu.Items.OfType<FrameworkElement>().Where(x => Equals(x.Tag, Tag)).ToArray())
                menu.Items.Remove(item);
        }

        private void OnOpening(object sender, ContextMenuEventArgs e)
        {
            if (disposed) return;
            var layer = FindLayer(e.OriginalSource as DependencyObject);
            var owner = FindMenuOwner(e.OriginalSource as DependencyObject);
            if (layer is null || owner?.ContextMenu is not { } menu)
                return;

            RemoveTagged(menu);
            LastOriginalCount = menu.Items.Count;
            var separator = new Separator { Tag = Tag };
            var item = new MenuItem { Header = "CNWL Folder action", Tag = Tag };
            item.Click += (_, _) => Clicks++;
            menu.Items.Add(separator);
            menu.Items.Add(item);

            Openings++;
            LastLayer = layer.Value;
            LastMenu = menu;
            LastItem = item;

            RoutedEventHandler? closed = null;
            closed = (_, _) =>
            {
                menu.Closed -= closed;
                RemoveTagged(menu);
            };
            menu.Closed += closed;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            labels.RemoveHandler(FrameworkElement.ContextMenuOpeningEvent, opening);
            if (LastMenu is not null)
                RemoveTagged(LastMenu);
        }
    }

    private sealed class FolderOwnerAdorner : Adorner
    {
        private readonly VisualCollection children;
        private readonly DirectDisplay display;
        private readonly CollapsedSpan span;
        private readonly Button toggle;

        internal int Clicks { get; private set; }
        internal Button Toggle => toggle;

        internal FolderOwnerAdorner(UIElement adornedElement, DirectDisplay display, CollapsedSpan span)
            : base(adornedElement)
        {
            this.display = display;
            this.span = span;
            toggle = new Button
            {
                Content = "▶",
                Padding = new Thickness(0),
                Width = 22,
                Height = Math.Max(18, display.Height - 6),
                ToolTip = "CNWL folder toggle",
                Focusable = false
            };
            toggle.Click += (_, _) =>
            {
                Clicks++;
                var collapsed = display.Layout.IsHidden(span.Start + 1);
                display.SetSpans(collapsed ? [] : [span]);
                toggle.Content = collapsed ? "▼" : "▶";
                InvalidateArrange();
            };
            children = new VisualCollection(this) { toggle };
        }

        private Rect ToggleRect() =>
            new(
                2,
                display.Layout.VisualRowOfLogical(span.Start) * display.Height + 3,
                toggle.Width,
                toggle.Height);

        protected override int VisualChildrenCount => children.Count;
        protected override Visual GetVisualChild(int index) => children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            toggle.Measure(new Size(toggle.Width, toggle.Height));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            toggle.Arrange(ToggleRect());
            return finalSize;
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        {
            // This Adorner is full-size only so its child can track folded rows.
            // It contains no full-size Canvas: only the explicit button owns input.
            return ToggleRect().Contains(hitTestParameters.HitPoint)
                ? base.HitTestCore(hitTestParameters)
                : null;
        }
    }

    private static Point Center(FrameworkElement element)
    {
        var rect = Host.ScreenRect(element);
        if (rect.Width < 4 || rect.Height < 4)
            throw new InvalidOperationException("Element has no clickable rect: " + element.GetType().Name + " " + rect);
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static async Task Run(Window window)
    {
        DirectDisplay? display = null;
        FolderMenuLease? menuLease = null;
        FolderOwnerAdorner? adorner = null;
        AdornerLayer? adornerLayer = null;
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
            var character = new Character { Name = "CNWL_P4_UI" };
            for (var layer = 0; layer <= 8; layer++)
            {
                var item = new VoiceItem(character)
                {
                    Frame = 20,
                    Layer = layer,
                    Length = 60,
                    Serif = "L" + layer,
                    Remark = "CNWL_P4_L" + layer
                };
                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add L" + layer);
            }
            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(700);

            display = new DirectDisplay(host, Log);
            var span = new CollapsedSpan(2, 5);
            display.SetSpans([span]);
            await Task.Delay(700);
            display.ThrowIfFailed();

            Check("fixture_folded", display.Layout.IsHidden(3) && !display.Layout.IsHidden(2));
            var labels = FindLabels(window, timeline);
            Fact("labels_type", labels.GetType().FullName);
            Check("layer_labels_found", labels.IsVisible && ItemsBindingPath(labels) == "LayerLabels");

            adornerLayer = AdornerLayer.GetAdornerLayer(labels)
                ?? throw new InvalidOperationException("LayerLabels has no AdornerLayer.");
            adorner = new FolderOwnerAdorner(labels, display, span);
            adornerLayer.Add(adorner);
            await Task.Delay(500);
            Check("adorner_attached", adornerLayer.GetAdorners(labels)?.Contains(adorner) == true);
            Check("toggle_on_screen", Host.ScreenRect(labels).Contains(Center(adorner.Toggle)));

            await Native.Click(Center(adorner.Toggle));
            await Task.Delay(500);
            display.ThrowIfFailed();
            Check("native_toggle_expands", adorner.Clicks == 1 && !display.Layout.IsHidden(3));

            await Native.Click(Center(adorner.Toggle));
            await Task.Delay(500);
            display.ThrowIfFailed();
            Check("native_toggle_collapses", adorner.Clicks == 2 && display.Layout.IsHidden(3));

            var row6 = FindLayerElement(labels, 6);
            var rowRect = Host.ScreenRect(row6);
            var outsideToggle = new Point(rowRect.Right - 12, rowRect.Y + rowRect.Height / 2);
            timeline.LayerSelection.Clear();
            await Native.Click(outsideToggle);
            Check("outside_overlay_preserves_native_layer_click", timeline.LayerSelection.SelectedLayers.Contains(6));

            menuLease = new FolderMenuLease(labels);
            await Native.Click(outsideToggle, right: true);
            await Task.Delay(500);
            Check("context_open_observed", menuLease.Openings == 1 && menuLease.LastLayer == 6);
            Check("native_menu_preserved_and_extended",
                menuLease.LastMenu is { IsOpen: true }
                && menuLease.LastMenu.Items.Count >= menuLease.LastOriginalCount + 2
                && menuLease.LastItem is not null);

            if (menuLease.LastItem is null)
                throw new InvalidOperationException("Injected menu item missing.");
            await Native.Click(Center(menuLease.LastItem));
            await Task.Delay(450);
            Check("native_menu_action_click", menuLease.Clicks == 1);
            Check("menu_cleanup_after_close",
                menuLease.LastMenu is not null
                && !menuLease.LastMenu.Items.OfType<FrameworkElement>()
                    .Any(x => Equals(x.Tag, "CNWL.P4.Menu")));

            menuLease.Dispose();
            menuLease = null;
            adornerLayer.Remove(adorner);
            adorner = null;
            await Task.Delay(250);
            Check("adorner_detached", adornerLayer.GetAdorners(labels)?.OfType<FolderOwnerAdorner>().Any() != true);
            Check("no_display_failure_or_reentry", display.Failure is null && display.Reentries == 0);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));

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
                menuLease?.Dispose();
                if (adorner is not null && adornerLayer is not null)
                    adornerLayer.Remove(adorner);
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
