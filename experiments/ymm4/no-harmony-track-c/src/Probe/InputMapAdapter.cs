using System.Windows;
using System.Windows.Input;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// Input only. A click must not transition display state before native selection.
internal sealed class InputMapAdapter : IDisposable
{
    private readonly Host host;
    private readonly DirectDisplay display;
    private readonly Action<string> log;
    private readonly Dictionary<IItem, int> group = new(ReferenceEqualityComparer.Instance);
    private IItem? anchor;
    private int originalLayer;
    private Point down;
    private bool marquee, disposed, dragging;
    internal int Corrections { get; private set; }
    internal int RightMaps { get; private set; }
    internal int Marquees { get; private set; }
    internal bool GestureActive => anchor is not null || marquee;
    internal InputMapAdapter(Host host, DirectDisplay display, Action<string> log)
    {
        this.host = host; this.display = display; this.log = log;
        host.Source.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down), true);
        host.Source.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewMove), true);
        host.Source.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewUp), true);
        host.Source.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(Move), true);
        host.Source.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(Up), true);
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (disposed) return; display.ThrowIfFailed(); var p = Mouse.GetPosition(host.Source);
        if (e.ChangedButton == MouseButton.Right)
        {
            var logical = display.Layout.MapDisplayPointToLogical(p, display.Height);
            log($"right_before display={p} logical={logical}");
            Host.SetReactive(host.Vm, "TimelineCursorPositionWhenRightClick", logical); RightMaps++; log("right_after"); return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        var view = Host.AncestorItem(e.OriginalSource as DependencyObject); down = p;
        if (view is null && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            marquee = true; e.Handled = true; Mouse.Capture(host.Source, CaptureMode.SubTree); log("marquee_down"); return;
        }
        var item = Host.Item(view?.DataContext); if (item is null) return;
        anchor = item; originalLayer = item.Layer; dragging = false; group.Clear();
        IEnumerable<IItem> selected = host.Timeline.SelectedItems.Any(x => ReferenceEquals(x, item)) ? host.Timeline.SelectedItems : new[] { item };
        foreach (var member in selected) group[member] = member.Layer;
        log($"down L{originalLayer} F{item.Frame} group={group.Count} point={p}");
    }
    private bool OverThreshold(Point p) => Math.Abs(p.X - down.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - down.Y) >= SystemParameters.MinimumVerticalDragDistance;
    private void PreviewMove(object sender, MouseEventArgs e)
    {
        if (marquee) { e.Handled = true; return; }
        if (anchor is null || dragging || e.LeftButton != MouseButtonState.Pressed || !OverThreshold(Mouse.GetPosition(host.Source))) return;
        dragging = true; display.BeginGesture(group.Keys);
    }
    private void PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !marquee) return;
        var rect = new Rect(host.Source.PointToScreen(down), host.Source.PointToScreen(Mouse.GetPosition(host.Source)));
        marquee = false; e.Handled = true; Mouse.Capture(null);
        var selected = host.ItemViews().Where(x => x.IsVisible && x.IsHitTestVisible && x.Opacity > 0 && rect.IntersectsWith(Host.ScreenRect(x)))
            .Select(x => Host.Item(x.DataContext)).OfType<IItem>().Where(x => !display.Layout.IsHidden(x.Layer)).Distinct<IItem>(ReferenceEqualityComparer.Instance).ToArray();
        host.Timeline.SelectItems(selected); Marquees++; log("marquee_up selected=" + selected.Length);
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (disposed || anchor is null || !dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var p = Mouse.GetPosition(host.Source); var layout = display.Layout; var height = display.Height;
        var desired = layout.DisplayYToLogical(layout.VisualRowOfLogical(originalLayer) * height + p.Y - down.Y + height / 2.0, height);
        var delta = desired - originalLayer;
        if (group.Values.Any(layer => layer + delta < 0 || layer + delta > layout.MaxLayer)) return;
        var before = string.Join("|", group.Keys.Select(x => $"L{x.Layer}:F{x.Frame}"));
        foreach (var (item, layer) in group) if (item.Layer != layer + delta) { item.Layer = layer + delta; Corrections++; }
        log($"move desired={desired} before={before} after={string.Join("|", group.Keys.Select(x => $"L{x.Layer}:F{x.Frame}"))}");
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (anchor is not null) log($"up L{anchor.Layer} F{anchor.Frame}");
        anchor = null; group.Clear(); if (dragging) display.EndGesture(); dragging = false;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        host.Source.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down));
        host.Source.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewMove));
        host.Source.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewUp));
        host.Source.RemoveHandler(Mouse.MouseMoveEvent, new MouseEventHandler(Move));
        host.Source.RemoveHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(Up));
        if (marquee && ReferenceEquals(Mouse.Captured, host.Source)) Mouse.Capture(null);
        marquee = false; anchor = null; group.Clear(); if (dragging) display.EndGesture(); dragging = false; log("input_detached");
    }
}
