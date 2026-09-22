namespace Ymm4NoHarmonyState;

using Ymm4NoHarmonyPersistence;

public readonly record struct LayerVisibilityWrite(
    int Layer,
    bool Visible);

public sealed record VisibilityTransition(
    FolderSessionDocument State,
    IReadOnlyList<LayerVisibilityWrite> Writes);

/// <summary>
/// Host-independent visibility policy.
///
/// Folder hidden state is persisted per FolderId, while effective hidden layers
/// are always derived from the union of hidden folder ranges. The restore map is
/// one entry per logical layer, never one reference count per folder.
/// </summary>
public static class FolderVisibilityRules
{
    public static VisibilityTransition SetFolderHidden(
        FolderSessionDocument source,
        string timelineKey,
        Guid folderId,
        bool hidden,
        IReadOnlyDictionary<int, bool> currentVisibility)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(currentVisibility);
        if (folderId == Guid.Empty)
            throw new ArgumentException(
                "Folder id must not be empty.",
                nameof(folderId));

        var before =
            FolderSessionDocumentRules.NormalizeAndValidate(source);
        var key = timelineKey.Trim();
        var timeline = FolderDocumentRules.FindTimeline(before.Core, key)
            ?? throw new KeyNotFoundException(
                $"Timeline '{key}' was not found.");
        var target = timeline.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        var oldOption =
            FolderSessionDocumentRules.FindOption(before, key, folderId);
        if ((oldOption?.Hidden ?? false) == hidden)
            return new(before, Array.Empty<LayerVisibilityWrite>());

        var next = FolderSessionDocumentRules.SetFolderOption(
            before,
            key,
            folderId,
            oldOption?.Color,
            hidden);

        var oldCovered = CoveredLayers(before, key);
        var newCovered = CoveredLayers(next, key);

        var restore = before.VisibilityRestore
            .ToDictionary(
                x => (x.TimelineKey, x.Layer),
                x => x);

        var writes = new SortedDictionary<int, bool>();

        if (hidden)
        {
            // First coverage captures the current host-visible state exactly once.
            foreach (var layer in newCovered)
            {
                if (!oldCovered.Contains(layer))
                {
                    if (!currentVisibility.TryGetValue(layer, out var visible))
                    {
                        throw new ArgumentException(
                            $"Current visibility is missing for L{layer}.",
                            nameof(currentVisibility));
                    }

                    restore[(key, layer)] = new VisibilityRestoreState
                    {
                        TimelineKey = key,
                        Layer = layer,
                        RestoreVisible = visible,
                        UserOverride = false
                    };
                }
            }

            // Explicitly hiding this folder is the user's new hide intent. It may
            // re-hide a layer that had been manually shown while another hidden
            // reason remained. Preserve the user's desired post-unhide value, but
            // clear the override flag and hide it now.
            for (var layer = target.Start; layer <= target.End; layer++)
            {
                var restoreKey = (key, layer);
                if (restore.TryGetValue(restoreKey, out var entry)
                    && entry.UserOverride)
                {
                    restore[restoreKey] = entry with
                    {
                        UserOverride = false
                    };
                }

                writes[layer] = false;
            }
        }
        else
        {
            // A layer is restored only when the last hidden-folder reason leaves.
            foreach (var layer in oldCovered.Except(newCovered))
            {
                var restoreKey = (key, layer);
                if (!restore.Remove(restoreKey, out var entry))
                {
                    throw new InvalidOperationException(
                        $"Missing visibility restore entry for L{layer}.");
                }

                writes[layer] = entry.RestoreVisible;
            }
        }

        next = FolderSessionDocumentRules.NormalizeAndValidate(
            next with
            {
                VisibilityRestore = restore.Values.ToArray()
            });

        return new(
            next,
            writes.Select(x =>
                new LayerVisibilityWrite(x.Key, x.Value)).ToArray());
    }

    /// <summary>
    /// Record an explicit host/user visibility change while a layer is still
    /// covered by at least one hidden folder. No host write is produced: the
    /// user's current value is respected immediately and becomes the value to
    /// restore after the final hidden reason is removed.
    /// </summary>
    public static FolderSessionDocument ObserveUserVisibility(
        FolderSessionDocument source,
        string timelineKey,
        int layer,
        bool visible)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        if (layer < 0)
            throw new ArgumentOutOfRangeException(nameof(layer));

        var normalized =
            FolderSessionDocumentRules.NormalizeAndValidate(source);
        var key = timelineKey.Trim();

        if (!CoveredLayers(normalized, key).Contains(layer))
            return normalized;

        var entries = normalized.VisibilityRestore.ToArray();
        var index = Array.FindIndex(
            entries,
            x => x.Layer == layer
                && string.Equals(
                    x.TimelineKey,
                    key,
                    StringComparison.Ordinal));

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Hidden L{layer} has no visibility restore entry.");
        }

        entries[index] = entries[index] with
        {
            RestoreVisible = visible,
            UserOverride = true
        };

        return FolderSessionDocumentRules.NormalizeAndValidate(
            normalized with
            {
                VisibilityRestore = entries
            });
    }

    public static IReadOnlySet<int> CoveredLayers(
        FolderSessionDocument source,
        string timelineKey)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);

        var normalized =
            FolderSessionDocumentRules.NormalizeAndValidate(source);
        var key = timelineKey.Trim();
        var timeline = FolderDocumentRules.FindTimeline(
            normalized.Core,
            key);

        if (timeline is null)
            return new HashSet<int>();

        var hiddenIds = normalized.FolderOptions
            .Where(x =>
                x.Hidden
                && string.Equals(
                    x.TimelineKey,
                    key,
                    StringComparison.Ordinal))
            .Select(x => x.FolderId)
            .ToHashSet();

        var result = new HashSet<int>();

        foreach (var folder in timeline.Folders)
        {
            if (!hiddenIds.Contains(folder.Id))
                continue;

            for (var layer = folder.Start; layer <= folder.End; layer++)
                result.Add(layer);
        }

        return result;
    }
}
