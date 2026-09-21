using System.Windows;
using System.Windows.Input;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// Input adapter only. The first bubbling MouseMove has already passed through
// TimelineItemView's native drag handler before gesture-freeze begins.
// This preserves native drag initialization, Frame movement, snapping and history.
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
    internal int BubbleMoves { get; private set; }
    internal bool GestureActive => anchor is not null || marquee || display.GestureActive;

    internal InputMapAdapter(Host host, DirectDisplay display, Action<string> log)
    {
        this.host = host;
        this.display = display;
        this.log = log;

        host.Source.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down), true);
        host.Source.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewMove), true);
        host.Source.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewUp), true);
        host.Source.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(Move), true);
        host.Source.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(Up), true);
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (disposed)
            return;

        display.ThrowIfFailed();
        var p = Mouse.GetPosition(host.Source);

        if (e.ChangedButton == MouseButton.Right)
        {
            var logical = display.Layout.MapDisplayPointToLogical(p, display.Height);
            log($"right_before display={p} logical={logical}");
            Host.SetReactive(host.Vm, "TimelineCursorPositionWhenRightClick", logical);
            RightMaps++;
            log("right_after");
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var view = Host.AncestorItem(e.OriginalSource as DependencyObject);
        down = p;

        if (view is null && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            marquee = true;
            e.Handled = true;
            Mouse.Capture(host.Source, CaptureMode.SubTree);
            log("marquee_down");
            return;
        }

        var item = Host.Item(view?.DataContext);
        if (item is null)
            return;

        anchor = item;
        originalLayer = item.Layer;
        dragging = false;
        group.Clear();

        IEnumerable<IItem> selected =
            host.Timeline.SelectedItems.Any(x => ReferenceEquals(x, item))
                ? host.Timeline.SelectedItems
                : new[] { item };

        foreach (var member in selected)
            group[member] = member.Layer;

        log($"down L{originalLayer} F{item.Frame} group={group.Count} point={p}");
    }

    private bool OverThreshold(Point p) =>
        Math.Abs(p.X - down.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(p.Y - down.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void PreviewMove(object sender, MouseEventArgs e)
    {
        // Only marquee replaces preview input. Native item drag must reach its normal
        // Preview/Bubble handlers before this adapter changes any display state.
        if (marquee)
            e.Handled = true;
    }

    private void PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !marquee)
            return;

        var rect = new Rect(
            host.Source.PointToScreen(down),
            host.Source.PointToScreen(Mouse.GetPosition(host.Source)));

        marquee = false;
        e.Handled = true;
        Mouse.Capture(null);

        var selected = host.ItemViews()
            .Where(x =>
                x.IsVisible &&
                x.IsHitTestVisible &&
                x.Opacity > 0 &&
                rect.IntersectsWith(Host.ScreenRect(x)))
            .Select(x => Host.Item(x.DataContext))
            .OfType<IItem>()
            .Where(x => !display.Layout.IsHidden(x.Layer))
            .Distinct<IItem>(ReferenceEqualityComparer.Instance)
            .ToArray();

        host.Timeline.SelectItems(selected);
        Marquees++;
        log("marquee_up selected=" + selected.Length);
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (disposed || anchor is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        BubbleMoves++;
        var p = Mouse.GetPosition(host.Source);

        if (!dragging)
        {
            if (!OverThreshold(p))
                return;

            // This is a bubbling handler: native TimelineItemView drag processing for
            // this MouseMove has already run. From now until MouseUp, freeze direct
            // geometry writes so native Layer/Top updates cannot cause a refresh loop.
            dragging = true;
            log($"gesture_transition_after_native_move anchor=L{anchor.Layer}:F{anchor.Frame} p={p}");
            try
            {
                display.BeginGesture(group.Keys);
            }
            catch (Exception ex)
            {
                log("gesture_begin_failure=" + ex);
                throw;
            }
        }

        var layout = display.Layout;
        var height = display.Height;

        var desired = layout.DisplayYToLogical(
            layout.VisualRowOfLogical(originalLayer) * height +
            p.Y - down.Y +
            height / 2.0,
            height);

        var delta = desired - originalLayer;

        // Preserve group shape. Reject the whole correction rather than clamping
        // members independently.
        if (group.Values.Any(layer => layer + delta < 0 || layer + delta > layout.MaxLayer))
        {
            log($"move_rejected desired={desired} delta={delta}");
            display.UpdateGestureVisuals();
            return;
        }

        var before = string.Join("|", group.Keys.Select(x => $"L{x.Layer}:F{x.Frame}"));
        var targets = new Dictionary<IItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var (item, layer) in group)
            targets[item] = layer + delta;

        // Put the folded visual compensation in place BEFORE changing Layer.
        // If the host immediately moves Top to the native logical row, the selected
        // view does not flash/jump outside the visible folded row. The widened
        // FastCanvas virtualization lease keeps non-active selected views realized.
        display.PrepareGestureVisuals(targets);

        foreach (var (item, target) in targets)
        {
            if (item.Layer != target)
            {
                item.Layer = target;
                Corrections++;
            }
        }

        display.UpdateGestureVisuals();

        log(
            $"move desired={desired} before={before} " +
            $"after={string.Join("|", group.Keys.Select(x => $"L{x.Layer}:F{x.Frame}"))}");
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (anchor is not null)
            log($"up L{anchor.Layer} F{anchor.Frame} dragging={dragging}");

        anchor = null;
        group.Clear();

        if (dragging)
            display.EndGesture();

        dragging = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        host.Source.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down));
        host.Source.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewMove));
        host.Source.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewUp));
        host.Source.RemoveHandler(Mouse.MouseMoveEvent, new MouseEventHandler(Move));
        host.Source.RemoveHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(Up));

        if (marquee && ReferenceEquals(Mouse.Captured, host.Source))
            Mouse.Capture(null);

        marquee = false;
        anchor = null;
        group.Clear();

        if (dragging || display.GestureActive)
            display.EndGesture();

        dragging = false;
        log($"input_detached bubble_moves={BubbleMoves} corrections={Corrections}");
    }
}
