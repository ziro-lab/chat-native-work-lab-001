using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// Track C candidate. Idle/folded state uses the proven Track B direct geometry.
// During a native drag, direct geometry writes and FastCanvas refreshes are frozen.
// The native drag owns Frame/history; InputMapAdapter post-corrects Layer and this
// class only applies RenderTransform to the already-realized selected views.
// MouseUp clears transforms and schedules one normal folded-layout settle.
internal sealed class DirectDisplay : IDisposable
{
    private sealed record Slot(object Target, Func<int> Layer, bool Item, double HeightRatio, PropertyInfo Top, PropertyInfo Height);

    private readonly Host host;
    private readonly TimelineViewModel vm;
    private readonly Action<string> log;
    private readonly Dictionary<object, Slot> slots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyPropertyChanged> watched = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyPropertyChanged> permanentWatched = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyCollectionChanged> collections = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IItem> gestureItems = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FrameworkElement> refreshTargets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FrameworkElement, object> gestureTransforms = [];
    private readonly object oldMaxHeight;
    private Rect? gestureViewportOriginal;
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private CollapsedSpan[] spans = [];
    private bool applying, queued, disposed, gesture;
    private long budgetStart;
    private int windowApplications;

    internal int Applications { get; private set; }
    internal int Mutations { get; private set; }
    internal int CanvasRefreshes { get; private set; }
    internal int Reentries { get; private set; }
    internal int GestureSamples { get; private set; }
    internal int MissingGestureViews { get; private set; }
    internal int DeferredApplies { get; private set; }
    internal int RenderAudits { get; private set; }
    internal int PendingRenderAudits { get; private set; }
    internal Exception? Failure { get; private set; }
    internal string Phase { get; set; } = "attach";
    internal int Height => SettingsBase<YMMSettings>.Default.LayerHeight;
    internal FolderLayout Layout { get; private set; } = FolderLayout.Create(32, []);
    internal int SubscriptionCount => watched.Count + collections.Count;
    internal double ExpectedExtent { get; private set; }
    internal bool GestureActive => gesture;
    internal event EventHandler? Applied;

    internal DirectDisplay(Host host, Action<string> log)
    {
        this.host = host;
        this.log = log;
        vm = (TimelineViewModel)host.Vm;
        oldMaxHeight = host.Source.ReadLocalValue(FrameworkElement.MaxHeightProperty);
        WatchPermanent(vm);
        WatchPermanent(vm.Viewport);
        WatchPermanent(SettingsBase<YMMSettings>.Default);
        WatchCollection(vm.Items);
        WatchCollection(vm.LayerLabels);
        WatchCollection(vm.LayerLines);
        RefreshSlots();
    }

    internal void RegisterRefreshTarget(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.GetType().Name != "FastCanvasItemsControl")
            throw new ArgumentException(
                "Refresh target must be a FastCanvasItemsControl.",
                nameof(target));

        refreshTargets.Add(target);
    }

    private void WatchPermanent(object value)
    {
        if (value is INotifyPropertyChanged notify)
        {
            permanentWatched.Add(notify);
            Watch(notify);
        }
    }

    private void Watch(object value)
    {
        if (value is INotifyPropertyChanged notify && watched.Add(notify))
            notify.PropertyChanged += Changed;
    }

    private void WatchCollection(object value)
    {
        if (value is INotifyCollectionChanged notify && collections.Add(notify))
            notify.CollectionChanged += CollectionChanged;
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!applying && !gesture)
            Queue();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!applying && !gesture)
            Queue();
    }

    internal void SetSpans(params CollapsedSpan[] next)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(DirectDisplay));
        if (gesture)
            throw new InvalidOperationException("Folder change during gesture");
        spans = next.ToArray();
        Queue();
    }

    internal void BeginGesture(IEnumerable<IItem> items)
    {
        ThrowIfFailed();
        if (disposed || gesture)
            throw new InvalidOperationException("Invalid gesture lifecycle");

        gesture = true;
        gestureItems.Clear();
        foreach (var item in items)
            gestureItems.Add(item);

        if (gestureItems.Count == 0)
        {
            gesture = false;
            throw new InvalidOperationException("Empty gesture selection");
        }

        LeaseGestureViewport();
        log("gesture_begin_frozen count=" + gestureItems.Count + " viewport=" + vm.Viewport.Value);
    }

    private void LeaseGestureViewport()
    {
        var current = vm.Viewport.Value;
        if (gestureViewportOriginal is not null)
            throw new InvalidOperationException("Viewport lease already active");

        gestureViewportOriginal = current;

        var displayBottomRow = Math.Max(
            0,
            (int)Math.Floor(Math.Max(current.Y, current.Bottom - 1) / Height));

        displayBottomRow = Math.Min(
            displayBottomRow,
            Math.Max(0, Layout.VisibleLayers.Count - 1));

        var logicalVisibleBottom =
            (Layout.DisplayRowToLogical(displayBottomRow) + 2) * (double)Height;

        var selectedNativeBottom =
            (gestureItems.Max(x => x.Layer) + 4) * (double)Height;

        var expandedBottom = Math.Max(
            current.Bottom,
            Math.Max(logicalVisibleBottom, selectedNativeBottom));

        var expanded = new Rect(
            current.X,
            current.Y,
            current.Width,
            Math.Max(current.Height, expandedBottom - current.Y));

        vm.Viewport.Value = expanded;
        log($"gesture_viewport_lease old={current} expanded={expanded}");
    }

    private void RestoreGestureViewport()
    {
        if (gestureViewportOriginal is not { } original)
            return;

        vm.Viewport.Value = original;
        gestureViewportOriginal = null;
        log("gesture_viewport_restored=" + original);
    }

    private bool ApplyGestureVisual(FrameworkElement view, IItem item, int logicalLayer, bool countVisibility, bool anticipateNativeTop = false)
    {
        if (!gestureTransforms.ContainsKey(view))
            gestureTransforms[view] = view.ReadLocalValue(UIElement.RenderTransformProperty);

        var previousTranslation = view.RenderTransform is TranslateTransform prior ? prior.Y : 0.0;
        var measuredBaseTop = view.TranslatePoint(new Point(), host.Source).Y - previousTranslation;
        // PrepareGestureVisuals runs immediately before the Layer setter in the
        // same UI input stack. Anticipate YMM4's native logical Top so the final
        // pre-render state already contains the correct folded translation.
        var baseTop = anticipateNativeTop ? logicalLayer * (double)Height : measuredBaseTop;
        var desiredTop = Layout.VisualRowOfLogical(logicalLayer) * (double)Height;
        var translation = desiredTop - baseTop;

        if (view.RenderTransform is not TranslateTransform current ||
            Math.Abs(current.X) > 0.001 ||
            Math.Abs(current.Y - translation) > 0.001)
        {
            view.SetCurrentValue(UIElement.RenderTransformProperty, new TranslateTransform(0, translation));
        }

        if (!countVisibility)
            return true;

        var box = Host.ScreenRect(view);
        var visible = box.Width > 2 && box.Height > 2 && Host.ScreenRect(host.Scroll).IntersectsWith(box);
        log($"gesture_visual_immediate item={item.Remark} layer={item.Layer} target_layer={logicalLayer} local_top={host.LocalTop(item):F2} desired_top={desiredTop:F2} translation={translation:F2} visible={visible}");
        // Do not treat same-stack TranslatePoint as rendered evidence. WPF applies
        // the final DP state at render/layout priority after this mouse route.
        return visible;
    }

    internal void PrepareGestureVisuals(IReadOnlyDictionary<IItem, int> targetLayers)
    {
        if (!gesture || disposed)
            return;

        ThrowIfFailed();

        foreach (var view in host.ItemViews().ToArray())
        {
            var item = Host.Item(view.DataContext);
            if (item is null || !gestureItems.Contains(item) || !targetLayers.TryGetValue(item, out var target))
                continue;

            ApplyGestureVisual(view, item, target, false, item.Layer != target);
        }
    }

    internal void UpdateGestureVisuals()
    {
        if (!gesture || disposed)
            return;

        ThrowIfFailed();

        var found = new HashSet<IItem>(ReferenceEqualityComparer.Instance);
        foreach (var view in host.ItemViews().ToArray())
        {
            var item = Host.Item(view.DataContext);
            if (item is null || !gestureItems.Contains(item))
                continue;

            found.Add(item);
            ApplyGestureVisual(view, item, item.Layer, true);
        }

        GestureSamples++;
        ScheduleRenderAudit(gestureItems.Select(x => (Item: x, Layer: x.Layer)).ToArray());
        if (GestureSamples > 2000)
            throw new InvalidOperationException("Gesture visual update budget exceeded");
    }

    private void ScheduleRenderAudit((IItem Item, int Layer)[] expected)
    {
        PendingRenderAudits++;
        host.View.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                RenderAudits++;
                foreach (var (item, layer) in expected)
                {
                    var view = host.ItemViews().FirstOrDefault(x => ReferenceEquals(Host.Item(x.DataContext), item));
                    if (view is null)
                    {
                        MissingGestureViews++;
                        log($"gesture_render_audit item={item.Remark} layer={layer} realized=False visible=False");
                        continue;
                    }

                    var expectedTop = Layout.VisualRowOfLogical(layer) * (double)Height;
                    var actualTop = view.TranslatePoint(new Point(), host.Source).Y;
                    var box = Host.ScreenRect(view);
                    var visible =
                        Math.Abs(actualTop - expectedTop) < 2.0 &&
                        box.Width > 2 &&
                        box.Height > 2 &&
                        Host.ScreenRect(host.Scroll).IntersectsWith(box);

                    log($"gesture_render_audit item={item.Remark} layer={layer} realized=True actual_top={actualTop:F2} expected_top={expectedTop:F2} visible={visible}");
                    if (!visible)
                        MissingGestureViews++;
                }
            }
            catch (Exception ex)
            {
                MissingGestureViews += Math.Max(1, expected.Length);
                log("gesture_render_audit_failure=" + ex);
            }
            finally
            {
                PendingRenderAudits--;
            }
        }), DispatcherPriority.Render);
    }

    internal void EndGesture()
    {
        if (!gesture)
            return;

        gesture = false;

        foreach (var (view, original) in gestureTransforms)
        {
            if (original == DependencyProperty.UnsetValue)
                view.ClearValue(UIElement.RenderTransformProperty);
            else
                view.SetValue(UIElement.RenderTransformProperty, original);
        }

        gestureTransforms.Clear();
        RestoreGestureViewport();
        gestureItems.Clear();
        log($"gesture_end samples={GestureSamples} render_audits={RenderAudits} pending_render_audits={PendingRenderAudits} missing={MissingGestureViews} deferred={DeferredApplies}");
        Queue();
    }

    private void Queue()
    {
        if (disposed || queued)
            return;

        queued = true;
        host.View.Dispatcher.BeginInvoke(new Action(() =>
        {
            queued = false;
            if (disposed)
                return;

            if (gesture)
            {
                DeferredApplies++;
                log("apply_deferred_during_gesture phase=" + Phase);
                return;
            }

            try
            {
                Apply();
                Applied?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Failure = ex;
                log("display_failure=" + ex);
                try { Dispose(); }
                catch (Exception restore) { log("restore_failure=" + restore); }
            }
        }), DispatcherPriority.ContextIdle);
    }

    internal void ThrowIfFailed()
    {
        if (Failure is not null)
            throw new InvalidOperationException("Display adapter failed", Failure);
    }

    private void AddSlot(object target, Func<int> layer, bool item)
    {
        Watch(target);
        if (slots.ContainsKey(target))
            return;

        var type = target.GetType();
        var top = type.GetProperty("Top", Host.Flags) ?? throw new MissingMemberException(type.FullName, "Top");
        var height = type.GetProperty("Height", Host.Flags) ?? throw new MissingMemberException(type.FullName, "Height");
        if (top.GetSetMethod(true) is null || height.GetSetMethod(true) is null)
            throw new InvalidOperationException("Geometry setter missing");

        var ratio = Convert.ToDouble(height.GetValue(target)) / Height;
        if (!double.IsFinite(ratio) || ratio < 0)
            throw new InvalidOperationException("Invalid native height");

        slots.Add(target, new Slot(target, layer, item, ratio, top, height));
        if (slots.Count > 2048)
            throw new InvalidOperationException("Fixture slot budget exceeded");
    }

    private void RefreshSlots()
    {
        var current = new HashSet<object>(
            ReferenceEqualityComparer.Instance);

        foreach (var item in vm.Items)
        {
            current.Add(item);
            AddSlot(item, () => item.Item.Layer, true);
        }

        AddRows(
            (IList)vm.LayerLabels,
            current);
        AddRows(
            (IList)vm.LayerLines,
            current);

        PruneSlots(current);
    }

    private void AddRows(
        IList rows,
        HashSet<object> current)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var layer = i;
            var target = rows[i]
                ?? throw new InvalidOperationException(
                    "Null row");

            current.Add(target);
            AddSlot(
                target,
                () => layer,
                false);
        }
    }

    private void PruneSlots(
        HashSet<object> current)
    {
        foreach (var target in slots.Keys
            .Where(target => !current.Contains(target))
            .ToArray())
        {
            var slot = slots[target];

            // A virtualized/replaced VM can be reused by the host later.
            // Restore native logical geometry before releasing our reference so
            // a reappearing VM is never treated as a 6px folded native item.
            var layer = slot.Layer();
            var nativeTop = layer * (double)Height;
            var nativeHeight =
                slot.HeightRatio * Height;

            if (Math.Abs(
                    Convert.ToDouble(
                        slot.Top.GetValue(slot.Target))
                    - nativeTop)
                >= 0.001)
            {
                slot.Top.SetValue(
                    slot.Target,
                    nativeTop);
                Mutations++;
            }

            if (Math.Abs(
                    Convert.ToDouble(
                        slot.Height.GetValue(slot.Target))
                    - nativeHeight)
                >= 0.001)
            {
                slot.Height.SetValue(
                    slot.Target,
                    nativeHeight);
                Mutations++;
            }

            slots.Remove(target);

            if (target
                    is INotifyPropertyChanged notify
                && !permanentWatched.Contains(notify)
                && watched.Remove(notify))
            {
                notify.PropertyChanged -= Changed;
            }
        }
    }

    private void Apply()
    {
        if (gesture)
            throw new InvalidOperationException("Direct layout apply during gesture");

        if (applying)
        {
            Reentries++;
            throw new InvalidOperationException("Direct layout reentrancy");
        }

        var now = clock.ElapsedMilliseconds;
        if (now - budgetStart >= 1000)
        {
            budgetStart = now;
            windowApplications = 0;
        }

        if (++windowApplications > 40 || ++Applications > 1000)
            throw new InvalidOperationException("Direct layout update budget exceeded");

        applying = true;
        try
        {
            RefreshSlots();
            var count = vm.LayerLabels.Count;
            if (count != vm.LayerLines.Count || count == 0)
                throw new InvalidOperationException("Row collections inconsistent");

            var maximum = Math.Max(count, Math.Max(host.Timeline.MaxLayer, spans.Length == 0 ? 0 : spans.Max(x => x.End))) + 8;
            if (maximum > 1024)
                throw new InvalidOperationException("Fixture layer budget exceeded");

            Layout = FolderLayout.Create(maximum, spans);
            ExpectedExtent = Enumerable.Range(0, count).Count(x => !Layout.IsHidden(x)) * (double)Height;

            var before = Mutations;
            foreach (var item in vm.Items)
                ApplySlot(slots[item]);
            foreach (var row in vm.LayerLabels)
                ApplySlot(slots[row]);
            foreach (var row in vm.LayerLines)
                ApplySlot(slots[row]);

            if (Math.Abs(host.Source.MaxHeight - ExpectedExtent) > 0.01)
            {
                log($"maxheight_before phase={Phase} old={host.Source.MaxHeight} target={ExpectedExtent} viewport={vm.Viewport.Value}");
                host.Source.SetCurrentValue(FrameworkElement.MaxHeightProperty, ExpectedExtent);
                Mutations++;
                log("maxheight_after");
            }

            if (Mutations != before)
                RefreshCanvases();

            log($"apply_complete phase={Phase} application={Applications} writes={Mutations - before} rows={count} extent={ExpectedExtent}");
        }
        finally
        {
            applying = false;
        }
    }

    private void ApplySlot(Slot slot)
    {
        var layer = slot.Layer();
        var hidden = Layout.IsHidden(layer);
        Write(slot, slot.Top, hidden ? -100000.0 - layer * Height : Layout.VisualRowOfLogical(layer) * (double)Height);
        Write(slot, slot.Height, hidden ? (slot.Item ? 6.0 : 0.0) : slot.HeightRatio * Height);
    }

    private void Write(Slot slot, PropertyInfo property, double desired)
    {
        var current = Convert.ToDouble(property.GetValue(slot.Target));
        if (Math.Abs(current - desired) < 0.001)
            return;

        log($"write_before phase={Phase} type={slot.Target.GetType().Name} layer={slot.Layer()} property={property.Name} old_top={slot.Top.GetValue(slot.Target)} old_height={slot.Height.GetValue(slot.Target)} desired={desired} viewport={vm.Viewport.Value} applying={applying}");
        property.SetValue(slot.Target, desired);
        Mutations++;
        log("write_after " + property.Name);
    }

    private void RefreshCanvases()
    {
        var canvases = new HashSet<FrameworkElement>(
            ReferenceEqualityComparer.Instance);

        foreach (var canvas in Host.Elements(host.View)
            .Where(x => x.GetType().Name == "FastCanvasItemsControl"))
        {
            canvases.Add(canvas);
        }

        foreach (var target in refreshTargets)
            canvases.Add(target);

        foreach (var canvas in canvases)
        {
            var update = canvas.GetType().GetMethod("UpdateAll", Host.Flags, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException("FastCanvasItemsControl.UpdateAll");
            log("canvas_before phase=" + Phase + " target=" + canvas.GetType().Name);
            update.Invoke(canvas, null);
            canvas.InvalidateMeasure();
            canvas.InvalidateArrange();
            canvas.InvalidateVisual();
            CanvasRefreshes++;
            log("canvas_after");
        }
    }

    internal bool GeometryMatches()
    {
        if (gesture)
            return false;

        bool Valid(Slot slot)
        {
            var layer = slot.Layer();
            var hidden = Layout.IsHidden(layer);
            var top = hidden ? -100000.0 - layer * Height : Layout.VisualRowOfLogical(layer) * (double)Height;
            var height = hidden ? (slot.Item ? 6.0 : 0.0) : slot.HeightRatio * Height;
            return Math.Abs(Convert.ToDouble(slot.Top.GetValue(slot.Target)) - top) < 0.01
                && Math.Abs(Convert.ToDouble(slot.Height.GetValue(slot.Target)) - height) < 0.01;
        }

        return vm.Items.All(x => slots.TryGetValue(x, out var slot) && Valid(slot))
            && vm.LayerLabels.All(x => slots.TryGetValue(x, out var slot) && Valid(slot))
            && vm.LayerLines.All(x => slots.TryGetValue(x, out var slot) && Valid(slot));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        EndGesture();
        disposed = true;

        foreach (var notify in watched)
            notify.PropertyChanged -= Changed;
        foreach (var notify in collections)
            notify.CollectionChanged -= CollectionChanged;

        watched.Clear();
        permanentWatched.Clear();
        collections.Clear();
        Phase = "restore";

        foreach (var slot in slots.Values)
        {
            Write(slot, slot.Top, slot.Layer() * (double)Height);
            Write(slot, slot.Height, slot.HeightRatio * Height);
        }

        if (oldMaxHeight == DependencyProperty.UnsetValue)
            host.Source.ClearValue(FrameworkElement.MaxHeightProperty);
        else
            host.Source.SetValue(FrameworkElement.MaxHeightProperty, oldMaxHeight);

        RefreshCanvases();
        slots.Clear();
        refreshTargets.Clear();

        log($"display_detached applications={Applications} writes={Mutations} canvas_refreshes={CanvasRefreshes} subscriptions={SubscriptionCount} gesture_samples={GestureSamples} render_audits={RenderAudits} pending_render_audits={PendingRenderAudits} missing_gesture_views={MissingGestureViews} deferred={DeferredApplies}");
    }
}
