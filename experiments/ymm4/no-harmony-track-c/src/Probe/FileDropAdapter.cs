using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

// Integrates the already-proven no-Harmony file-drop route with the exact
// FolderLayout used by Track C. YMM4 still performs the real AddFileItem action.
// The adapter maps display coordinates to logical coordinates and immediately
// post-corrects newly materialized items on the VM collection event.
internal sealed class FileDropMapAdapter : IDisposable
{
    private readonly Host host;
    private readonly DirectDisplay display;
    private readonly TimelineViewModel vm;
    private readonly Action<string> log;
    private readonly INotifyPropertyChanged vmNotify;

    private int pendingLogicalLayer = -1;
    private int pendingDisplayRow = -1;
    private HashSet<IItem>? pendingBefore;
    private bool disposed;

    internal int DragEnterCount { get; private set; }
    internal int DragOverCount { get; private set; }
    internal int DropCount { get; private set; }
    internal int AddCommandExecutions { get; private set; }
    internal int PostCorrectedItems { get; private set; }
    internal int LastLogicalLayer { get; private set; } = -1;
    internal string LastExecutedTarget { get; private set; } = "";

    internal FileDropMapAdapter(Host host, DirectDisplay display, Action<string> log)
    {
        this.host = host;
        this.display = display;
        this.log = log;
        vm = (TimelineViewModel)host.Vm;

        vmNotify = vm as INotifyPropertyChanged
            ?? throw new InvalidOperationException("TimelineViewModel does not notify property changes");

        vmNotify.PropertyChanged += VmChanged;
        host.View.AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(DragEnter), true);
        host.View.AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(DragOver), true);
        host.View.AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(Drop), true);
    }

    private Point Map(DragEventArgs e)
    {
        var raw = e.GetPosition(host.Source);
        var mapped = display.Layout.MapDisplayPointToLogical(raw, display.Height);
        log($"filedrop_map raw={raw} mapped={mapped} visible={display.Layout.VisibleCsv}");
        return mapped;
    }

    private void SetCursor(Point mapped, bool rightAlso)
    {
        Host.SetReactive(host.Vm, "TimelineCursorPosition", mapped);
        if (rightAlso)
            Host.SetReactive(host.Vm, "TimelineCursorPositionWhenRightClick", mapped);
    }

    private void DragEnter(object sender, DragEventArgs e)
    {
        if (disposed || !display.IsOperational)
            return;

        DragEnterCount++;
        var mapped = Map(e);
        SetCursor(mapped, false);
        log("filedrop_enter effects=" + e.Effects);
        // Do not handle: YMM4 remains responsible for normal drag-enter semantics.
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        if (disposed || !display.IsOperational)
            return;

        DragOverCount++;
        var mapped = Map(e);
        SetCursor(mapped, false);
    }

    private void Drop(object sender, DragEventArgs e)
    {
        if (disposed || !display.IsOperational)
            return;

        DropCount++;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
        {
            log("filedrop_drop_without_files");
            return;
        }

        var raw = e.GetPosition(host.Source);
        var mapped = display.Layout.MapDisplayPointToLogical(raw, display.Height);
        log($"filedrop_map raw={raw} mapped={mapped} visible={display.Layout.VisibleCsv}");
        SetCursor(mapped, true);
        pendingDisplayRow = Math.Max(
            0,
            (int)Math.Floor(raw.Y / display.Height));
        pendingLogicalLayer = (int)Math.Floor(mapped.Y / display.Height);
        LastLogicalLayer = pendingLogicalLayer;
        pendingBefore = new HashSet<IItem>(vm.Items.Select(x => (IItem)x.Item), ReferenceEqualityComparer.Instance);

        ICommand? command = CommandSettings.Default[CommandType.AddFileItem];
        IInputElement? executedTarget = null;

        if (command is RoutedCommand routed)
        {
            foreach (var target in new IInputElement?[] { host.Source, Keyboard.FocusedElement, host.Window })
            {
                if (target is null || !routed.CanExecute(paths, target))
                    continue;

                e.Handled = true;
                routed.Execute(paths, target);
                executedTarget = target;
                AddCommandExecutions++;
                break;
            }
        }
        else if (command?.CanExecute(paths) == true)
        {
            e.Handled = true;
            command.Execute(paths);
            AddCommandExecutions++;
        }

        LastExecutedTarget = executedTarget?.GetType().Name ?? (AddCommandExecutions > 0 ? "<direct>" : "<none>");
        log($"filedrop_command executed={AddCommandExecutions} target={LastExecutedTarget} logical_layer={pendingLogicalLayer} files={paths.Length}");
        CorrectPendingNewItems("after_command");
    }

    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || pendingLogicalLayer < 0)
            return;

        CorrectPendingNewItems("vm_" + (e.PropertyName ?? "<null>"));
    }

    private static bool TimeRangesOverlap(IItem a, IItem b)
    {
        var aStart = (long)a.Frame;
        var bStart = (long)b.Frame;
        var aEnd = aStart + Math.Max(0, (long)a.Length);
        var bEnd = bStart + Math.Max(0, (long)b.Length);
        return aStart < bEnd && bStart < aEnd;
    }

    private bool LayerWouldOverlap(
        IItem item,
        int layer,
        HashSet<IItem> addedSet)
    {
        foreach (var other in host.Timeline.Items)
        {
            if (addedSet.Contains(other) || other.Layer != layer)
                continue;

            if (TimeRangesOverlap(item, other))
                return true;
        }

        return false;
    }

    private int ResolveFoldedDropLayer(
        IItem item,
        int requestedLayer,
        HashSet<IItem> addedSet)
    {
        var requestedRow =
            display.Layout.VisualRowOfLogical(requestedLayer);
        var rawHostLayer = item.Layer;

        // YMM4 evaluates the drop against uncompressed physical rows. Translate
        // any host row offset back into folded display-row space before deciding
        // the logical layer. A zero offset means the host simply used the raw
        // display row and had no knowledge of collisions on the logical target.
        var hostRowDelta =
            pendingDisplayRow < 0
                ? 0
                : rawHostLayer - pendingDisplayRow;

        var candidateRow = requestedRow + hostRowDelta;
        if (candidateRow >= 0
            && candidateRow < display.Layout.VisibleLayers.Count)
        {
            var candidate =
                display.Layout.DisplayRowToLogical(candidateRow);
            if (!LayerWouldOverlap(item, candidate, addedSet))
            {
                log(
                    $"filedrop_folded_host_map item={item.GetType().Name} " +
                    $"raw_host_layer={rawHostLayer} raw_drop_row={pendingDisplayRow} " +
                    $"requested_layer={requestedLayer} host_row_delta={hostRowDelta} " +
                    $"resolved_layer={candidate}");
                return candidate;
            }
        }

        // Native AddFileItem prefers the next lower row when the requested row
        // is occupied. Search visible folded rows in that same direction; if
        // the host supplied a row delta, preserve its direction.
        var direction = Math.Sign(hostRowDelta);
        if (direction == 0)
            direction = 1;

        for (var step = 1;
             step < display.Layout.VisibleLayers.Count;
             step++)
        {
            var row = requestedRow + direction * step;
            if (row < 0
                || row >= display.Layout.VisibleLayers.Count)
            {
                break;
            }

            var layer =
                display.Layout.DisplayRowToLogical(row);
            if (!LayerWouldOverlap(item, layer, addedSet))
            {
                log(
                    $"filedrop_collision_escape item={item.GetType().Name} " +
                    $"raw_host_layer={rawHostLayer} requested_layer={requestedLayer} " +
                    $"resolved_layer={layer} direction={direction} step={step}");
                return layer;
            }
        }

        // If the preferred direction has no room, try the opposite side before
        // falling back to the requested logical layer.
        direction = -direction;
        for (var step = 1;
             step < display.Layout.VisibleLayers.Count;
             step++)
        {
            var row = requestedRow + direction * step;
            if (row < 0
                || row >= display.Layout.VisibleLayers.Count)
            {
                break;
            }

            var layer =
                display.Layout.DisplayRowToLogical(row);
            if (!LayerWouldOverlap(item, layer, addedSet))
            {
                log(
                    $"filedrop_collision_escape_reverse item={item.GetType().Name} " +
                    $"raw_host_layer={rawHostLayer} requested_layer={requestedLayer} " +
                    $"resolved_layer={layer} direction={direction} step={step}");
                return layer;
            }
        }

        log(
            $"filedrop_collision_escape_unresolved item={item.GetType().Name} " +
            $"raw_host_layer={rawHostLayer} requested_layer={requestedLayer}");
        return requestedLayer;
    }

    private void CorrectPendingNewItems(string source)
    {
        if (disposed || pendingLogicalLayer < 0 || pendingBefore is null)
            return;

        var added = vm.Items
            .Select(x => x.Item)
            .Where(item => !pendingBefore.Contains(item))
            .ToArray();

        if (added.Length == 0)
            return;

        var addedSet = new HashSet<IItem>(
            ReferenceEqualityComparer.Instance);
        foreach (var item in added)
            addedSet.Add(item);

        foreach (var item in added)
        {
            var requestedLayer = pendingLogicalLayer;
            var targetLayer = ResolveFoldedDropLayer(
                item,
                requestedLayer,
                addedSet);

            if (targetLayer != requestedLayer)
            {
                log(
                    $"filedrop_preserve_native_collision source={source} " +
                    $"item={item.GetType().Name} raw_host_layer={item.Layer} " +
                    $"requested_layer={requestedLayer} resolved_layer={targetLayer} " +
                    $"frame={item.Frame} length={item.Length}");
            }

            if (item.Layer != targetLayer)
                item.Layer = targetLayer;

            PostCorrectedItems++;
            log($"filedrop_post_correct source={source} item={item.GetType().Name} layer={item.Layer} frame={item.Frame}");
        }

        pendingLogicalLayer = -1;
        pendingDisplayRow = -1;
        pendingBefore = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        pendingLogicalLayer = -1;
        pendingDisplayRow = -1;
        pendingBefore = null;
        vmNotify.PropertyChanged -= VmChanged;
        host.View.RemoveHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(DragEnter));
        host.View.RemoveHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(DragOver));
        host.View.RemoveHandler(DragDrop.PreviewDropEvent, new DragEventHandler(Drop));
        log($"filedrop_detached enter={DragEnterCount} over={DragOverCount} drop={DropCount} command={AddCommandExecutions} corrected={PostCorrectedItems}");
    }
}

internal sealed record IntegratedDropObservation(
    DragDropEffects Effect,
    string FilePath,
    IItem[] AddedItems);

internal static class IntegratedFileDropHarness
{
    private static class DropNative
    {
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
        internal const uint LeftDown = 2;
        internal const uint LeftUp = 4;
        internal const uint KeyUp = 2;
    }

    internal static async Task<IntegratedDropObservation> DropPng(
        Window mainWindow,
        Host host,
        Point targetScreen,
        string output,
        string label)
    {
        var png = Path.Combine(output, label + "-1x1.png");
        File.WriteAllBytes(
            png,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQ1sAAAAASUVORK5CYII="));

        var before = host.Timeline.Items.ToArray();

        var sourceBorder = new Border
        {
            Width = 64,
            Height = 64,
            Background = Brushes.Gray
        };

        var sourceWindow = new Window
        {
            Width = 80,
            Height = 80,
            Left = 20,
            Top = 20,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Content = sourceBorder
        };

        sourceWindow.Show();
        sourceWindow.Activate();
        await Task.Delay(250);

        var start = sourceBorder.PointToScreen(
            new Point(sourceBorder.ActualWidth / 2, sourceBorder.ActualHeight / 2));

        DropNative.SetCursorPos((int)start.X, (int)start.Y);
        await Task.Delay(100);
        DropNative.mouse_event(DropNative.LeftDown, 0, 0, 0, 0);
        await Task.Delay(80);

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { png });

        using var cancel = new CancellationTokenSource();
        var mover = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancel.Token);
                for (var i = 1; i <= 12; i++)
                {
                    DropNative.SetCursorPos(
                        (int)Math.Round(start.X + (targetScreen.X - start.X) * i / 12.0),
                        (int)Math.Round(start.Y + (targetScreen.Y - start.Y) * i / 12.0));
                    await Task.Delay(75, cancel.Token);
                }

                await Task.Delay(180, cancel.Token);
                DropNative.mouse_event(DropNative.LeftUp, 0, 0, 0, 0);
                await Task.Delay(1600, cancel.Token);
                DropNative.keybd_event(0x1B, 0, 0, 0);
                DropNative.keybd_event(0x1B, 0, DropNative.KeyUp, 0);
            }
            catch (OperationCanceledException)
            {
            }
        });

        DragDropEffects effect;
        try
        {
            effect = System.Windows.DragDrop.DoDragDrop(
                sourceBorder,
                data,
                DragDropEffects.Copy);
        }
        finally
        {
            cancel.Cancel();
            DropNative.mouse_event(DropNative.LeftUp, 0, 0, 0, 0);
            sourceWindow.Close();
            mainWindow.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
        }

        try { await mover; }
        catch (OperationCanceledException) { }

        await Task.Delay(1200);

        var added = host.Timeline.Items
            .Where(item => !before.Any(existing => ReferenceEquals(existing, item)))
            .ToArray();

        return new(effect, png, added);
    }

    internal static string? FilePathOf(IItem item)
    {
        try
        {
            return item.GetType()
                .GetProperty("FilePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(item) as string;
        }
        catch
        {
            return null;
        }
    }
}
