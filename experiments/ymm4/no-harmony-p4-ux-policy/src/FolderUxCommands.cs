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
    string? Detail = null)
{
    public bool Allowed => Status == FolderCreationStatus.Allowed;
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
