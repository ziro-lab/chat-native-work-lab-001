using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyStructuralConvenience;

namespace Ymm4NoHarmonyPanel;

public enum PanelDropZone
{
    Before,
    Into,
    After
}

public sealed record PanelDragBlock(
    int Start,
    int Count,
    Guid? SourceParentFolderId,
    IReadOnlyList<Guid> MovingFolderIds)
{
    public int End => checked(Start + Count - 1);
}

public sealed record PanelDropTarget(
    int OriginalInsertionBoundary,
    Guid? IntoFolderId);

public sealed record PanelMovePlan(
    FolderDocument Core,
    Func<int, int> MapLayer,
    IReadOnlyList<int> GroupRanges,
    int Start,
    int Count,
    int OriginalInsertionBoundary,
    int FinalInsertionPoint,
    Guid? IntoFolderId,
    IReadOnlyList<Guid> MovingFolderIds);

public static class PanelMoveRules
{
    public static IReadOnlyList<PanelRow> TopLevelSelectedRows(
        IReadOnlyList<PanelRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var selectedFolders = rows
            .Where(row =>
                row.Kind == PanelRowKind.Folder
                && row.FolderId is not null)
            .ToArray();

        return Array.AsReadOnly(rows
            .Where(row =>
                !selectedFolders.Any(folder =>
                    !string.Equals(folder.Key, row.Key, StringComparison.Ordinal)
                    && folder.First <= row.First
                    && row.Last <= folder.Last
                    && row.Depth > folder.Depth))
            .OrderBy(row => row.First)
            .ThenByDescending(row => row.Last)
            .ToArray());
    }

    public static PanelDragBlock? TryGetDragBlock(
        IReadOnlyList<PanelRow> selectedRows)
    {
        ArgumentNullException.ThrowIfNull(selectedRows);

        var rows = TopLevelSelectedRows(selectedRows);
        if (rows.Count == 0)
            return null;

        var parent = rows[0].ParentFolderId;

        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].ParentFolderId != parent
                || rows[i].First != rows[i - 1].Last + 1)
            {
                return null;
            }
        }

        var movingFolders = rows
            .Where(row =>
                row.Kind == PanelRowKind.Folder
                && row.FolderId is not null)
            .Select(row => row.FolderId!.Value)
            .ToArray();

        return new PanelDragBlock(
            Start: rows[0].First,
            Count: checked(rows[^1].Last - rows[0].First + 1),
            SourceParentFolderId: parent,
            MovingFolderIds: Array.AsReadOnly(movingFolders));
    }

    public static PanelDropTarget ResolveDrop(
        PanelRow target,
        PanelDropZone zone)
    {
        ArgumentNullException.ThrowIfNull(target);

        return (target.Kind, zone) switch
        {
            (PanelRowKind.Layer, PanelDropZone.Before) =>
                new(target.First, target.ParentFolderId),

            (PanelRowKind.Layer, PanelDropZone.After) =>
                new(checked(target.First + 1), target.ParentFolderId),

            (PanelRowKind.Folder, PanelDropZone.Before) =>
                new(target.First, target.ParentFolderId),

            (PanelRowKind.Folder, PanelDropZone.Into) =>
                new(checked(target.Last + 1), target.FolderId),

            // "After" an expanded folder header is visually before its first
            // child row, so it means the beginning of that folder's contents.
            (PanelRowKind.Folder, PanelDropZone.After)
                when !target.IsCollapsed =>
                new(checked(target.First + 1), target.FolderId),

            (PanelRowKind.Folder, PanelDropZone.After) =>
                new(checked(target.Last + 1), target.ParentFolderId),

            (_, PanelDropZone.Into) =>
                throw new ArgumentException(
                    "Only folder rows accept Into drops.",
                    nameof(zone)),

            _ => throw new ArgumentOutOfRangeException(nameof(zone))
        };
    }

    public static bool CanDrop(
        FolderDocument document,
        string timelineKey,
        PanelDragBlock block,
        PanelDropTarget drop)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(drop);

        if (!IsValidMoveTarget(
                block.Start,
                block.Count,
                drop.OriginalInsertionBoundary))
        {
            return false;
        }

        if ((drop.OriginalInsertionBoundary == block.Start
                || drop.OriginalInsertionBoundary
                    == block.Start + block.Count)
            && drop.IntoFolderId == block.SourceParentFolderId)
        {
            return false;
        }

        if (drop.IntoFolderId is null)
            return true;

        var timeline = FolderDocumentRules.FindTimeline(
            FolderDocumentRules.NormalizeAndValidate(document),
            timelineKey);
        if (timeline is null)
            return false;

        var descendants = DescendantFolderIds(
            timeline.Folders,
            block.MovingFolderIds);

        return !descendants.Contains(drop.IntoFolderId.Value);
    }

    public static PanelMovePlan PlanMove(
        FolderDocument document,
        string timelineKey,
        PanelDragBlock block,
        PanelDropTarget drop,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(drop);
        ArgumentNullException.ThrowIfNull(groups);

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);

        if (!CanDrop(
                normalized,
                timelineKey,
                block,
                drop))
        {
            throw new ArgumentException(
                "Move target is not valid for the selected block.",
                nameof(drop));
        }

        var timeline = FolderDocumentRules.FindTimeline(
            normalized,
            timelineKey)
            ?? throw new KeyNotFoundException(
                $"Timeline '{timelineKey.Trim()}' was not found.");

        var folders = timeline.Folders.ToList();
        var movingIds = DescendantFolderIds(
            folders,
            block.MovingFolderIds);
        var moving = folders
            .Where(folder => movingIds.Contains(folder.Id))
            .ToArray();

        if (moving.Any(folder =>
                folder.Start < block.Start
                || block.End < folder.End))
        {
            throw new ArgumentException(
                "A moving folder extends outside the selected block.",
                nameof(block));
        }

        var stationary = folders
            .Where(folder => !movingIds.Contains(folder.Id))
            .ToList();

        ApplyDeleteRaw(
            stationary,
            block.Start,
            block.Count);

        var finalInsertion = InsertionPointAfterRemoval(
            block.Start,
            block.Count,
            drop.OriginalInsertionBoundary);

        var parentsAfterDelete = PanelProjection.ParentMap(
            stationary
                .Where(folder => folder.End >= folder.Start)
                .ToArray());
        var chainIds = SelfAndAncestorIds(
            stationary,
            parentsAfterDelete,
            drop.IntoFolderId);

        var chainSnapshot = stationary
            .Where(folder => chainIds.Contains(folder.Id))
            .Select(folder => folder with { })
            .ToArray();

        ApplyInsertRaw(
            stationary,
            finalInsertion,
            block.Count,
            chainIds);

        stationary.RemoveAll(folder =>
            folder.End < folder.Start);

        var map = MoveMap(
            block.Start,
            block.Count,
            drop.OriginalInsertionBoundary);

        foreach (var folder in moving)
        {
            stationary.Add(folder with
            {
                Start = map(folder.Start),
                End = map(folder.End)
            });
        }

        NormalizeNestedHeads(stationary);

        var next = FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            stationary);

        var groupRanges = groups
            .Select(group =>
            {
                if (block.Start <= group.Layer
                    && group.Layer <= block.End)
                {
                    return group.Range;
                }

                var wasControlled = group.Controls(block.Start);
                var range = AdjustRangeForDelete(
                    group.Layer,
                    group.Range,
                    block.Start,
                    block.Count);
                var layerAfterRemoval =
                    group.Layer > block.End
                        ? group.Layer - block.Count
                        : group.Layer;

                return Math.Max(
                    1,
                    AdjustRangeForInsert(
                        layerAfterRemoval,
                        range,
                        finalInsertion,
                        block.Count,
                        chainSnapshot,
                        wasControlled));
            })
            .ToArray();

        return new PanelMovePlan(
            Core: next,
            MapLayer: map,
            GroupRanges: Array.AsReadOnly(groupRanges),
            Start: block.Start,
            Count: block.Count,
            OriginalInsertionBoundary:
                drop.OriginalInsertionBoundary,
            FinalInsertionPoint: finalInsertion,
            IntoFolderId: drop.IntoFolderId,
            MovingFolderIds:
                Array.AsReadOnly(block.MovingFolderIds.ToArray()));
    }

    public static int InsertionPointAfterRemoval(
        int start,
        int count,
        int target) =>
        target > start
            ? checked(target - count)
            : target;

    public static Func<int, int> MoveMap(
        int start,
        int count,
        int target)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (!IsValidMoveTarget(start, count, target))
            throw new ArgumentException(
                "Target is inside the moved block.",
                nameof(target));

        var insertion = InsertionPointAfterRemoval(
            start,
            count,
            target);

        return layer =>
        {
            if (start <= layer
                && layer < start + count)
            {
                return checked(
                    insertion + (layer - start));
            }

            var removed =
                layer >= start + count
                    ? layer - count
                    : layer;

            return removed >= insertion
                ? checked(removed + count)
                : removed;
        };
    }

    public static bool IsValidMoveTarget(
        int start,
        int count,
        int target) =>
        target <= start
        || target >= start + count;

    private static HashSet<Guid> DescendantFolderIds(
        IReadOnlyList<PersistedFolder> folders,
        IEnumerable<Guid> roots)
    {
        var rootSet = roots.ToHashSet();
        var parents = PanelProjection.ParentMap(folders);
        var result = new HashSet<Guid>();

        foreach (var folder in folders)
        {
            Guid? current = folder.Id;

            while (current is { } id)
            {
                if (rootSet.Contains(id))
                {
                    result.Add(folder.Id);
                    break;
                }

                current = parents.GetValueOrDefault(id);
            }
        }

        return result;
    }

    private static HashSet<Guid> SelfAndAncestorIds(
        IReadOnlyList<PersistedFolder> folders,
        IReadOnlyDictionary<Guid, Guid?> parents,
        Guid? folderId)
    {
        var result = new HashSet<Guid>();
        var current = folderId;

        while (current is { } id)
        {
            if (!folders.Any(folder => folder.Id == id))
            {
                throw new ArgumentException(
                    $"Drop target folder '{id}' does not exist.",
                    nameof(folderId));
            }

            if (!result.Add(id))
                throw new InvalidOperationException(
                    "Folder parent relation contains a cycle.");

            current = parents.GetValueOrDefault(id);
        }

        return result;
    }

    private static void ApplyDeleteRaw(
        List<PersistedFolder> folders,
        int position,
        int count)
    {
        var last = checked(position + count - 1);

        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];

            var before = Math.Max(
                0,
                Math.Min(last, folder.Start - 1)
                    - position
                    + 1);

            var overlap = Math.Max(
                0,
                Math.Min(last, folder.End)
                    - Math.Max(position, folder.Start)
                    + 1);

            folders[i] = folder with
            {
                Start = folder.Start - before,
                End = folder.End - before - overlap
            };
        }
    }

    private static void ApplyInsertRaw(
        List<PersistedFolder> folders,
        int position,
        int count,
        IReadOnlySet<Guid> intoChain)
    {
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            var inChain = intoChain.Contains(folder.Id);

            if (folder.Start < position
                && position <= folder.End)
            {
                folders[i] = folder with
                {
                    End = checked(folder.End + count)
                };
            }
            else if (position <= folder.Start)
            {
                folders[i] =
                    inChain && position == folder.Start
                        ? folder with
                        {
                            End = checked(folder.End + count)
                        }
                        : folder with
                        {
                            Start = checked(folder.Start + count),
                            End = checked(folder.End + count)
                        };
            }
            else if (inChain
                && position == folder.End + 1)
            {
                folders[i] = folder with
                {
                    End = checked(folder.End + count)
                };
            }
        }
    }

    private static int AdjustRangeForDelete(
        int groupLayer,
        int range,
        int position,
        int count)
    {
        var last = checked(position + count - 1);
        var overlap = Math.Max(
            0,
            Math.Min(last, groupLayer + range)
                - Math.Max(position, groupLayer + 1)
                + 1);
        return range - overlap;
    }

    private static int AdjustRangeForInsert(
        int groupLayer,
        int range,
        int position,
        int count,
        IReadOnlyList<PersistedFolder> chain,
        bool wasControlled)
    {
        if (groupLayer >= position)
            return range;

        if (position <= groupLayer + range)
            return checked(range + count);

        if (position == groupLayer + range + 1
            && (wasControlled
                || ExtendsAtFolderEnd(
                    chain,
                    groupLayer,
                    range,
                    position)))
        {
            return checked(range + count);
        }

        return range;
    }

    private static bool ExtendsAtFolderEnd(
        IEnumerable<PersistedFolder> chain,
        int groupLayer,
        int range,
        int position) =>
        position == groupLayer + range + 1
        && chain.Any(folder =>
            folder.End == groupLayer + range
            && groupLayer >= folder.Start - 1);

    private static void NormalizeNestedHeads(
        List<PersistedFolder> folders)
    {
        for (var guard = 0; guard < 4096; guard++)
        {
            folders.RemoveAll(folder =>
                folder.End < folder.Start);

            var parents = PanelProjection.ParentMap(folders);

            var child = folders
                .OrderBy(folder => folder.Start)
                .ThenByDescending(folder => folder.End)
                .ThenBy(folder => folder.Id)
                .FirstOrDefault(folder =>
                    parents.GetValueOrDefault(folder.Id)
                        is { } parentId
                    && folders.First(
                        parent => parent.Id == parentId).Start
                        == folder.Start);

            if (child is null)
                return;

            var index = folders.FindIndex(folder =>
                folder.Id == child.Id);

            var shifted = child with
            {
                Start = checked(child.Start + 1)
            };

            if (shifted.Start > shifted.End)
                folders.RemoveAt(index);
            else
                folders[index] = shifted;
        }

        throw new InvalidOperationException(
            "Folder-head normalization did not converge.");
    }
}
