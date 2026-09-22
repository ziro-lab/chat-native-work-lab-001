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

    private static DependencyObject? Parent(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private static int? FindLayerFromSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = Parent(current))
            if (current is FrameworkElement fe && LayerId(fe.DataContext) is int layer)
                return layer;
        return null;
    }

    private static FrameworkElement? FindContextMenuOwnerFromSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = Parent(current))
            if (current is FrameworkElement { ContextMenu: not null } fe)
                return fe;
        return null;
    }

    private static (Point Screen, string HitType, string OwnerType) FindNativeLayerPoint(
        FrameworkElement labels,
        int layer)
    {
        var row = FindLayerElement(labels, layer);
        var rowRect = Host.ScreenRect(row);
        var labelsRect = Host.ScreenRect(labels);

        var left = Math.Max(rowRect.Left + 2, labelsRect.Left + 28);
        var right = Math.Min(rowRect.Right - 2, labelsRect.Right - 2);
        var top = Math.Max(rowRect.Top + 2, labelsRect.Top + 2);
        var bottom = Math.Min(rowRect.Bottom - 2, labelsRect.Bottom - 2);

        if (right <= left || bottom <= top)
            throw new InvalidOperationException($"No scan rect for layer {layer}: row={rowRect} labels={labelsRect}");

        var ys = new[]
        {
            (top + bottom) / 2,
            top + (bottom - top) * 0.25,
            top + (bottom - top) * 0.75
        };

        foreach (var y in ys)
        {
            for (var x = left; x <= right; x += 4)
            {
                var screen = new Point(x, y);
                var local = labels.PointFromScreen(screen);
                var hit = VisualTreeHelper.HitTest(labels, local)?.VisualHit;
                if (hit is null || FindLayerFromSource(hit) != layer)
                    continue;

                var owner = FindContextMenuOwnerFromSource(hit);
                if (owner is null)
                    continue;

                return (
                    screen,
                    hit.GetType().FullName ?? hit.GetType().Name,
                    owner.GetType().FullName ?? owner.GetType().Name);
            }
        }

        throw new InvalidOperationException($"No real hit-test point found for layer {layer}.");
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

        private static void RemoveTagged(ContextMenu menu)
        {
            foreach (var item in menu.Items.OfType<FrameworkElement>().Where(x => Equals(x.Tag, Tag)).ToArray())
                menu.Items.Remove(item);
        }

        private void OnOpening(object sender, ContextMenuEventArgs e)
        {
            if (disposed) return;
            var layer = FindLayerFromSource(e.OriginalSource as DependencyObject);
            var owner = FindContextMenuOwnerFromSource(e.OriginalSource as DependencyObject);
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
            children = new VisualCollection(this) { toggle };

            // Set this only after the visual collection exists: WPF may query
            // VisualChildrenCount while propagating inherited hit-test state.
            // The visual is display-only; input is handled on LayerLabels.
            IsHitTestVisible = false;
        }

        internal Rect ToggleRect =>
            new(
                2,
                display.Layout.VisualRowOfLogical(span.Start) * display.Height + 3,
                toggle.Width,
                toggle.Height);

        internal void SetCollapsed(bool collapsed)
        {
            toggle.Content = collapsed ? "▶" : "▼";
            InvalidateArrange();
        }

        protected override int VisualChildrenCount => children.Count;
        protected override Visual GetVisualChild(int index) => children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            toggle.Measure(new Size(toggle.Width, toggle.Height));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            toggle.Arrange(ToggleRect);
            return finalSize;
        }
    }

    private sealed class FolderToggleInputLease : IDisposable
    {
        private readonly FrameworkElement labels;
        private readonly DirectDisplay display;
        private readonly CollapsedSpan span;
        private readonly FolderOwnerAdorner adorner;
        private readonly MouseButtonEventHandler handler;
        private bool disposed;

        internal int Clicks { get; private set; }

        internal FolderToggleInputLease(
            FrameworkElement labels,
            DirectDisplay display,
            CollapsedSpan span,
            FolderOwnerAdorner adorner)
        {
            this.labels = labels;
            this.display = display;
            this.span = span;
            this.adorner = adorner;
            handler = OnPreviewMouseDown;
            labels.AddHandler(Mouse.PreviewMouseDownEvent, handler, true);
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (disposed || e.ChangedButton != MouseButton.Left)
                return;

            var point = e.GetPosition(labels);
            if (!adorner.ToggleRect.Contains(point))
                return;

            e.Handled = true;
            var wasCollapsed = display.Layout.IsHidden(span.Start + 1);
            var nowCollapsed = !wasCollapsed;
            display.SetSpans(nowCollapsed ? [span] : []);
            adorner.SetCollapsed(nowCollapsed);
            Clicks++;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            labels.RemoveHandler(Mouse.PreviewMouseDownEvent, handler);
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
        FolderToggleInputLease? toggleLease = null;
        AdornerLayer? adornerLayer = null;
        MouseButtonEventHandler? nativeInputObserver = null;
        FrameworkElement? labelsForObserver = null;
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

            var nativeRoute = FindNativeLayerPoint(labels, 6);
            var nativePoint = nativeRoute.Screen;
            Fact("layer6_hit_type", nativeRoute.HitType);
            Fact("layer6_context_owner", nativeRoute.OwnerType);
            Fact("layer6_native_point", nativePoint);

            var nativeLeftDown = 0;
            nativeInputObserver = (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left
                    && FindLayerFromSource(e.OriginalSource as DependencyObject) == 6)
                    nativeLeftDown++;
            };
            labelsForObserver = labels;
            labels.AddHandler(Mouse.PreviewMouseDownEvent, nativeInputObserver, true);

            await Native.Click(nativePoint);
            Check("baseline_native_layer_input_observed", nativeLeftDown > 0);
            Fact("baseline_selected_layers", string.Join(",", timeline.LayerSelection.SelectedLayers));

            adornerLayer = AdornerLayer.GetAdornerLayer(labels)
                ?? throw new InvalidOperationException("LayerLabels has no AdornerLayer.");
            adorner = new FolderOwnerAdorner(labels, display, span);
            adornerLayer.Add(adorner);
            toggleLease = new FolderToggleInputLease(labels, display, span, adorner);
            await Task.Delay(500);
            Check("adorner_attached", adornerLayer.GetAdorners(labels)?.Contains(adorner) == true);
            Check("adorner_input_transparent", !adorner.IsHitTestVisible);
            Check("toggle_on_screen", Host.ScreenRect(labels).Contains(Center(adorner.Toggle)));

            await Native.Click(Center(adorner.Toggle));
            await Task.Delay(500);
            display.ThrowIfFailed();
            Check("native_toggle_expands", toggleLease.Clicks == 1 && !display.Layout.IsHidden(3));

            await Native.Click(Center(adorner.Toggle));
            await Task.Delay(500);
            display.ThrowIfFailed();
            Check("native_toggle_collapses", toggleLease.Clicks == 2 && display.Layout.IsHidden(3));

            var beforePassThrough = nativeLeftDown;
            await Native.Click(nativePoint);
            Check("outside_overlay_preserves_native_layer_click", nativeLeftDown == beforePassThrough + 1);
            Fact("post_overlay_selected_layers", string.Join(",", timeline.LayerSelection.SelectedLayers));

            menuLease = new FolderMenuLease(labels);
            await Native.Click(nativePoint, right: true);
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
            if (nativeInputObserver is not null)
            {
                labels.RemoveHandler(Mouse.PreviewMouseDownEvent, nativeInputObserver);
                nativeInputObserver = null;
                labelsForObserver = null;
            }
            toggleLease.Dispose();
            toggleLease = null;
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
                if (nativeInputObserver is not null && labelsForObserver is not null)
                    labelsForObserver.RemoveHandler(Mouse.PreviewMouseDownEvent, nativeInputObserver);
                toggleLease?.Dispose();
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
