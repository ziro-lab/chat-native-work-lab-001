using System.Windows;
using System.Windows.Input;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Product-side adapter for the frozen P1 same-transaction structural policy.
///
/// Standard YMM4 layer commands remain host-owned. Before the host executes one
/// of the proven structural commands, the current folder ranges are transformed
/// with FolderRangeTracker and one folder-state undo callback is appended to the
/// host's pending history unit. This adapter deliberately does not call Record().
/// </summary>
internal sealed class StructuralFolderBridge : IDisposable
{
    private readonly Window root;
    private readonly Timeline timeline;
    private readonly UndoRedoManager undo;
    private readonly FolderStateStore state;
    private readonly Action<string> log;
    private readonly ExecutedRoutedEventHandler previewHandler;
    private bool disposed;

    internal int PendingEdits { get; private set; }
    internal int UndoCallbacks { get; private set; }
    internal int RedoCallbacks { get; private set; }

    internal StructuralFolderBridge(
        Window root,
        Timeline timeline,
        UndoRedoManager undo,
        FolderStateStore state,
        Action<string> log)
    {
        this.root = root;
        this.timeline = timeline;
        this.undo = undo;
        this.state = state;
        this.log = log;

        previewHandler = OnPreviewExecuted;
        root.AddHandler(
            CommandManager.PreviewExecutedEvent,
            previewHandler,
            true);
    }

    private static bool TryStructuralEdit(
        ICommand command,
        int layer,
        out CommandType type,
        out StructuralEdit? edit)
    {
        foreach (var candidate in new[]
        {
            CommandType.AddLayer,
            CommandType.DeleteLayer,
            CommandType.MoveUpLayer,
            CommandType.MoveDownLayer
        })
        {
            var configured = CommandSettings.Default[candidate];
            if (configured is null || !ReferenceEquals(command, configured))
                continue;

            type = candidate;
            edit = candidate switch
            {
                CommandType.AddLayer =>
                    new InsertLayers(layer, 1),
                CommandType.DeleteLayer =>
                    new DeleteLayers(layer, 1),
                CommandType.MoveDownLayer =>
                    new SwapAdjacentLayers(layer),
                CommandType.MoveUpLayer when layer > 0 =>
                    new SwapAdjacentLayers(layer - 1),
                _ => null
            };

            return edit is not null;
        }

        type = default;
        edit = null;
        return false;
    }

    private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (disposed || state.IsRecoveryBlocked)
            return;

        if (e.Parameter is not int layer
            || !TryStructuralEdit(e.Command, layer, out var type, out var edit)
            || edit is null)
            return;

        var key = timeline.ID.ToString("D");
        var timelineState = FolderDocumentRules.FindTimeline(state.Document, key);
        if (timelineState is null || timelineState.Folders.Count == 0)
            return;

        var before = state.Document;
        var beforeFolders = timelineState.Folders.ToArray();
        var beforeById = beforeFolders.ToDictionary(x => x.Id);

        var plan = FolderRangeTracker.Apply(
            beforeFolders.Select(x => new FolderRange(x.Id, x.Start, x.End)),
            edit);

        var nextFolders = plan.Ranges
            .Select(range =>
            {
                var folder = beforeById[range.Id];
                return folder with
                {
                    Start = range.Start,
                    End = range.End
                };
            })
            .ToArray();

        var after = FolderDocumentRules.ReplaceTimeline(
            before,
            key,
            nextFolders);

        if (FolderDocumentCodec.Save(before) == FolderDocumentCodec.Save(after))
        {
            log($"structural_noop type={type} layer={layer} timeline={key}");
            return;
        }

        state.ReplaceDocument(after);

        undo.AddCommand(new UndoRedoActionCommand(
            () =>
            {
                UndoCallbacks++;
                state.ReplaceDocument(before);
            },
            () =>
            {
                RedoCallbacks++;
                state.ReplaceDocument(after);
            }));

        PendingEdits++;

        // Frozen P1 contract: the host's structural command owns Record().
        // Adding a second Record() here would split one user action into two
        // undo units.
        log(
            $"structural_pending type={type} layer={layer} " +
            $"folders_before={beforeFolders.Length} folders_after={nextFolders.Length} " +
            $"timeline={key}");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        root.RemoveHandler(
            CommandManager.PreviewExecutedEvent,
            previewHandler);

        log("structural_bridge_detached");
    }
}
