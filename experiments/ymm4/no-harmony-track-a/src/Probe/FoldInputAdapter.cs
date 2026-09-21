using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// Track A intentionally never writes host VM geometry or calls FastCanvas.UpdateAll.
internal sealed class FoldInputAdapter : IDisposable
{
    private readonly Host host;
    private readonly int height;
    private readonly Action<string> log;
    private readonly Dictionary<FrameworkElement, (object Transform, object Opacity, object Hit)> originals = [];
    private readonly Dictionary<IItem, int> group = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer timer;
    private FolderLayout layout;
    private IItem? anchor;
    private int anchorLayer;
    private Point down, marqueeEnd;
    private bool marquee, disposed, applying;
    private int applications;
    internal int Corrections { get; private set; }
    internal bool MarqueeHandled { get; private set; }
    internal int RightMaps { get; private set; }
    internal FoldInputAdapter(Host host, int height, FolderLayout layout, Action<string> log)
    {
        this.host = host; this.height = height; this.layout = layout; this.log = log;
        host.Source.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down), true);
        host.Source.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewMove), true);
        host.Source.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewUp), true);
        host.Source.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(BubbleMove), true);
        host.Source.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(BubbleUp), true);
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += Tick;
        Apply(); timer.Start();
    }
    private void Tick(object? sender, EventArgs args)
    {
        try { Apply(); } catch (Exception ex) { log("adapter_failure=" + ex); Dispose(); }
    }
    internal void SetLayout(FolderLayout next)
    {
        if (anchor is not null || marquee) throw new InvalidOperationException("Layout switch during gesture");
        layout = next; Apply();
    }
    internal void Apply()
    {
        if (disposed) return;
        if (applying) throw new InvalidOperationException("RenderTransform reentrancy");
        if (++applications > 3000) throw new InvalidOperationException("RenderTransform application budget exceeded");
        applying = true;
        try
        {
            foreach (var view in host.ItemViews())
            {
                var item = Host.Item(view.DataContext);
                if (item is null || item.Layer < 0 || item.Layer > layout.MaxLayer) continue;
                if (!originals.ContainsKey(view))
                    originals.Add(view, (view.ReadLocalValue(UIElement.RenderTransformProperty), view.ReadLocalValue(UIElement.OpacityProperty), view.ReadLocalValue(UIElement.IsHitTestVisibleProperty)));
                var hidden = layout.IsHidden(item.Layer);
                var translation = layout.TranslationY(item.Layer, height);
                if (view.RenderTransform is not TranslateTransform current || current.X != 0 || current.Y != translation)
                    view.SetCurrentValue(UIElement.RenderTransformProperty, new TranslateTransform(0, translation));
                view.SetCurrentValue(UIElement.OpacityProperty, hidden ? 0.0 : 1.0);
                view.SetCurrentValue(UIElement.IsHitTestVisibleProperty, !hidden);
            }
        }
        finally { applying = false; }
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (disposed) return;
        var p = Mouse.GetPosition(host.Source);
        if (e.ChangedButton == MouseButton.Right)
        {
            var mapped = layout.MapDisplayPointToLogical(p, height);
            log($"right_before raw={p} mapped={mapped}");
            Host.SetReactive(host.Vm, "TimelineCursorPositionWhenRightClick", mapped);
            RightMaps++; log("right_after"); return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        var itemView = Host.AncestorItem(e.OriginalSource as DependencyObject);
        down = p;
        if (itemView is null && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            marquee = true; MarqueeHandled = true; marqueeEnd = p; e.Handled = true;
            Mouse.Capture(host.Source, CaptureMode.SubTree); log("marquee_down"); return;
        }
        var item = Host.Item(itemView?.DataContext);
        if (item is null) return;
        anchor = item; anchorLayer = item.Layer; group.Clear();
        IEnumerable<IItem> selected = host.Timeline.SelectedItems.Any(x => ReferenceEquals(x, item)) ? host.Timeline.SelectedItems : new[] { item };
        foreach (var candidate in selected) group[candidate] = candidate.Layer;
        log($"down L{anchorLayer} group={group.Count} raw={p}");
    }
    private void PreviewMove(object sender, MouseEventArgs e)
    {
        if (!marquee) return;
        marqueeEnd = Mouse.GetPosition(host.Source); e.Handled = true;
    }
    private void PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !marquee) return;
        marqueeEnd = Mouse.GetPosition(host.Source); marquee = false; e.Handled = true; Mouse.Capture(null);
        var box = new Rect(host.Source.PointToScreen(down), host.Source.PointToScreen(marqueeEnd));
        var selected = host.ItemViews().Where(x => x.IsVisible && x.IsHitTestVisible && x.Opacity > 0 && box.IntersectsWith(Host.ScreenRect(x)))
            .Select(x => Host.Item(x.DataContext)).OfType<IItem>().Distinct<IItem>(ReferenceEqualityComparer.Instance).ToArray();
        host.Timeline.SelectItems(selected); log("marquee_up selected=" + selected.Length);
    }
    private void BubbleMove(object sender, MouseEventArgs e)
    {
        if (anchor is null || disposed || e.LeftButton != MouseButtonState.Pressed) return;
        var p = Mouse.GetPosition(host.Source);
        if (Math.Abs(p.X - down.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - down.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var anchorY = layout.VisualRowOfLogical(anchorLayer) * height + (p.Y - down.Y) + height / 2.0;
        var desired = layout.DisplayYToLogical(anchorY, height);
        var delta = desired - anchorLayer;
        var before = string.Join("|", group.Keys.Select(x => $"L{x.Layer}:F{x.Frame}"));
        foreach (var (item, original) in group)
        {
            var target = original + delta;
            if (target < 0 || target > layout.MaxLayer) throw new InvalidOperationException("Group target outside fixture");
            if (item.Layer != target) { item.Layer = target; Corrections++; }
        }
        Apply(); log($"move desired={desired} before={before} after={string.Join("|", group.Keys.Select(x => $"L{x.Layer}:F{x.Frame}"))}");
    }
    private void BubbleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (anchor is not null) log($"up L{anchor.Layer}:F{anchor.Frame}");
        anchor = null; group.Clear(); Apply();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); timer.Tick -= Tick;
        host.Source.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down));
        host.Source.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewMove));
        host.Source.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewUp));
        host.Source.RemoveHandler(Mouse.MouseMoveEvent, new MouseEventHandler(BubbleMove));
        host.Source.RemoveHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(BubbleUp));
        if (marquee && ReferenceEquals(Mouse.Captured, host.Source)) Mouse.Capture(null);
        foreach (var (view, prior) in originals)
        {
            Restore(view, UIElement.RenderTransformProperty, prior.Transform);
            Restore(view, UIElement.OpacityProperty, prior.Opacity);
            Restore(view, UIElement.IsHitTestVisibleProperty, prior.Hit);
        }
        originals.Clear(); group.Clear(); anchor = null;
        log("adapter_detached applications=" + applications);
    }
    private static void Restore(DependencyObject target, DependencyProperty property, object value)
    {
        if (value == DependencyProperty.UnsetValue) target.ClearValue(property); else target.SetValue(property, value);
    }
}
