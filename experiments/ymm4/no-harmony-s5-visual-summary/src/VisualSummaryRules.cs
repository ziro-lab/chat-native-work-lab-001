namespace Ymm4NoHarmonyVisualSummary;

public readonly record struct VisualItemSpan(
    string Key,
    int Layer,
    int Frame,
    int Length)
{
    public int EndFrame => checked(Frame + Length);
}

public readonly record struct TimingBand(
    string Key,
    int SourceLayer,
    int OwnerLayer,
    int VisualRow,
    int Frame,
    int Length)
{
    public int EndFrame => checked(Frame + Length);
}

public readonly record struct GroupVisualSegment(
    int GroupLayer,
    int VisualStartRow,
    int VisualEndRow,
    int LogicalStart,
    int LogicalEnd)
{
    public int VisualRowCount =>
        checked(VisualEndRow - VisualStartRow + 1);
}

public static class VisualSummaryRules
{
    public static IReadOnlyList<TimingBand> BuildTimingBands(
        IEnumerable<VisualItemSpan> items,
        Func<int, bool> isHidden,
        Func<int, int> ownerOf,
        Func<int, int> visualRowOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isHidden);
        ArgumentNullException.ThrowIfNull(ownerOf);
        ArgumentNullException.ThrowIfNull(visualRowOf);

        var result = new List<TimingBand>();

        foreach (var item in items)
        {
            if (item.Layer < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"Item '{item.Key}' has a negative layer.");
            if (item.Frame < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"Item '{item.Key}' has a negative frame.");
            if (item.Length <= 0)
                continue;
            if (!isHidden(item.Layer))
                continue;

            var owner = ownerOf(item.Layer);
            if (owner < 0 || owner == item.Layer)
            {
                throw new InvalidOperationException(
                    $"Hidden layer L{item.Layer} did not resolve to a visible owner.");
            }

            var row = visualRowOf(owner);
            if (row < 0)
                throw new InvalidOperationException(
                    $"Owner L{owner} resolved to a negative visual row.");

            result.Add(new TimingBand(
                item.Key,
                item.Layer,
                owner,
                row,
                item.Frame,
                item.Length));
        }

        return Array.AsReadOnly(result
            .OrderBy(x => x.VisualRow)
            .ThenBy(x => x.OwnerLayer)
            .ThenBy(x => x.SourceLayer)
            .ThenBy(x => x.Frame)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToArray());
    }

    public static IReadOnlyList<GroupVisualSegment> BuildGroupSegments(
        int groupLayer,
        int groupRange,
        Func<int, int> ownerOf,
        Func<int, int> visualRowOf)
    {
        if (groupLayer < 0)
            throw new ArgumentOutOfRangeException(nameof(groupLayer));
        if (groupRange < 1)
            throw new ArgumentOutOfRangeException(nameof(groupRange));
        ArgumentNullException.ThrowIfNull(ownerOf);
        ArgumentNullException.ThrowIfNull(visualRowOf);

        var logicalEnd = checked(groupLayer + groupRange);

        var projected = Enumerable.Range(
                groupLayer,
                groupRange + 1)
            .Select(layer => new
            {
                Layer = layer,
                Owner = ownerOf(layer)
            })
            .Select(x => new
            {
                x.Layer,
                x.Owner,
                Row = visualRowOf(x.Owner)
            })
            .OrderBy(x => x.Layer)
            .ToArray();

        if (projected.Any(x => x.Owner < 0 || x.Row < 0))
            throw new InvalidOperationException(
                "Group projection resolved an invalid owner or row.");

        // One visual row may represent several logical layers under a collapsed
        // folder. Preserve the logical coverage for diagnostics but do not draw
        // duplicate background rows.
        var uniqueRows = projected
            .GroupBy(x => x.Row)
            .Select(group => new
            {
                Row = group.Key,
                LogicalStart = group.Min(x => x.Layer),
                LogicalEnd = group.Max(x => x.Layer)
            })
            .OrderBy(x => x.Row)
            .ToArray();

        if (uniqueRows.Length == 0)
            return Array.Empty<GroupVisualSegment>();

        var result = new List<GroupVisualSegment>();
        var startRow = uniqueRows[0].Row;
        var endRow = uniqueRows[0].Row;
        var logicalStart = uniqueRows[0].LogicalStart;
        var logicalEndSegment = uniqueRows[0].LogicalEnd;

        for (var i = 1; i < uniqueRows.Length; i++)
        {
            var current = uniqueRows[i];

            if (current.Row == endRow + 1)
            {
                endRow = current.Row;
                logicalStart = Math.Min(
                    logicalStart,
                    current.LogicalStart);
                logicalEndSegment = Math.Max(
                    logicalEndSegment,
                    current.LogicalEnd);
                continue;
            }

            result.Add(new GroupVisualSegment(
                groupLayer,
                startRow,
                endRow,
                logicalStart,
                logicalEndSegment));

            startRow = endRow = current.Row;
            logicalStart = current.LogicalStart;
            logicalEndSegment = current.LogicalEnd;
        }

        result.Add(new GroupVisualSegment(
            groupLayer,
            startRow,
            endRow,
            logicalStart,
            logicalEndSegment));

        return Array.AsReadOnly(result.ToArray());
    }
}
