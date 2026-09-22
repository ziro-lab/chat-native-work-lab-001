using System.Windows;
using System.Windows.Input;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyState;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Product-side adapter for the frozen P1 same-transaction structural policy.
/// S2 upgrades the history payload from Core-only FolderDocument snapshots to
/// the single FolderSessionDocument so visual metadata is restored together.
/// </summary>
internal sealed class StructuralFolderBridge : IDisposable
{
    private readonly Window root;
    private readonly Timeline timeline;
    private readonly UndoRedoManager undo;
    private readonly FolderStateStore state;
    private readonly Action<string> log;
    private readonly ExecutedRoutedEventHandler previewHandler;
    private CompositeOverrideLease? compositeOverride;
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

    internal CompositeOverrideLease PrepareCompositeOverride(
        CommandType type,
        int layer,
        FolderSessionDocument before,
        FolderSessionDocument after)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(StructuralFolderBridge));
        if (compositeOverride is not null)
            throw new InvalidOperationException(
                "A structural composite override is already active.");

        var lease = new CompositeOverrideLease(
            this,
            type,
            layer,
            FolderSessionDocumentRules.NormalizeAndValidate(before),
            FolderSessionDocumentRules.NormalizeAndValidate(after));
        compositeOverride = lease;
        return lease;
    }

    private void ReleaseCompositeOverride(CompositeOverrideLease lease)
    {
        if (ReferenceEquals(compositeOverride, lease))
            compositeOverride = null;
    }

    private void ApplyPendingState(
        FolderSessionDocument before,
        FolderSessionDocument after,
        string reason)
    {
        if (FolderSessionDocumentCodec.Save(before)
            == FolderSessionDocumentCodec.Save(after))
        {
            log("structural_noop reason=" + reason);
            return;
        }

        state.ReplaceState(after);
        undo.AddCommand(new UndoRedoActionCommand(
            () =>
            {
                UndoCallbacks++;
                state.ReplaceState(before);
            },
            () =>
            {
                RedoCallbacks++;
                state.ReplaceState(after);
            }));

        PendingEdits++;
        log(
            $"structural_pending reason={reason} " +
            $"timeline={timeline.ID:D}");
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
            || !TryStructuralEdit(
                e.Command,
                layer,
                out var type,
                out var edit)
            || edit is null)
            return;

        if (compositeOverride is { } composite
            && composite.Matches(type, layer))
        {
            var current =
                FolderSessionDocumentRules.NormalizeAndValidate(state.State);

            if (FolderSessionDocumentCodec.Save(current)
                != FolderSessionDocumentCodec.Save(composite.Before))
            {
                throw new InvalidOperationException(
                    "Folder session state changed while a composite structural command was pending.");
            }

            ApplyPendingState(
                composite.Before,
                composite.After,
                $"composite:{type}:L{layer}");
            composite.MarkApplied();
            return;
        }

        var key = timeline.ID.ToString("D");
        var beforeState =
            FolderSessionDocumentRules.NormalizeAndValidate(state.State);
        var timelineState = FolderDocumentRules.FindTimeline(
            beforeState.Core,
            key);

        if (timelineState is null || timelineState.Folders.Count == 0)
            return;

        var beforeFolders = timelineState.Folders.ToArray();
        var beforeById = beforeFolders.ToDictionary(x => x.Id);

        var plan = FolderRangeTracker.Apply(
            beforeFolders.Select(
                x => new FolderRange(x.Id, x.Start, x.End)),
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

        var afterCore = FolderDocumentRules.ReplaceTimeline(
            beforeState.Core,
            key,
            nextFolders);

        var afterState =
            FolderSessionDocumentRules.ReplaceCoreAfterStructuralEdit(
                beforeState,
                afterCore,
                key,
                plan);

        ApplyPendingState(
            beforeState,
            afterState,
            $"standard:{type}:L{layer}:folders={beforeFolders.Length}->{nextFolders.Length}");

        // Frozen P1 contract: YMM4 owns Record().
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        root.RemoveHandler(
            CommandManager.PreviewExecutedEvent,
            previewHandler);

        compositeOverride = null;
        log("structural_bridge_detached");
    }

    internal sealed class CompositeOverrideLease : IDisposable
    {
        private readonly StructuralFolderBridge owner;
        private bool disposed;

        internal CompositeOverrideLease(
            StructuralFolderBridge owner,
            CommandType type,
            int layer,
            FolderSessionDocument before,
            FolderSessionDocument after)
        {
            this.owner = owner;
            Type = type;
            Layer = layer;
            Before = before;
            After = after;
        }

        internal CommandType Type { get; }
        internal int Layer { get; }
        internal FolderSessionDocument Before { get; }
        internal FolderSessionDocument After { get; }
        internal bool Applied { get; private set; }

        internal bool Matches(CommandType type, int layer) =>
            !disposed && Type == type && Layer == layer;

        internal void MarkApplied() => Applied = true;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            owner.ReleaseCompositeOverride(this);
        }
    }
}
