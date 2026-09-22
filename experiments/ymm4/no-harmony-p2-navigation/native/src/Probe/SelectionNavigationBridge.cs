using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyNavigation;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// P2 candidate: observe real Timeline selection, never replace a host command.
// This boundary does not claim to intercept every ScrollToItem/keyboard route.
internal sealed class SelectionNavigationBridge : IDisposable
{
    private readonly Host host;
    private readonly DirectDisplay display;
    private readonly Func<IReadOnlyList<FolderRange>> readCollapsed;
    private readonly Action<IReadOnlyList<FolderRange>> writeCollapsed;
    private readonly Action<string> log;
    private readonly INotifyPropertyChanged changes;
    private DispatcherOperation? pending;
    private long revision;
    private bool disposed;

    internal int SelectionSignals { get; private set; }
    internal int Applied { get; private set; }
    internal Exception? Failure { get; private set; }

    internal SelectionNavigationBridge(Host host, DirectDisplay display,
        Func<IReadOnlyList<FolderRange>> readCollapsed,
        Action<IReadOnlyList<FolderRange>> writeCollapsed, Action<string> log)
    {
        this.host = host;
        this.display = display;
        this.readCollapsed = readCollapsed;
        this.writeCollapsed = writeCollapsed;
        this.log = log;
        changes = (INotifyPropertyChanged)host.Timeline;
        changes.PropertyChanged += Changed;
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != "SelectedItems"))
            return;
        SelectionSignals++;
        var selected = host.Timeline.SelectedItems;
        if (selected.Count == 1)
            NavigateTo(selected[0]);
        else
            CancelPending();
    }

    // Shared navigation entry for product-owned ScrollToItem-style routes.
    // It deliberately does not change native selection or CurrentFrame.
    internal void NavigateTo(IItem target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (disposed)
            throw new ObjectDisposedException(nameof(SelectionNavigationBridge));
        var ticket = ++revision;
        pending?.Abort();
        pending = null;
        Enqueue(() => Begin(ticket, target));
    }

    private void CancelPending()
    {
        revision++;
        pending?.Abort();
        pending = null;
    }

    private void Enqueue(Action action)
    {
        pending = host.View.Dispatcher.BeginInvoke(new Action(() =>
        {
            pending = null;
            if (disposed) return;
            try { action(); }
            catch (Exception ex)
            {
                Failure = ex;
                log("navigation_failure=" + ex);
                Dispose();
            }
        }), DispatcherPriority.ContextIdle);
    }

    private bool TryTarget(long ticket, IItem expected)
    {
        if (disposed || ticket != revision || display.GestureActive ||
            Mouse.Captured is not null || Mouse.LeftButton != MouseButtonState.Released)
            return false;
        if (!host.Timeline.Items.Any(x => ReferenceEquals(x, expected))) return false;
        return expected.Layer >= 0 && expected.Layer <= display.Layout.MaxLayer;
    }

    private void Begin(long ticket, IItem target)
    {
        display.ThrowIfFailed();
        if (!TryTarget(ticket, target)) return;
        var layer = target.Layer;
        var current = readCollapsed();
        var next = HiddenDestinationPolicy.RemainingCollapsed(current, layer, display.Layout.MaxLayer);
        if (!current.SequenceEqual(next))
        {
            // Only view state changes. Range identity/structure and the native
            // item/selection/playhead/history are not rewritten by this bridge.
            writeCollapsed(next);
            display.SetSpans(next.Select(x => new CollapsedSpan(x.Start, x.End)).ToArray());
        }
        // DirectDisplay queues its settle first at the same dispatcher priority.
        // Recheck identity/revision after that settle before touching the viewport.
        Enqueue(() => Complete(ticket, target, layer));
    }

    private void Complete(long ticket, IItem expected, int layer)
    {
        display.ThrowIfFailed();
        if (!TryTarget(ticket, expected) || expected.Layer != layer)
            return;
        if (display.Layout.IsHidden(layer))
            throw new InvalidOperationException("Navigation target is still folded after layout settle.");
        var height = host.Scroll.ViewportHeight;
        if (!double.IsFinite(height) || height <= 0)
            throw new InvalidOperationException("No usable Timeline viewport.");
        var top = display.Layout.VisualRowOfLogical(layer) * (double)display.Height;
        var bottom = top + display.Height;
        var offset = host.Scroll.VerticalOffset;
        if (top < offset)
            host.Scroll.ScrollToVerticalOffset(top);
        else if (bottom > offset + height)
            host.Scroll.ScrollToVerticalOffset(bottom - height);
        Applied++;
        log($"navigation_applied layer={layer} row={display.Layout.VisualRowOfLogical(layer)}");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelPending();
        changes.PropertyChanged -= Changed;
    }
}
