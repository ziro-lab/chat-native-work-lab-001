using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyPersistence;

namespace Ymm4NoHarmonyStructuralConvenience;

public readonly record struct GroupSpan(int Layer, int Range)
{
    public int LastControlled => checked(Layer + Range);
    public bool Controls(int layer) => Layer < layer && layer <= LastControlled;
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
    FolderDocument Core,
    Func<int, int> MapLayer,
    IReadOnlyList<int> GroupRanges);

public sealed record GroupControlAddPlan(
    StructuralConveniencePlan Structural,
    int GroupLayer,
    int GroupRange);

public static class StructuralConvenienceRules
{
    public static StructuralConveniencePlan PlanInsertIntoFolder(
        FolderDocument document,
        string timelineKey,
        Guid folderId,
        int position,
        int count,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(groups);
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder id must not be empty.", nameof(folderId));
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey)
            ?? throw new KeyNotFoundException($"Timeline '{timelineKey.Trim()}' was not found.");
        var target = timeline.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new KeyNotFoundException($"Folder '{folderId}' was not found.");

        if (position < target.Start || position > checked(target.End + 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                $"Insert position L{position} is outside folder {target.Start}..{target.End} plus its tail boundary.");
        }

        var beforeFolders = timeline.Folders.ToArray();
        var byId = beforeFolders.ToDictionary(x => x.Id);
        var ranges = beforeFolders
            .Select(x => new FolderRange(x.Id, x.Start, x.End))
            .ToArray();

        var standard = FolderRangeTracker.Apply(
            ranges,
            new InsertLayers(position, count));

        var owningChain = beforeFolders
            .Where(x =>
                x.Start <= target.Start
                && target.End <= x.End)
            .OrderBy(x => x.End - x.Start)
            .ThenByDescending(x => x.Start)
            .ToArray();
        var owningIds = owningChain.Select(x => x.Id).ToHashSet();

        var nextFolders = standard.Ranges
            .Select(range =>
            {
                var original = byId[range.Id];
                if (!owningIds.Contains(range.Id))
                {
                    return original with
                    {
                        Start = range.Start,
                        End = range.End
                    };
                }

                var start = range.Start;
                var end = range.End;

                if (position == original.Start)
                    start = original.Start;

                if (position == checked(original.End + 1))
                    end = checked(original.End + count);

                return original with
                {
                    Start = start,
                    End = end
                };
            })
            .ToArray();

        var next = FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            nextFolders);

        var groupRanges = groups
            .Select(group =>
                Math.Max(
                    1,
                    AdjustRangeForInsert(
                        group.Layer,
                        group.Range,
                        position,
                        count,
                        owningChain)))
            .ToArray();

        return new(
            next,
            standard.MapLayer,
            Array.AsReadOnly(groupRanges));
    }

    public static StructuralConveniencePlan PlanDeleteFolderContents(
        FolderDocument document,
        string timelineKey,
        Guid folderId,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(groups);

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey)
            ?? throw new KeyNotFoundException($"Timeline '{timelineKey.Trim()}' was not found.");
        var folder = timeline.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new KeyNotFoundException($"Folder '{folderId}' was not found.");

        var position = folder.Start;
        var count = checked(folder.End - folder.Start + 1);
        var standard = FolderRangeTracker.Apply(
            timeline.Folders.Select(
                x => new FolderRange(x.Id, x.Start, x.End)),
            new DeleteLayers(position, count));

        var byId = timeline.Folders.ToDictionary(x => x.Id);
        var nextFolders = standard.Ranges
            .Select(range =>
            {
                var original = byId[range.Id];
                return original with
                {
                    Start = range.Start,
                    End = range.End
                };
            })
            .ToArray();

        var next = FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            nextFolders);

        var groupRanges = groups
            .Select(group =>
                standard.MapLayer(group.Layer) < 0
                    ? group.Range
                    : Math.Max(
                        1,
                        AdjustRangeForDelete(
                            group.Layer,
                            group.Range,
                            position,
                            count)))
            .ToArray();

        return new(
            next,
            standard.MapLayer,
            Array.AsReadOnly(groupRanges));
    }

    public static GroupControlAddPlan PlanAddGroupControl(
        FolderDocument document,
        string timelineKey,
        Guid folderId,
        IReadOnlyList<GroupSpan> groups)
    {
        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var folder = FolderDocumentRules.FindTimeline(normalized, timelineKey)
            ?.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new KeyNotFoundException($"Folder '{folderId}' was not found.");

        var structural = PlanInsertIntoFolder(
            normalized,
            timelineKey,
            folderId,
            folder.Start,
            1,
            groups);

        var updated = FolderDocumentRules.FindTimeline(
                structural.Core,
                timelineKey)
            ?.Folders.FirstOrDefault(x => x.Id == folderId)
            ?? throw new InvalidOperationException("Target folder disappeared during Group Control planning.");

        return new(
            structural,
            updated.Start,
            Math.Max(1, updated.End - updated.Start));
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
            var group = groups[i];

            if (folder.Start <= group.Layer && group.Layer <= folder.End)
            {
                if (group.LastControlled > folder.End
                    && group.Layer < folder.End)
                {
                    issues.Add(new(
                        i,
                        GroupIssueKind.LeaksOutOfFolder,
                        Math.Max(1, folder.End - group.Layer)));
                }

                continue;
            }

            if (group.Layer >= folder.Start)
                continue;

            if (group.LastControlled >= folder.Start
                && group.LastControlled < folder.End)
            {
                issues.Add(new(
                    i,
                    GroupIssueKind.CoversFolderPartially,
                    Math.Max(1, folder.End - group.Layer)));
            }
        }

        return issues;
    }

    public static IReadOnlyList<int> PlanFitGroupRanges(
        PersistedFolder folder,
        IReadOnlyList<GroupSpan> groups)
    {
        var ranges = groups.Select(x => x.Range).ToArray();

        foreach (var issue in FindGroupIssues(folder, groups))
            ranges[issue.GroupIndex] = issue.SuggestedRange;

        return Array.AsReadOnly(ranges);
    }

    public static IReadOnlyList<int?> SuggestExternalGroupRangeFixes(
        FolderDocument beforeDocument,
        string timelineKey,
        StructuralEdit edit,
        IReadOnlyList<GroupSpan> groupsAfter)
    {
        ArgumentNullException.ThrowIfNull(beforeDocument);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(groupsAfter);

        var before = FolderDocumentRules.NormalizeAndValidate(beforeDocument);
        var folders = FolderDocumentRules.FindTimeline(before, timelineKey)
            ?.Folders
            ?? [];

        var result = new int?[groupsAfter.Count];

        switch (edit)
        {
            case InsertLayers insert:
            {
                var position = insert.Position;
                var count = insert.Count;

                if (!folders.Any(
                        folder =>
                            folder.Start < position
                            && position <= folder.End))
                {
                    return result;
                }

                for (var i = 0; i < groupsAfter.Count; i++)
                {
                    var group = groupsAfter[i];

                    if (position <= group.Layer
                        && group.Layer < position + count)
                    {
                        continue;
                    }

                    var beforeLayer = group.Layer < position
                        ? group.Layer
                        : group.Layer - count;

                    if (beforeLayer < position
                        && position <= beforeLayer + group.Range)
                    {
                        result[i] = checked(group.Range + count);
                    }
                }

                return result;
            }

            case DeleteLayers delete:
            {
                var position = delete.Position;
                var count = delete.Count;
                var lastDeleted = checked(position + count - 1);

                if (!folders.Any(
                        folder =>
                            folder.Start <= position
                            && lastDeleted <= folder.End))
                {
                    return result;
                }

                for (var i = 0; i < groupsAfter.Count; i++)
                {
                    var group = groupsAfter[i];
                    var beforeLayer = group.Layer < position
                        ? group.Layer
                        : checked(group.Layer + count);
                    var range = AdjustRangeForDelete(
                        beforeLayer,
                        group.Range,
                        position,
                        count);

                    if (range != group.Range)
                        result[i] = Math.Max(1, range);
                }

                return result;
            }

            default:
                return result;
        }
    }

    private static int AdjustRangeForInsert(
        int groupLayer,
        int range,
        int position,
        int count,
        IReadOnlyList<PersistedFolder> owningChain)
    {
        if (groupLayer >= position)
            return range;

        if (position <= groupLayer + range)
            return checked(range + count);

        if (position == groupLayer + range + 1
            && ExtendsAtFolderEnd(
                owningChain,
                groupLayer,
                range,
                position))
        {
            return checked(range + count);
        }

        return range;
    }

    private static bool ExtendsAtFolderEnd(
        IEnumerable<PersistedFolder> owningChain,
        int groupLayer,
        int range,
        int position) =>
        position == groupLayer + range + 1
        && owningChain.Any(
            folder =>
                folder.End == groupLayer + range
                && groupLayer >= folder.Start - 1);

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
