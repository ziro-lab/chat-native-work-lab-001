using Ymm4NoHarmonyPersistence;

namespace Ymm4NoHarmonyUx;

public enum FolderCreationStatus
{
    Allowed,
    EmptySelection,
    TooSmall,
    InvalidSelection,
    NonContiguous,
    InvalidRange
}

public sealed record FolderCreationDecision(
    FolderCreationStatus Status,
    int? Start = null,
    int? End = null,
    string? Detail = null,
    int? RequestedStart = null,
    int? RequestedEnd = null,
    bool NeedsAdditionalLayer = false)
{
    public bool Allowed => Status == FolderCreationStatus.Allowed;
    public bool AdjustedForFolderHead =>
        Allowed
        && RequestedStart is not null
        && Start is not null
        && RequestedStart.Value != Start.Value;
}

public static class FolderUxCommands
{
    public static FolderCreationDecision EvaluateCreate(
        FolderDocument document,
        string timelineKey,
        IEnumerable<int> selectedLayers,
        Guid candidateId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(selectedLayers);
        if (candidateId == Guid.Empty)
            throw new ArgumentException("Candidate folder id must not be empty.", nameof(candidateId));

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var layers = selectedLayers.Distinct().OrderBy(x => x).ToArray();

        if (layers.Length == 0)
            return new(FolderCreationStatus.EmptySelection);
        if (layers.Any(x => x < 0))
            return new(FolderCreationStatus.InvalidSelection);
        if (layers.Length < 2)
            return new(FolderCreationStatus.TooSmall, layers[0], layers[0]);
        if (layers.Zip(layers.Skip(1), (a, b) => b == a + 1).Any(x => !x))
            return new(FolderCreationStatus.NonContiguous, layers[0], layers[^1]);

        var start = layers[0];
        var end = layers[^1];
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey);
        var folders = timeline?.Folders ?? [];

        try
        {
            FolderDocumentRules.ReplaceTimeline(
                normalized,
                timelineKey,
                folders.Append(new PersistedFolder
                {
                    Id = candidateId,
                    Start = start,
                    End = end,
                    Name = "_candidate_",
                    IsCollapsed = false
                }));
        }
        catch (ArgumentException ex)
        {
            return new(FolderCreationStatus.InvalidRange, start, end, ex.Message);
        }

        return new(FolderCreationStatus.Allowed, start, end);
    }

    /// <summary>
    /// Resolve the reference LayerPatan creation behavior from a layer-context click.
    /// If the clicked layer is selected, the whole selected min..max span is used
    /// (gaps are intentionally included). Otherwise only the clicked layer is used.
    /// Existing folder heads are skipped downward until a legal nested/disjoint
    /// range is found. A final one-row range is legal and requests one empty layer
    /// insertion during the host-owned composite create operation.
    /// </summary>
    public static FolderCreationDecision EvaluateCreateFromContext(
        FolderDocument document,
        string timelineKey,
        IEnumerable<int> selectedLayers,
        int clickedLayer,
        Guid candidateId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(selectedLayers);
        if (candidateId == Guid.Empty)
            throw new ArgumentException("Candidate folder id must not be empty.", nameof(candidateId));

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var layers = selectedLayers.Distinct().OrderBy(x => x).ToArray();

        if (clickedLayer < 0 || layers.Any(x => x < 0))
            return new(FolderCreationStatus.InvalidSelection);

        var useSelection = layers.Contains(clickedLayer);
        var requestedStart = useSelection ? layers[0] : clickedLayer;
        var requestedEnd = useSelection ? layers[^1] : clickedLayer;
        var start = requestedStart;
        var end = requestedEnd;
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey);
        var folders = timeline?.Folders ?? [];

        for (var guard = 0; guard < 4096; guard++)
        {
            var head = folders.FirstOrDefault(
                x => x.Start == start && end <= x.End);

            if (head is null)
                break;

            start++;
            end = Math.Max(end, start);
            if (start > head.End)
            {
                return new(
                    FolderCreationStatus.InvalidRange,
                    start,
                    end,
                    "Folder head adjustment exhausted the containing folder.",
                    requestedStart,
                    requestedEnd);
            }

            if (guard == 4095)
            {
                return new(
                    FolderCreationStatus.InvalidRange,
                    start,
                    end,
                    "Folder head adjustment did not converge.",
                    requestedStart,
                    requestedEnd);
            }
        }

        try
        {
            FolderDocumentRules.ReplaceTimeline(
                normalized,
                timelineKey,
                folders.Append(new PersistedFolder
                {
                    Id = candidateId,
                    Start = start,
                    End = end,
                    Name = "_candidate_",
                    IsCollapsed = false
                }));
        }
        catch (ArgumentException ex)
        {
            return new(
                FolderCreationStatus.InvalidRange,
                start,
                end,
                ex.Message,
                requestedStart,
                requestedEnd);
        }

        return new(
            FolderCreationStatus.Allowed,
            start,
            end,
            RequestedStart: requestedStart,
            RequestedEnd: requestedEnd,
            NeedsAdditionalLayer: start == end);
    }

    public static FolderDocument CreateFolder(
        FolderDocument document,
        string timelineKey,
        FolderCreationDecision decision,
        Guid id,
        string name)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.Allowed || decision.Start is null || decision.End is null)
            throw new InvalidOperationException($"Folder creation rejected: {decision.Status}");
        if (id == Guid.Empty)
            throw new ArgumentException("Folder id must not be empty.", nameof(id));

        var cleanName = NormalizeName(name);
        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey);
        var folders = timeline?.Folders ?? [];

        return FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            folders.Append(new PersistedFolder
            {
                Id = id,
                Start = decision.Start.Value,
                End = decision.End.Value,
                Name = cleanName,
                IsCollapsed = false
            }));
    }

    /// <summary>
    /// Compute the final FolderDocument for the one-row creation convenience
    /// after one host layer is inserted immediately below the requested row.
    /// The inserted row belongs to the new folder and all of its ancestors.
    /// </summary>
    public static FolderDocument CreateFolderWithInsertedLayer(
        FolderDocument document,
        string timelineKey,
        FolderCreationDecision decision,
        Guid id,
        string name)
    {
        if (!decision.NeedsAdditionalLayer
            || decision.Start is null
            || decision.End is null
            || decision.Start.Value != decision.End.Value)
        {
            throw new InvalidOperationException(
                "Inserted-layer create requires an allowed one-row decision.");
        }

        var created = CreateFolder(document, timelineKey, decision, id, name);
        var timeline = FolderDocumentRules.FindTimeline(created, timelineKey)
            ?? throw new InvalidOperationException("Created timeline state is missing.");
        var originals = timeline.Folders.ToDictionary(x => x.Id);
        var insertionPosition = decision.End.Value + 1;

        var owningIds = timeline.Folders
            .Where(x =>
                x.Id == id
                || (x.Start <= decision.Start.Value
                    && decision.Start.Value <= x.End))
            .Select(x => x.Id)
            .ToHashSet();

        var plan = Ymm4NoHarmonyFolderRanges.FolderRangeTracker.Apply(
            timeline.Folders.Select(
                x => new Ymm4NoHarmonyFolderRanges.FolderRange(
                    x.Id,
                    x.Start,
                    x.End)),
            new Ymm4NoHarmonyFolderRanges.InsertLayers(
                insertionPosition,
                1));

        var transformed = plan.Ranges.Select(range =>
        {
            var original = originals[range.Id];
            var end = range.End;

            // Standard insertion at End+1 does not grow a positional folder.
            // This is an explicit "insert into this folder" operation, so the
            // new folder and every containing ancestor must own the new row.
            if (owningIds.Contains(range.Id))
                end = Math.Max(end, checked(original.End + 1));

            return original with
            {
                Start = range.Start,
                End = end
            };
        });

        return FolderDocumentRules.ReplaceTimeline(
            created,
            timelineKey,
            transformed);
    }

    public static FolderDocument CreateFolder(
        FolderDocument document,
        string timelineKey,
        IEnumerable<int> selectedLayers,
        Guid id,
        string name)
    {
        var decision = EvaluateCreate(document, timelineKey, selectedLayers, id);
        if (!decision.Allowed || decision.Start is null || decision.End is null)
            throw new InvalidOperationException($"Folder creation rejected: {decision.Status}");

        var cleanName = NormalizeName(name);
        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey);
        var folders = timeline?.Folders ?? [];

        return FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            folders.Append(new PersistedFolder
            {
                Id = id,
                Start = decision.Start.Value,
                End = decision.End.Value,
                Name = cleanName,
                IsCollapsed = false
            }));
    }

    public static FolderDocument RenameFolder(
        FolderDocument document,
        string timelineKey,
        Guid folderId,
        string name) =>
        ReplaceFolder(document, timelineKey, folderId, x => x with { Name = NormalizeName(name) });

    public static FolderDocument ToggleCollapsed(
        FolderDocument document,
        string timelineKey,
        Guid folderId) =>
        ReplaceFolder(document, timelineKey, folderId, x => x with { IsCollapsed = !x.IsCollapsed });

    public static FolderDocument SetAllCollapsed(
        FolderDocument document,
        string timelineKey,
        bool collapsed)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey);
        if (timeline is null)
            return normalized;

        return FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            timeline.Folders.Select(x => x with { IsCollapsed = collapsed }));
    }

    public static FolderDocument Ungroup(
        FolderDocument document,
        string timelineKey,
        Guid folderId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder id must not be empty.", nameof(folderId));

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey)
            ?? throw new KeyNotFoundException($"Timeline '{timelineKey.Trim()}' was not found.");

        if (!timeline.Folders.Any(x => x.Id == folderId))
            throw new KeyNotFoundException($"Folder '{folderId}' was not found.");

        return FolderDocumentRules.ReplaceTimeline(
            normalized,
            timelineKey,
            timeline.Folders.Where(x => x.Id != folderId));
    }

    private static FolderDocument ReplaceFolder(
        FolderDocument document,
        string timelineKey,
        Guid folderId,
        Func<PersistedFolder, PersistedFolder> change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(change);
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder id must not be empty.", nameof(folderId));

        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        var timeline = FolderDocumentRules.FindTimeline(normalized, timelineKey)
            ?? throw new KeyNotFoundException($"Timeline '{timelineKey.Trim()}' was not found.");

        var found = false;
        var folders = timeline.Folders.Select(x =>
        {
            if (x.Id != folderId)
                return x;
            found = true;
            return change(x);
        }).ToArray();

        if (!found)
            throw new KeyNotFoundException($"Folder '{folderId}' was not found.");

        return FolderDocumentRules.ReplaceTimeline(normalized, timelineKey, folders);
    }

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var value = name.Trim();
        if (value.Length == 0)
            throw new ArgumentException("Folder name must not be blank.", nameof(name));
        return value;
    }
}
