using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// Display-only: no input hooks and no Timeline item Frame/Layer writes.
internal sealed class DirectDisplay : IDisposable
{
    private sealed record Slot(object Target, Func<int> Layer, bool Item, double HeightRatio, PropertyInfo Top, PropertyInfo Height);
    private readonly Host host;
    private readonly TimelineViewModel vm;
    private readonly Action<string> log;
    private readonly Dictionary<object, Slot> slots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyPropertyChanged> watched = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyCollectionChanged> collections = new(ReferenceEqualityComparer.Instance);
    private readonly object oldMaxHeight;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private CollapsedSpan[] spans = [];
    private bool applying, queued, disposed;
    private long budgetStart;
    private int windowApplications;
    internal int Applications { get; private set; }
    internal int Mutations { get; private set; }
    internal int CanvasRefreshes { get; private set; }
    internal int Reentries { get; private set; }
    internal Exception? Failure { get; private set; }
    internal string Phase { get; set; } = "attach";
    internal int Height => SettingsBase<YMMSettings>.Default.LayerHeight;
    internal FolderLayout Layout { get; private set; } = FolderLayout.Create(32, []);
    internal int SubscriptionCount => watched.Count + collections.Count;
    internal double ExpectedExtent { get; private set; }
    internal DirectDisplay(Host host, Action<string> log)
    {
        this.host = host; this.log = log; vm = (TimelineViewModel)host.Vm;
        oldMaxHeight = host.Source.ReadLocalValue(FrameworkElement.MaxHeightProperty);
        Watch(vm); Watch(vm.Viewport); Watch(SettingsBase<YMMSettings>.Default);
        WatchCollection(vm.Items); WatchCollection(vm.LayerLabels); WatchCollection(vm.LayerLines);
        RefreshSlots();
    }
    private void Watch(object value)
    {
        if (value is INotifyPropertyChanged notify && watched.Add(notify)) notify.PropertyChanged += Changed;
    }
    private void WatchCollection(object value)
    {
        if (value is INotifyCollectionChanged notify && collections.Add(notify)) notify.CollectionChanged += CollectionChanged;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!applying) Queue();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!applying) Queue();
    }
    internal void SetSpans(params CollapsedSpan[] next)
    {
        if (disposed) throw new ObjectDisposedException(nameof(DirectDisplay));
        spans = next.ToArray(); Queue();
    }
    private void Queue()
    {
        if (disposed || queued) return;
        queued = true;
        host.View.Dispatcher.BeginInvoke(new Action(() =>
        {
            queued = false;
            if (disposed) return;
            try { Apply(); }
            catch (Exception ex)
            {
                Failure = ex; log("display_failure=" + ex);
                try { Dispose(); } catch (Exception restore) { log("restore_failure=" + restore); }
            }
        }), DispatcherPriority.ContextIdle);
    }
    internal void ThrowIfFailed()
    {
        if (Failure is not null) throw new InvalidOperationException("Display adapter failed", Failure);
    }
    private void AddSlot(object target, Func<int> layer, bool item)
    {
        Watch(target);
        if (slots.ContainsKey(target)) return;
        var type = target.GetType();
        var top = type.GetProperty("Top", Host.Flags) ?? throw new MissingMemberException(type.FullName, "Top");
        var height = type.GetProperty("Height", Host.Flags) ?? throw new MissingMemberException(type.FullName, "Height");
        if (top.GetSetMethod(true) is null || height.GetSetMethod(true) is null) throw new InvalidOperationException("Geometry setter missing");
        var ratio = Convert.ToDouble(height.GetValue(target)) / Height;
        if (!double.IsFinite(ratio) || ratio < 0) throw new InvalidOperationException("Invalid native height");
        slots.Add(target, new Slot(target, layer, item, ratio, top, height));
        if (slots.Count > 2048) throw new InvalidOperationException("Fixture slot budget exceeded");
    }
    private void RefreshSlots()
    {
        foreach (var item in vm.Items) AddSlot(item, () => item.Item.Layer, true);
        AddRows((IList)vm.LayerLabels); AddRows((IList)vm.LayerLines);
    }
    private void AddRows(IList rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var layer = i; AddSlot(rows[i] ?? throw new InvalidOperationException("Null row"), () => layer, false);
        }
    }
    private void Apply()
    {
        if (applying) { Reentries++; throw new InvalidOperationException("Direct layout reentrancy"); }
        var now = clock.ElapsedMilliseconds;
        if (now - budgetStart >= 1000) { budgetStart = now; windowApplications = 0; }
        if (++windowApplications > 40 || ++Applications > 1000) throw new InvalidOperationException("Direct layout update budget exceeded");
        applying = true;
        try
        {
            RefreshSlots();
            var count = vm.LayerLabels.Count;
            if (count != vm.LayerLines.Count || count == 0) throw new InvalidOperationException("Row collections inconsistent");
            var maximum = Math.Max(count, Math.Max(host.Timeline.MaxLayer, spans.Length == 0 ? 0 : spans.Max(x => x.End))) + 8;
            if (maximum > 1024) throw new InvalidOperationException("Fixture layer budget exceeded");
            Layout = FolderLayout.Create(maximum, spans);
            ExpectedExtent = Enumerable.Range(0, count).Count(x => !Layout.IsHidden(x)) * (double)Height;
            var before = Mutations;
            foreach (var item in vm.Items) ApplySlot(slots[item]);
            foreach (var row in vm.LayerLabels) ApplySlot(slots[row]);
            foreach (var row in vm.LayerLines) ApplySlot(slots[row]);
            if (Math.Abs(host.Source.MaxHeight - ExpectedExtent) > 0.01)
            {
                log($"maxheight_before phase={Phase} old={host.Source.MaxHeight} target={ExpectedExtent} viewport={vm.Viewport.Value}");
                host.Source.SetCurrentValue(FrameworkElement.MaxHeightProperty, ExpectedExtent);
                log("maxheight_after"); Mutations++;
            }
            if (Mutations != before) RefreshCanvases();
            log($"apply_complete phase={Phase} application={Applications} writes={Mutations - before} rows={count} extent={ExpectedExtent}");
        }
        finally { applying = false; }
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
        if (Math.Abs(current - desired) < 0.001) return;
        log($"write_before phase={Phase} type={slot.Target.GetType().Name} layer={slot.Layer()} property={property.Name} old_top={slot.Top.GetValue(slot.Target)} old_height={slot.Height.GetValue(slot.Target)} desired={desired} viewport={vm.Viewport.Value} applying={applying}");
        property.SetValue(slot.Target, desired);
        Mutations++; log("write_after " + property.Name);
    }
    private void RefreshCanvases()
    {
        foreach (var canvas in Host.Elements(host.View).Where(x => x.GetType().Name == "FastCanvasItemsControl").ToArray())
        {
            var update = canvas.GetType().GetMethod("UpdateAll", Host.Flags, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException("FastCanvasItemsControl.UpdateAll");
            log("canvas_before phase=" + Phase);
            update.Invoke(canvas, null); CanvasRefreshes++;
            log("canvas_after");
        }
    }
    internal bool GeometryMatches()
    {
        bool Valid(Slot slot)
        {
            var layer = slot.Layer(); var hidden = Layout.IsHidden(layer);
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
        if (disposed) return;
        disposed = true;
        foreach (var notify in watched) notify.PropertyChanged -= Changed;
        foreach (var notify in collections) notify.CollectionChanged -= CollectionChanged;
        watched.Clear(); collections.Clear();
        Phase = "restore";
        foreach (var slot in slots.Values)
        {
            Write(slot, slot.Top, slot.Layer() * (double)Height);
            Write(slot, slot.Height, slot.HeightRatio * Height);
        }
        if (oldMaxHeight == DependencyProperty.UnsetValue) host.Source.ClearValue(FrameworkElement.MaxHeightProperty);
        else host.Source.SetValue(FrameworkElement.MaxHeightProperty, oldMaxHeight);
        RefreshCanvases(); slots.Clear();
        log($"display_detached applications={Applications} writes={Mutations} canvas_refreshes={CanvasRefreshes} subscriptions={SubscriptionCount}");
    }
}
