using Ymm4NoHarmonyFolderRanges;

namespace Ymm4NoHarmonyStructuralObserver;

public readonly record struct LayerPair(int OldLayer, int NewLayer);

public enum StructuralDetectionStatus
{
    None,
    Exact,
    Ambiguous
}

public sealed record StructuralDetection(
    StructuralDetectionStatus Status,
    IReadOnlyList<StructuralEdit> Edits,
    string Reason)
{
    public static StructuralDetection None(string reason) =>
        new(StructuralDetectionStatus.None, Array.Empty<StructuralEdit>(), reason);

    public static StructuralDetection Exact(IEnumerable<StructuralEdit> edits, string reason) =>
        new(StructuralDetectionStatus.Exact, edits.ToArray(), reason);

    public static StructuralDetection Ambiguous(string reason) =>
        new(StructuralDetectionStatus.Ambiguous, Array.Empty<StructuralEdit>(), reason);
}

/// <summary>
/// Host-independent structural delta detector.
///
/// Input is intentionally reduced to stable old/new layer observations plus
/// optional insert/delete hints. It knows nothing about YMM4, WPF, commands,
/// view models, UndoRedoManager, reflection or display geometry.
///
/// The detector is conservative: if an empty gap makes the exact structural
/// position underdetermined, it reports Ambiguous instead of guessing.
/// </summary>
public static class StructuralDeltaDetector
{
    public static StructuralDetection Detect(
        IEnumerable<LayerPair> pairs,
        IReadOnlySet<int>? vanishedLayers = null,
        IReadOnlySet<int>? insertedHints = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        var deltaByOld = new SortedDictionary<int, int>();

        foreach (var pair in pairs)
        {
            if (pair.OldLayer < 0 || pair.NewLayer < 0)
                return StructuralDetection.Ambiguous("Negative layer observation.");

            var delta = pair.NewLayer - pair.OldLayer;

            if (deltaByOld.TryGetValue(pair.OldLayer, out var existing))
            {
                if (existing != delta)
                    return StructuralDetection.Ambiguous(
                        $"Old layer {pair.OldLayer} mapped to multiple new layers.");
            }
            else
            {
                deltaByOld[pair.OldLayer] = delta;
            }
        }

        if (deltaByOld.Count == 0)
            return StructuralDetection.None("No stable layer observations.");

        var swap = TryDetectAdjacentSwap(deltaByOld);
        if (swap is not null)
            return StructuralDetection.Exact([swap], "Exact adjacent swap.");

        var edits = new List<StructuralEdit>();
        var previousOld = -1;
        var previousDelta = 0;
        var previousNew = -1;

        foreach (var (oldLayer, delta) in deltaByOld)
        {
            var newLayer = oldLayer + delta;

            if (newLayer <= previousNew)
            {
                return StructuralDetection.Ambiguous(
                    "Observed order changed but was not an exact adjacent swap.");
            }

            if (delta > previousDelta)
            {
                var count = delta - previousDelta;
                var low = previousNew + 1;
                var high = oldLayer + previousDelta;

                var position = ResolveInsertPosition(
                    low,
                    high,
                    count,
                    insertedHints);

                if (position is null)
                {
                    return StructuralDetection.Ambiguous(
                        $"Insert position is underdetermined in [{low},{high}] for count={count}.");
                }

                edits.Add(new InsertLayers(position.Value, count));
            }
            else if (delta < previousDelta)
            {
                var count = previousDelta - delta;
                var gap = oldLayer - previousOld - 1;

                if (gap < count)
                {
                    return StructuralDetection.Ambiguous(
                        $"Delete count {count} does not fit observed old-layer gap {gap}.");
                }

                var deleted = ResolveDeletedLayers(
                    previousOld,
                    oldLayer,
                    count,
                    vanishedLayers);

                if (deleted is null)
                {
                    return StructuralDetection.Ambiguous(
                        $"Delete position is underdetermined between {previousOld} and {oldLayer}.");
                }

                var removedSoFar = 0;
                foreach (var deletedOldLayer in deleted)
                {
                    edits.Add(
                        new DeleteLayers(
                            deletedOldLayer + previousDelta - removedSoFar,
                            1));
                    removedSoFar++;
                }
            }

            previousOld = oldLayer;
            previousDelta = delta;
            previousNew = newLayer;
        }

        edits = MergeDeletes(edits);

        if (edits.Count == 0)
        {
            var changed = deltaByOld.Any(x => x.Value != 0);
            return changed
                ? StructuralDetection.Ambiguous("Changed layers did not form a supported structural edit.")
                : StructuralDetection.None("All observed layers retained identity positions.");
        }

        return StructuralDetection.Exact(edits, "Exact monotonic insert/delete delta.");
    }

    private static SwapAdjacentLayers? TryDetectAdjacentSwap(
        IReadOnlyDictionary<int, int> deltaByOld)
    {
        var changed = deltaByOld
            .Where(x => x.Value != 0)
            .Select(x => (Old: x.Key, New: x.Key + x.Value))
            .OrderBy(x => x.Old)
            .ToArray();

        if (changed.Length != 2)
            return null;

        var first = changed[0];
        var second = changed[1];

        if (second.Old != first.Old + 1)
            return null;

        if (first.New != second.Old || second.New != first.Old)
            return null;

        return new SwapAdjacentLayers(first.Old);
    }

    private static int? ResolveInsertPosition(
        int low,
        int high,
        int count,
        IReadOnlySet<int>? insertedHints)
    {
        if (low > high)
            return null;

        if (low == high)
            return low;

        if (insertedHints is null || insertedHints.Count == 0)
            return null;

        var candidates = insertedHints
            .Where(x => low <= x && x <= high)
            .OrderBy(x => x)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        // For a multi-layer insert, prefer a contiguous hinted block.
        for (var i = 0; i < candidates.Length; i++)
        {
            var start = candidates[i];
            var contiguous = true;

            for (var offset = 1; offset < count; offset++)
            {
                if (!insertedHints.Contains(start + offset))
                {
                    contiguous = false;
                    break;
                }
            }

            if (contiguous)
                return start;
        }

        return count == 1 && candidates.Length == 1
            ? candidates[0]
            : null;
    }

    private static IReadOnlyList<int>? ResolveDeletedLayers(
        int previousOld,
        int oldLayer,
        int count,
        IReadOnlySet<int>? vanishedLayers)
    {
        var candidates = vanishedLayers?
            .Where(x => previousOld < x && x < oldLayer)
            .OrderBy(x => x)
            .ToArray()
            ?? Array.Empty<int>();

        if (candidates.Length == count && IsContiguous(candidates))
            return candidates;

        var gap = oldLayer - previousOld - 1;

        if (gap == count)
            return Enumerable.Range(oldLayer - count, count).ToArray();

        return null;
    }

    private static bool IsContiguous(IReadOnlyList<int> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] + 1)
                return false;
        }

        return true;
    }

    private static List<StructuralEdit> MergeDeletes(
        IReadOnlyList<StructuralEdit> edits)
    {
        var merged = new List<StructuralEdit>();

        foreach (var edit in edits)
        {
            if (edit is DeleteLayers delete
                && merged.LastOrDefault() is DeleteLayers previous
                && delete.Position == previous.Position)
            {
                merged[^1] = previous with { Count = previous.Count + delete.Count };
                continue;
            }

            merged.Add(edit);
        }

        return merged;
    }
}
