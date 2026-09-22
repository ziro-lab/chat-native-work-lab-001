using System.Windows;
using System.Windows.Input;
using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
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
    private CompositeOverrideLease? compositeOverride;
    private bool disposed;

    internal int PendingEdits { get; private set; }
    internal int UndoCallbacks { get; private set; }
    internal int RedoCallbacks { get; private set; }
    internal int GroupRangeCorrections { get; private set; }

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
        FolderDocument before,
        FolderDocument after,
        Action? beforeHostMutation = null)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(StructuralFolderBridge));
        if (compositeOverride is not null)
            throw new InvalidOperationException("A structural composite override is already active.");

        var lease = new CompositeOverrideLease(
            this,
            type,
            layer,
            FolderDocumentRules.NormalizeAndValidate(before),
            FolderDocumentRules.NormalizeAndValidate(after),
            beforeHostMutation);
        compositeOverride = lease;
        return lease;
    }

    private void ReleaseCompositeOverride(CompositeOverrideLease lease)
    {
        if (ReferenceEquals(compositeOverride, lease))
            compositeOverride = null;
    }

    private void ApplyPendingState(
        FolderProductState before,
        FolderProductState after,
        string reason)
    {
        var normalizedBefore =
            FolderProductStateRules.NormalizeAndValidate(before);
        var normalizedAfter =
            FolderProductStateRules.NormalizeAndValidate(after);

        if (FolderProductStateCodec.Save(normalizedBefore)
            == FolderProductStateCodec.Save(normalizedAfter))
        {
            log("structural_noop reason=" + reason);
            return;
        }

        state.ReplaceProductState(normalizedAfter);
        undo.AddCommand(new UndoRedoActionCommand(
            () =>
            {
                UndoCallbacks++;
                state.ReplaceProductState(normalizedBefore);
            },
            () =>
            {
                RedoCallbacks++;
                state.ReplaceProductState(normalizedAfter);
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
            || !TryStructuralEdit(e.Command, layer, out var type, out var edit)
            || edit is null)
            return;

        if (compositeOverride is { } composite
            && composite.Matches(type, layer))
        {
            var current = FolderDocumentRules.NormalizeAndValidate(state.Document);
            if (FolderDocumentCodec.Save(current) != FolderDocumentCodec.Save(composite.Before))
            {
                throw new InvalidOperationException(
                    "Folder state changed while a composite structural command was pending.");
            }

            var compositeBeforeProduct = state.ProductState;
            var compositeAfterProduct = FolderProductStateRules.ReplaceCore(
                compositeBeforeProduct,
                composite.After);

            var compositeKey = timeline.ID.ToString("D");
            var currentTimeline = FolderDocumentRules.FindTimeline(
                composite.Before,
                compositeKey);
            var remapPlan = FolderRangeTracker.Apply(
                currentTimeline?.Folders.Select(
                    x => new FolderRange(x.Id, x.Start, x.End))
                    ?? [],
                edit);

            compositeAfterProduct = FolderProductStateRules.RemapRestoreLayers(
                compositeAfterProduct,
                compositeKey,
                remapPlan.MapLayer);

            ApplyPendingState(
                compositeBeforeProduct,
                compositeAfterProduct,
                $"composite:{type}:L{layer}");
            composite.ApplyBeforeHostMutation();
            composite.MarkApplied();
            return;
        }

        var key = timeline.ID.ToString("D");
        var timelineState = FolderDocumentRules.FindTimeline(state.Document, key);
        if (timelineState is null || timelineState.Folders.Count == 0)
            return;

        var beforeProduct = state.ProductState;
        var before = beforeProduct.Core;

        var groupItems = timeline.Items
            .OfType<GroupItem>()
            .ToArray();
        var groupSpans = groupItems
            .Select(group => new GroupSpan(
                group.Layer,
                group.GroupRange))
            .ToArray();
        var plannedGroupRanges =
            StructuralConvenienceRules.PlanStandardGroupRangesBeforeHost(
                before,
                key,
                edit,
                groupSpans);
        GroupRangeCorrections += HandsOnHostAccess.ApplyGroupRanges(
            groupItems,
            plannedGroupRanges);

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

        var afterProduct = FolderProductStateRules.ReplaceCore(
            beforeProduct,
            after);
        afterProduct = FolderProductStateRules.RemapRestoreLayers(
            afterProduct,
            key,
            plan.MapLayer);

        ApplyPendingState(
            beforeProduct,
            afterProduct,
            $"standard:{type}:L{layer}:folders={beforeFolders.Length}->{nextFolders.Length}");

        // Frozen P1 contract: the host's structural command owns Record().
        // Adding a second Record() here would split one user action into two
        // undo units.
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
            FolderDocument before,
            FolderDocument after,
            Action? beforeHostMutation)
        {
            this.owner = owner;
            Type = type;
            Layer = layer;
            Before = before;
            After = after;
            BeforeHostMutation = beforeHostMutation;
        }

        internal CommandType Type { get; }
        internal int Layer { get; }
        internal FolderDocument Before { get; }
        internal FolderDocument After { get; }
        private Action? BeforeHostMutation { get; }
        internal bool Applied { get; private set; }

        internal bool Matches(CommandType type, int layer) =>
            !disposed && Type == type && Layer == layer;

        internal void ApplyBeforeHostMutation()
        {
            BeforeHostMutation?.Invoke();
        }

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
