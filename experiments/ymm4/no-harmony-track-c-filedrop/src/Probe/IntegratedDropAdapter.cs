using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed record IntegratedDropResult(
    string FilePath,
    int LogicalLayer,
    int AddedCount,
    int[] LayersBeforeCorrection,
    IItem[] AddedItems,
    bool CommandExecuted);

internal sealed class IntegratedDropAdapter : IDisposable
{
    private readonly Host host;
    private readonly DirectDisplay display;
    private readonly Action<string> log;
    private bool disposed;
    private TaskCompletionSource<IntegratedDropResult>? completion;
    private string? expectedPath;

    internal bool DragEnterSeen { get; private set; }
    internal bool DragOverSeen { get; private set; }
    internal bool DropSeen { get; private set; }
    internal int PostCorrections { get; private set; }

    internal IntegratedDropAdapter(Host host, DirectDisplay display, Action<string> log)
    {
        this.host = host;
        this.display = display;
        this.log = log;

        host.View.AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(OnEnter), true);
        host.View.AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(OnOver), true);
        host.View.AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(OnDrop), true);
    }

    internal Task<IntegratedDropResult> Expect(string path)
    {
        if (completion is not null && !completion.Task.IsCompleted)
            throw new InvalidOperationException("A drop is already pending.");

        expectedPath = Path.GetFullPath(path);
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DragEnterSeen = DragOverSeen = DropSeen = false;
        return completion.Task;
    }

    private Point Map(DragEventArgs e)
    {
        var raw = e.GetPosition(host.Source);
        var mapped = display.Layout.MapDisplayPointToLogical(raw, display.Height);
        log($"drop_map raw={raw} mapped={mapped} logical={(int)Math.Floor(mapped.Y / display.Height)}");
        return mapped;
    }

    private void OnEnter(object sender, DragEventArgs e)
    {
        if (disposed) return;
        DragEnterSeen = true;
        Host.SetReactive(host.Vm, "TimelineCursorPosition", Map(e));
    }

    private void OnOver(object sender, DragEventArgs e)
    {
        if (disposed) return;
        DragOverSeen = true;
        Host.SetReactive(host.Vm, "TimelineCursorPosition", Map(e));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (disposed) return;
        DropSeen = true;
        display.ThrowIfFailed();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
        {
            completion?.TrySetException(new InvalidOperationException("FileDrop payload missing."));
            return;
        }

        if (expectedPath is null ||
            !paths.Any(x => string.Equals(Path.GetFullPath(x), expectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            completion?.TrySetException(new InvalidOperationException("Unexpected drop path."));
            return;
        }

        var mapped = Map(e);
        var logicalLayer = (int)Math.Floor(mapped.Y / display.Height);
        Host.SetReactive(host.Vm, "TimelineCursorPosition", mapped);
        Host.SetReactive(host.Vm, "TimelineCursorPositionWhenRightClick", mapped);

        var before = new HashSet<IItem>(host.Timeline.Items, ReferenceEqualityComparer.Instance);
        var executed = ExecuteHostAdd(paths);
        e.Handled = executed;

        log($"drop_execute command={executed} logical={logicalLayer} before_count={before.Count}");

        if (!executed)
        {
            completion?.TrySetException(new InvalidOperationException("YMM4 AddFileItem command was not executable."));
            return;
        }

        host.View.Dispatcher.BeginInvoke(
            new Action(() => FinalizeDrop(before, logicalLayer, paths[0], executed)),
            DispatcherPriority.ContextIdle);
    }

    private bool ExecuteHostAdd(string[] paths)
    {
        ICommand? command = CommandSettings.Default[CommandType.AddFileItem];
        if (command is null)
            return false;

        if (command is RoutedCommand routed)
        {
            foreach (var target in new IInputElement?[] { host.Source, Keyboard.FocusedElement, host.Window })
            {
                if (target is null || !routed.CanExecute(paths, target))
                    continue;

                routed.Execute(paths, target);
                return true;
            }
            return false;
        }

        if (!command.CanExecute(paths))
            return false;

        command.Execute(paths);
        return true;
    }

    private void FinalizeDrop(HashSet<IItem> before, int logicalLayer, string filePath, bool executed)
    {
        try
        {
            display.ThrowIfFailed();

            var added = host.Timeline.Items.Where(x => !before.Contains(x)).ToArray();
            var beforeLayers = added.Select(x => x.Layer).ToArray();

            foreach (var item in added)
            {
                if (item.Layer == logicalLayer)
                    continue;

                item.Layer = logicalLayer;
                PostCorrections++;
            }

            log(
                $"drop_finalize added={added.Length} before_layers={string.Join(",", beforeLayers)} " +
                $"after_layers={string.Join(",", added.Select(x => x.Layer))} logical={logicalLayer}");

            completion?.TrySetResult(
                new IntegratedDropResult(
                    filePath,
                    logicalLayer,
                    added.Length,
                    beforeLayers,
                    added,
                    executed));
        }
        catch (Exception ex)
        {
            completion?.TrySetException(ex);
        }
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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        host.View.RemoveHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(OnEnter));
        host.View.RemoveHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(OnOver));
        host.View.RemoveHandler(DragDrop.PreviewDropEvent, new DragEventHandler(OnDrop));

        completion?.TrySetCanceled();
        completion = null;
        expectedPath = null;
        log("drop_adapter_detached");
    }
}
