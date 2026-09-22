using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;

namespace Ymm4NoHarmonyPanel;

public enum PanelRowKind
{
    Folder,
    Layer
}

public sealed record PanelRow(
    string Key,
    PanelRowKind Kind,
    int First,
    int Last,
    int Depth,
    Guid? ParentFolderId,
    Guid? FolderId,
    string Name,
    bool IsCollapsed,
    bool IsHidden,
    int ItemCount,
    int GroupIssueCount)
{
    public int Count => checked(Last - First + 1);
}

public static class PanelProjection
{
    public static IReadOnlyList<PanelRow> Build(
        FolderProductState productState,
        string timelineKey,
        int maxLayer,
        IReadOnlyDictionary<int, int> itemCounts,
        IReadOnlyList<GroupSpan> groups)
    {
        ArgumentNullException.ThrowIfNull(productState);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(itemCounts);
        ArgumentNullException.ThrowIfNull(groups);

        var state = FolderProductStateRules.NormalizeAndValidate(productState);
        var key = timelineKey.Trim();
        var timeline = FolderDocumentRules.FindTimeline(state.Core, key);
        var folders = timeline?.Folders.ToArray() ?? [];

        var folderMax = folders.Select(x => x.End).DefaultIfEmpty(0).Max();
        var itemMax = itemCounts.Keys.DefaultIfEmpty(0).Max();
        var lastLayer = Math.Max(
            0,
            Math.Max(maxLayer, Math.Max(folderMax, itemMax))) + 1;

        var parents = ParentMap(folders);
        var ordered = folders
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.End)
            .ThenBy(x => x.Id)
            .ToArray();

        var rows = new List<PanelRow>();

        void EmitLayer(
            int layer,
            int depth,
            Guid? parentId)
        {
            rows.Add(new PanelRow(
                Key: $"L{layer}",
                Kind: PanelRowKind.Layer,
                First: layer,
                Last: layer,
                Depth: depth,
                ParentFolderId: parentId,
                FolderId: null,
                Name: $"L{layer:00}",
                IsCollapsed: false,
                IsHidden: false,
                ItemCount: itemCounts.GetValueOrDefault(layer),
                GroupIssueCount: 0));
        }

        void EmitRange(
            int start,
            int end,
            int depth,
            Guid? parentId)
        {
            var cursor = start;
            var children = ordered
                .Where(folder =>
                    parents.GetValueOrDefault(folder.Id) == parentId)
                .OrderBy(folder => folder.Start)
                .ThenByDescending(folder => folder.End)
                .ToArray();

            foreach (var child in children)
            {
                for (; cursor < child.Start; cursor++)
                    EmitLayer(cursor, depth, parentId);

                EmitFolder(child, depth, parentId);

                if (!child.IsCollapsed)
                {
                    EmitRange(
                        child.Start,
                        child.End,
                        depth + 1,
                        child.Id);
                }

                cursor = child.End + 1;
            }

            for (; cursor <= end; cursor++)
                EmitLayer(cursor, depth, parentId);
        }

        void EmitFolder(
            PersistedFolder folder,
            int depth,
            Guid? parentId)
        {
            var option = FolderProductStateRules.FindOption(
                state,
                key,
                folder.Id);
            var itemCount = Enumerable
                .Range(folder.Start, folder.End - folder.Start + 1)
                .Sum(layer => itemCounts.GetValueOrDefault(layer));
            var issues = StructuralConvenienceRules.FindGroupIssues(
                folder,
                groups);

            rows.Add(new PanelRow(
                Key: $"F{folder.Id:D}",
                Kind: PanelRowKind.Folder,
                First: folder.Start,
                Last: folder.End,
                Depth: depth,
                ParentFolderId: parentId,
                FolderId: folder.Id,
                Name: folder.Name,
                IsCollapsed: folder.IsCollapsed,
                IsHidden: option?.Hidden == true,
                ItemCount: itemCount,
                GroupIssueCount: issues.Count));
        }

        EmitRange(0, lastLayer, 0, null);
        return Array.AsReadOnly(rows.ToArray());
    }

    public static IReadOnlyDictionary<Guid, Guid?> ParentMap(
        IReadOnlyList<PersistedFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var result = new Dictionary<Guid, Guid?>();

        foreach (var folder in folders)
        {
            var parent = folders
                .Where(candidate =>
                    candidate.Id != folder.Id
                    && candidate.Start <= folder.Start
                    && folder.End <= candidate.End
                    && (candidate.Start < folder.Start
                        || folder.End < candidate.End))
                .OrderBy(candidate =>
                    candidate.End - candidate.Start)
                .ThenByDescending(candidate => candidate.Start)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();

            result[folder.Id] = parent?.Id;
        }

        return result;
    }

    public static int DepthOf(
        IReadOnlyDictionary<Guid, Guid?> parents,
        Guid folderId)
    {
        ArgumentNullException.ThrowIfNull(parents);

        var depth = 0;
        var seen = new HashSet<Guid>();
        Guid? current = folderId;

        while (current is { } id
            && parents.TryGetValue(id, out var parent)
            && parent is { } parentId)
        {
            if (!seen.Add(id))
                throw new InvalidOperationException(
                    "Folder parent relation contains a cycle.");

            depth++;
            current = parentId;
        }

        return depth;
    }
}
