using System.Collections;
using System.Collections.Specialized;
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
    private readonly INotifyCollectionChanged itemsNotify;

    private int pendingLogicalLayer = -1;
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

        itemsNotify = vm.Items as INotifyCollectionChanged
            ?? throw new InvalidOperationException("TimelineViewModel.Items is not observable");

        itemsNotify.CollectionChanged += ItemsChanged;
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
        if (disposed)
            return;

        DragEnterCount++;
        var mapped = Map(e);
        SetCursor(mapped, false);
        log("filedrop_enter effects=" + e.Effects);
        // Do not handle: YMM4 remains responsible for normal drag-enter semantics.
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        if (disposed)
            return;

        DragOverCount++;
        var mapped = Map(e);
        SetCursor(mapped, false);
    }

    private void Drop(object sender, DragEventArgs e)
    {
        if (disposed)
            return;

        DropCount++;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
        {
            log("filedrop_drop_without_files");
            return;
        }

        var mapped = Map(e);
        SetCursor(mapped, true);
        pendingLogicalLayer = (int)Math.Floor(mapped.Y / display.Height);
        LastLogicalLayer = pendingLogicalLayer;

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

        // If no collection event happened synchronously, keep the pending layer for
        // a later asynchronous collection event. It is cleared by ItemsChanged.
    }

    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (disposed || pendingLogicalLayer < 0 || e.NewItems is null)
            return;

        var corrected = 0;
        foreach (var value in e.NewItems.Cast<object>())
        {
            var item = Host.Item(value);
            if (item is null)
                continue;

            if (item.Layer != pendingLogicalLayer)
                item.Layer = pendingLogicalLayer;

            corrected++;
            PostCorrectedItems++;
            log($"filedrop_post_correct item={item.GetType().Name} layer={item.Layer} frame={item.Frame}");
        }

        if (corrected > 0)
            pendingLogicalLayer = -1;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        pendingLogicalLayer = -1;
        itemsNotify.CollectionChanged -= ItemsChanged;
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
