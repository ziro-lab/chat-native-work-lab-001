using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;

namespace Ymm4NoHarmonyS3;

public readonly record struct GroupSpan(int Layer, int Range)
{
    public int LastControlled => Layer + Range;
    public bool Controls(int layer) =>
        Layer < layer && layer <= LastControlled;
}

public enum GroupIssueKind
{
    LeaksOutOfFolder,
    CoversFolderPartially
}

public readonly record struct GroupIssue(
    int GroupIndex,
    GroupIssueKind Kind,
    int SuggestedRange);

public sealed record StructuralConveniencePlan(
    FolderProductState State,
    StructuralEdit Edit,
    IReadOnlyList<int> GroupRanges)
{
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

public static class StructuralConvenienceRules
{
    public static StructuralConveniencePlan PlanInsertIntoFolder(
        FolderProductState state,
        string timelineKey,
        Guid folderId,
        int position,
        int count,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder id must not be empty.", nameof(folderId));
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var normalized = FolderProductStateRules.NormalizeAndValidate(state);
        var timeline = FolderDocumentRules.FindTimeline(
                normalized.Core,
                timelineKey)
            ?? throw new KeyNotFoundException(
                $"Timeline '{timelineKey}' was not found.");
        var owner = timeline.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        if (position < owner.Start || position > owner.End + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                $"Insert position L{position} is outside folder {owner.Start}..{owner.End}+1.");
        }

        var edit = new InsertLayers(position, count);
        var beforeFolders = timeline.Folders.ToArray();
        var beforeById = beforeFolders.ToDictionary(x => x.Id);

        var chainIds = beforeFolders
            .Where(x =>
                x.Id == owner.Id
                || (x.Start <= owner.Start && owner.End <= x.End))
            .Select(x => x.Id)
            .ToHashSet();

        var standard = FolderRangeTracker.Apply(
            beforeFolders.Select(
                x => new FolderRange(x.Id, x.Start, x.End)),
            edit);

        var nextFolders = standard.Ranges
            .Select(range =>
            {
                var before = beforeById[range.Id];
                if (!chainIds.Contains(range.Id))
                {
                    return new PersistedFolder
                    {
                        Id = range.Id,
                        Start = range.Start,
                        End = range.End,
                        Name = before.Name,
                        IsCollapsed = before.IsCollapsed
                    };
                }

                var start = range.Start;
                var end = range.End;

                if (position == before.Start)
                {
                    start = before.Start;
                    end = checked(before.End + count);
                }
                else if (position == before.End + 1)
                {
                    end = checked(before.End + count);
                }

                return new PersistedFolder
                {
                    Id = range.Id,
                    Start = start,
                    End = end,
                    Name = before.Name,
                    IsCollapsed = before.IsCollapsed
                };
            })
            .ToArray();

        var nextCore = FolderDocumentRules.ReplaceTimeline(
            normalized.Core,
            timelineKey,
            nextFolders);
        var nextState = FolderProductStateRules.ReplaceCore(
            normalized,
            nextCore);
        nextState = FolderProductStateRules.RemapRestoreLayers(
            nextState,
            timelineKey,
            standard.MapLayer);

        var chain = beforeFolders
            .Where(x => chainIds.Contains(x.Id))
            .ToArray();
        var groupRanges = groups
            .Select(g => Math.Max(
                1,
                AdjustRangeForInsert(
                    g.Layer,
                    g.Range,
                    position,
                    count,
                    chain)))
            .ToArray();

        return new(
            nextState,
            edit,
            Array.AsReadOnly(groupRanges));
    }

    public static StructuralConveniencePlan PlanDeleteFolderContents(
        FolderProductState state,
        string timelineKey,
        Guid folderId,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder id must not be empty.", nameof(folderId));

        var normalized = FolderProductStateRules.NormalizeAndValidate(state);
        var timeline = FolderDocumentRules.FindTimeline(
                normalized.Core,
                timelineKey)
            ?? throw new KeyNotFoundException(
                $"Timeline '{timelineKey}' was not found.");
        var folder = timeline.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new KeyNotFoundException(
                $"Folder '{folderId}' was not found.");

        var edit = new DeleteLayers(
            folder.Start,
            folder.End - folder.Start + 1);
        var beforeFolders = timeline.Folders.ToArray();
        var beforeById = beforeFolders.ToDictionary(x => x.Id);

        var standard = FolderRangeTracker.Apply(
            beforeFolders.Select(
                x => new FolderRange(x.Id, x.Start, x.End)),
            edit);

        var nextFolders = standard.Ranges
            .Select(range =>
            {
                var before = beforeById[range.Id];
                return new PersistedFolder
                {
                    Id = range.Id,
                    Start = range.Start,
                    End = range.End,
                    Name = before.Name,
                    IsCollapsed = before.IsCollapsed
                };
            })
            .ToArray();

        var nextCore = FolderDocumentRules.ReplaceTimeline(
            normalized.Core,
            timelineKey,
            nextFolders);
        var nextState = FolderProductStateRules.ReplaceCore(
            normalized,
            nextCore);
        nextState = FolderProductStateRules.RemapRestoreLayers(
            nextState,
            timelineKey,
            standard.MapLayer);

        var groupRanges = groups
            .Select(g =>
                standard.MapLayer(g.Layer) < 0
                    ? g.Range
                    : Math.Max(
                        1,
                        AdjustRangeForDelete(
                            g.Layer,
                            g.Range,
                            edit.Position,
                            edit.Count)))
            .ToArray();

        return new(
            nextState,
            edit,
            Array.AsReadOnly(groupRanges));
    }

    public static IReadOnlyList<GroupIssue> FindGroupIssues(
        PersistedFolder folder,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(groups);

        var issues = new List<GroupIssue>();
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];

            if (folder.Start <= g.Layer && g.Layer <= folder.End)
            {
                if (g.LastControlled > folder.End
                    && g.Layer < folder.End)
                {
                    issues.Add(new(
                        i,
                        GroupIssueKind.LeaksOutOfFolder,
                        folder.End - g.Layer));
                }
            }
            else if (g.Layer < folder.Start)
            {
                if (g.LastControlled >= folder.Start
                    && g.LastControlled < folder.End)
                {
                    issues.Add(new(
                        i,
                        GroupIssueKind.CoversFolderPartially,
                        folder.End - g.Layer));
                }
            }
        }

        return issues;
    }

    public static IReadOnlyList<int> FitGroupRanges(
        PersistedFolder folder,
        IReadOnlyList<GroupSpan> groups)
    {
        var ranges = groups.Select(x => x.Range).ToArray();
        foreach (var issue in FindGroupIssues(folder, groups))
            ranges[issue.GroupIndex] = Math.Max(1, issue.SuggestedRange);
        return Array.AsReadOnly(ranges);
    }

    public static IReadOnlyList<int?> FixGroupsAfterExternalEdit(
        IReadOnlyList<PersistedFolder> foldersBefore,
        StructuralEdit edit,
        IReadOnlyList<GroupSpan> groupsAfter)
    {
        ArgumentNullException.ThrowIfNull(foldersBefore);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(groupsAfter);

        var result = new int?[groupsAfter.Count];

        if (edit is InsertLayers insert)
        {
            var p = insert.Position;
            var n = insert.Count;
            if (!foldersBefore.Any(
                    f => f.Start < p && p <= f.End))
                return result;

            for (var i = 0; i < groupsAfter.Count; i++)
            {
                var g = groupsAfter[i];
                if (p <= g.Layer && g.Layer < p + n)
                    continue;

                var beforeLayer = g.Layer < p
                    ? g.Layer
                    : g.Layer - n;

                if (beforeLayer < p
                    && p <= beforeLayer + g.Range)
                {
                    result[i] = checked(g.Range + n);
                }
            }

            return result;
        }

        if (edit is DeleteLayers delete)
        {
            var p = delete.Position;
            var n = delete.Count;
            var q = checked(p + n - 1);

            if (!foldersBefore.Any(
                    f => f.Start <= p && q <= f.End))
                return result;

            for (var i = 0; i < groupsAfter.Count; i++)
            {
                var g = groupsAfter[i];
                var beforeLayer = g.Layer < p
                    ? g.Layer
                    : checked(g.Layer + n);
                var range = AdjustRangeForDelete(
                    beforeLayer,
                    g.Range,
                    p,
                    n);

                if (range != g.Range)
                    result[i] = Math.Max(1, range);
            }

            return result;
        }

        return result;
    }

    private static int AdjustRangeForInsert(
        int groupLayer,
        int range,
        int position,
        int count,
        IReadOnlyList<PersistedFolder> chain)
    {
        if (groupLayer >= position)
            return range;

        if (position <= groupLayer + range)
            return checked(range + count);

        if (position == groupLayer + range + 1
            && chain.Any(
                f =>
                    f.End == groupLayer + range
                    && groupLayer >= f.Start - 1))
        {
            return checked(range + count);
        }

        return range;
    }

    private static int AdjustRangeForDelete(
        int groupLayer,
        int range,
        int position,
        int count)
    {
        var lastDeleted = checked(position + count - 1);
        var overlap = Math.Max(
            0,
            Math.Min(lastDeleted, groupLayer + range)
                - Math.Max(position, groupLayer + 1)
                + 1);
        return range - overlap;
    }
}
