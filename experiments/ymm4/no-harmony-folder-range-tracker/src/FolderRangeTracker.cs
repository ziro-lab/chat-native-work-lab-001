namespace Ymm4NoHarmonyFolderRanges;

public readonly record struct FolderRange(Guid Id, int Start, int End)
{
    public int Count => End - Start + 1;
    public bool Contains(int layer) => Start <= layer && layer <= End;
    public bool Contains(FolderRange other) => Start <= other.Start && other.End <= End;
    public bool Intersects(FolderRange other) => Start <= other.End && other.Start <= End;
}

public abstract record StructuralEdit;

public sealed record InsertLayers(int Position, int Count) : StructuralEdit;

public sealed record DeleteLayers(int Position, int Count) : StructuralEdit;

public sealed record SwapAdjacentLayers(int FirstLayer) : StructuralEdit;

public sealed class FolderRangePlan
{
    private readonly FolderRange[] ranges;

    internal FolderRangePlan(IEnumerable<FolderRange> ranges, StructuralEdit edit)
    {
        this.ranges = ranges
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.End)
            .ThenBy(x => x.Id)
            .ToArray();
        Edit = edit;
    }

    public StructuralEdit Edit { get; }
    public IReadOnlyList<FolderRange> Ranges => ranges;

    public int MapLayer(int oldLayer) =>
        Edit switch
        {
            InsertLayers insert =>
                oldLayer >= insert.Position
                    ? checked(oldLayer + insert.Count)
                    : oldLayer,

            DeleteLayers delete =>
                oldLayer < delete.Position
                    ? oldLayer
                    : oldLayer < delete.Position + delete.Count
                        ? -1
                        : oldLayer - delete.Count,

            SwapAdjacentLayers swap =>
                oldLayer == swap.FirstLayer
                    ? swap.FirstLayer + 1
                    : oldLayer == swap.FirstLayer + 1
                        ? swap.FirstLayer
                        : oldLayer,

            _ => throw new NotSupportedException(Edit.GetType().FullName)
        };
}

/// <summary>
/// Pure folder-range transform. This class deliberately has no YMM4, WPF,
/// command, reflection, or display dependencies.
///
/// Product policy:
/// - folders are positional contiguous layer ranges;
/// - an insert inside a range joins that range;
/// - an insert at/before the folder head happens before the folder, so the
///   folder head follows the previous content downward;
/// - deleting the head promotes the next surviving row at that numeric head;
/// - standard adjacent layer reordering does not move folder metadata: rows
///   crossing a boundary change folder membership;
/// - nested/disjoint ranges are valid, crossing ranges are invalid;
/// - one layer can host at most one folder head.
/// </summary>
public static class FolderRangeTracker
{
    public static FolderRangePlan Apply(
        IEnumerable<FolderRange> source,
        StructuralEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edit);

        var current = source.ToList();
        Validate(current);

        var transformed = edit switch
        {
            InsertLayers insert => ApplyInsert(current, insert),
            DeleteLayers delete => ApplyDelete(current, delete),
            SwapAdjacentLayers swap => ApplySwap(current, swap),
            _ => throw new NotSupportedException(edit.GetType().FullName)
        };

        NormalizeNestedHeads(transformed);
        Validate(transformed);
        return new FolderRangePlan(transformed, edit);
    }

    public static void Validate(IEnumerable<FolderRange> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var ranges = source.ToArray();

        if (ranges.Select(x => x.Id).Distinct().Count() != ranges.Length)
            throw new ArgumentException("Folder IDs must be unique.", nameof(source));

        foreach (var range in ranges)
        {
            if (range.Id == Guid.Empty)
                throw new ArgumentException("Folder ID must not be empty.", nameof(source));
            if (range.Start < 0 || range.End < range.Start)
                throw new ArgumentException($"Invalid folder range {range}.", nameof(source));
        }

        var duplicateHead = ranges
            .GroupBy(x => x.Start)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateHead is not null)
            throw new ArgumentException(
                $"Multiple folders cannot share head layer {duplicateHead.Key}.",
                nameof(source));

        for (var i = 0; i < ranges.Length; i++)
        {
            for (var j = i + 1; j < ranges.Length; j++)
            {
                var a = ranges[i];
                var b = ranges[j];

                if (!a.Intersects(b))
                    continue;

                if (a.Contains(b) || b.Contains(a))
                    continue;

                throw new ArgumentException(
                    $"Crossing folder ranges are ambiguous: {a} vs {b}.",
                    nameof(source));
            }
        }
    }

    private static List<FolderRange> ApplyInsert(
        IReadOnlyList<FolderRange> source,
        InsertLayers edit)
    {
        if (edit.Position < 0)
            throw new ArgumentOutOfRangeException(nameof(edit.Position));
        if (edit.Count <= 0)
            throw new ArgumentOutOfRangeException(nameof(edit.Count));

        var result = new List<FolderRange>(source.Count);

        foreach (var range in source)
        {
            if (range.Start < edit.Position && edit.Position <= range.End)
            {
                result.Add(range with { End = checked(range.End + edit.Count) });
            }
            else if (edit.Position <= range.Start)
            {
                result.Add(range with
                {
                    Start = checked(range.Start + edit.Count),
                    End = checked(range.End + edit.Count)
                });
            }
            else
            {
                result.Add(range);
            }
        }

        return result;
    }

    private static List<FolderRange> ApplyDelete(
        IReadOnlyList<FolderRange> source,
        DeleteLayers edit)
    {
        if (edit.Position < 0)
            throw new ArgumentOutOfRangeException(nameof(edit.Position));
        if (edit.Count <= 0)
            throw new ArgumentOutOfRangeException(nameof(edit.Count));

        var lastDeleted = checked(edit.Position + edit.Count - 1);
        var result = new List<FolderRange>(source.Count);

        foreach (var range in source)
        {
            var deletedBeforeHead = Math.Max(
                0,
                Math.Min(lastDeleted, range.Start - 1) - edit.Position + 1);

            var deletedInside = Math.Max(
                0,
                Math.Min(lastDeleted, range.End)
                    - Math.Max(edit.Position, range.Start)
                    + 1);

            var next = range with
            {
                Start = range.Start - deletedBeforeHead,
                End = range.End - deletedBeforeHead - deletedInside
            };

            if (next.End >= next.Start)
                result.Add(next);
        }

        return result;
    }

    private static List<FolderRange> ApplySwap(
        IReadOnlyList<FolderRange> source,
        SwapAdjacentLayers edit)
    {
        if (edit.FirstLayer < 0)
            throw new ArgumentOutOfRangeException(nameof(edit.FirstLayer));

        // Positional-folder policy: the standard YMM4 MoveUp/MoveDown action
        // swaps row contents/identity. Folder metadata remains attached to
        // logical positions. Crossing a boundary therefore changes membership.
        return source.ToList();
    }

    private static void NormalizeNestedHeads(List<FolderRange> ranges)
    {
        for (var guard = 0; guard < 4096; guard++)
        {
            var ordered = ranges
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End)
                .ThenBy(x => x.Id)
                .ToArray();

            var changed = false;

            foreach (var child in ordered)
            {
                FolderRange? parent = null;

                foreach (var candidate in ordered)
                {
                    if (candidate.Id == child.Id)
                        continue;

                    var strictlyContains =
                        candidate.Contains(child)
                        && (candidate.Start < child.Start || child.End < candidate.End);

                    if (!strictlyContains)
                        continue;

                    if (parent is null
                        || candidate.Count < parent.Value.Count
                        || (candidate.Count == parent.Value.Count
                            && candidate.Start > parent.Value.Start))
                    {
                        parent = candidate;
                    }
                }

                if (parent is null || parent.Value.Start != child.Start)
                    continue;

                var index = ranges.FindIndex(x => x.Id == child.Id);
                if (index < 0)
                    throw new InvalidOperationException("Folder identity disappeared.");

                var shifted = child with { Start = child.Start + 1 };

                if (shifted.Start > shifted.End)
                    ranges.RemoveAt(index);
                else
                    ranges[index] = shifted;

                changed = true;
                break;
            }

            if (!changed)
                return;
        }

        throw new InvalidOperationException("Folder head normalization did not converge.");
    }
}
