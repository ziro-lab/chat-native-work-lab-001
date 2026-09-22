using System.ComponentModel;
using System.Windows.Threading;
using Ymm4NoHarmonyProductState;
using YukkuriMovieMaker.Project;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Owns the host-visible layer state for folder Hidden.
///
/// Hidden reasons are derived from the union of hidden folder ranges. One
/// restore value is kept per logical layer. Internal writes are never
/// reinterpreted as user overrides. External eye changes are sampled after the
/// current dispatcher stack so native Undo/Redo and the plugin state callback
/// can settle first.
/// </summary>
internal sealed class FolderVisibilityCoordinator : IDisposable
{
    private readonly Timeline timeline;
    private readonly FolderStateStore state;
    private readonly Action<string> log;
    private readonly INotifyPropertyChanged settingsSignal;
    private readonly HashSet<int> pluginSuppressed = [];
    private DispatcherOperation? pendingCapture;
    private int internalWriteDepth;
    private bool disposed;

    internal FolderVisibilityCoordinator(
        Timeline timeline,
        FolderStateStore state,
        Action<string> log)
    {
        this.timeline = timeline;
        this.state = state;
        this.log = log;
        settingsSignal = timeline.LayerSettings;
        settingsSignal.PropertyChanged += OnLayerSettingsChanged;
        state.Changed += OnProductStateChanged;
        RebuildSuppressionOwnership();
    }

    private string TimelineKey => timeline.ID.ToString("D");

    internal FolderProductState ApplyHiddenChange(
        FolderProductState before,
        FolderProductState requestedAfter)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(FolderVisibilityCoordinator));

        var normalizedBefore =
            FolderProductStateRules.NormalizeAndValidate(before);
        var normalizedAfter =
            FolderProductStateRules.NormalizeAndValidate(requestedAfter);

        var beforeHidden =
            FolderProductStateRules.HiddenLayers(normalizedBefore, TimelineKey);
        var afterHidden =
            FolderProductStateRules.HiddenLayers(normalizedAfter, TimelineKey);
        var restore =
            FolderProductStateRules.RestoreMap(normalizedBefore, TimelineKey)
                .ToDictionary(x => x.Key, x => x.Value);

        internalWriteDepth++;
        try
        {
            foreach (var layer in afterHidden.Except(beforeHidden).OrderBy(x => x))
            {
                var current = timeline.LayerSettings.IsVisibles[layer];
                if (!restore.ContainsKey(layer))
                    restore[layer] = current;

                // This layer is now governed by a Hidden reason even when it
                // was already false before the plugin touched it.
                pluginSuppressed.Add(layer);

                if (current)
                    timeline.LayerSettings.IsVisibles[layer] = false;
            }

            foreach (var layer in beforeHidden.Except(afterHidden).OrderBy(x => x))
            {
                if (restore.TryGetValue(layer, out var original))
                {
                    // If the user explicitly showed a hidden layer, ownership
                    // was released and the current value wins. Otherwise put
                    // the exact pre-hide value back.
                    if (pluginSuppressed.Contains(layer)
                        && timeline.LayerSettings.IsVisibles[layer] != original)
                    {
                        timeline.LayerSettings.IsVisibles[layer] = original;
                    }

                    restore.Remove(layer);
                }

                pluginSuppressed.Remove(layer);
            }
        }
        finally
        {
            internalWriteDepth--;
        }

        var finalState = FolderProductStateRules.ReplaceRestoreMap(
            normalizedAfter,
            TimelineKey,
            restore);

        log(
            $"visibility_apply hidden_before={beforeHidden.Count} " +
            $"hidden_after={afterHidden.Count} restore={restore.Count}");

        return finalState;
    }

    private void OnLayerSettingsChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (disposed || internalWriteDepth > 0)
            return;

        pendingCapture?.Abort();
        pendingCapture = timeline.Dispatcher.BeginInvoke(
            new Action(CaptureExternalOverrides),
            DispatcherPriority.ContextIdle);
    }

    private void OnProductStateChanged(object? sender, EventArgs e)
    {
        if (disposed)
            return;

        // State callbacks may run during native Undo/Redo. Defer ownership
        // reconstruction until the host's visibility setters have settled.
        pendingCapture?.Abort();
        pendingCapture = timeline.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                pendingCapture = null;
                if (!disposed)
                    RebuildSuppressionOwnership();
            }),
            DispatcherPriority.ContextIdle);
    }

    private void RebuildSuppressionOwnership()
    {
        pluginSuppressed.Clear();

        var product = state.ProductState;
        var hidden = FolderProductStateRules.HiddenLayers(
            product,
            TimelineKey);
        var restore = FolderProductStateRules.RestoreMap(
            product,
            TimelineKey);

        foreach (var layer in hidden)
        {
            if (restore.ContainsKey(layer)
                && !timeline.LayerSettings.IsVisibles[layer])
            {
                pluginSuppressed.Add(layer);
            }
        }
    }

    private void CaptureExternalOverrides()
    {
        pendingCapture = null;
        if (disposed || internalWriteDepth > 0 || state.IsRecoveryBlocked)
            return;

        var before = state.ProductState;
        var hidden = FolderProductStateRules.HiddenLayers(
            before,
            TimelineKey);
        var restore = FolderProductStateRules.RestoreMap(
                before,
                TimelineKey)
            .ToDictionary(x => x.Key, x => x.Value);

        var changed = false;

        foreach (var layer in hidden.OrderBy(x => x))
        {
            var current = timeline.LayerSettings.IsVisibles[layer];

            if (current)
            {
                pluginSuppressed.Remove(layer);
                if (!restore.TryGetValue(layer, out var remembered)
                    || remembered != true)
                {
                    restore[layer] = true;
                    changed = true;
                }
            }
            else if (!pluginSuppressed.Contains(layer))
            {
                // The layer was previously released by an external "show".
                // A later external "hide" means the desired post-folder state
                // is now false.
                pluginSuppressed.Add(layer);
                if (!restore.TryGetValue(layer, out var remembered)
                    || remembered != false)
                {
                    restore[layer] = false;
                    changed = true;
                }
            }
        }

        pluginSuppressed.RemoveWhere(layer => !hidden.Contains(layer));

        if (!changed)
            return;

        var after = FolderProductStateRules.ReplaceRestoreMap(
            before,
            TimelineKey,
            restore);
        state.ReplaceProductState(after);

        log(
            $"visibility_external_override hidden={hidden.Count} " +
            $"restore={restore.Count}");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        pendingCapture?.Abort();
        pendingCapture = null;
        settingsSignal.PropertyChanged -= OnLayerSettingsChanged;
        state.Changed -= OnProductStateChanged;
        pluginSuppressed.Clear();
    }
}
