using Ymm4NoHarmonyFolderRanges;

namespace Ymm4NoHarmonyPersistence;

public sealed record PersistedFolder
{
    public required Guid Id { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsCollapsed { get; init; }
}

public sealed record TimelineFolderState
{
    // Opaque host-owned identity. P3 host integration decides how this is
    // resolved; the persistence model deliberately does not depend on Scene/YMM4.
    public required string TimelineKey { get; init; }
    public IReadOnlyList<PersistedFolder> Folders { get; init; } = [];
}

public sealed record FolderDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public IReadOnlyList<TimelineFolderState> Timelines { get; init; } = [];

    public static FolderDocument Empty { get; } = new();
}

public static class FolderDocumentRules
{
    public static FolderDocument NormalizeAndValidate(FolderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != FolderDocument.CurrentSchemaVersion)
            throw new ArgumentException($"Unsupported schema version {document.SchemaVersion}.", nameof(document));
        if (document.Timelines is null)
            throw new ArgumentException("Timelines must not be null.", nameof(document));

        var timelineKeys = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<TimelineFolderState>(document.Timelines.Count);

        foreach (var timeline in document.Timelines)
        {
            if (timeline is null)
                throw new ArgumentException("Timeline entry must not be null.", nameof(document));

            var key = timeline.TimelineKey?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("TimelineKey must not be empty.", nameof(document));
            if (!timelineKeys.Add(key))
                throw new ArgumentException($"Duplicate TimelineKey '{key}'.", nameof(document));
            if (timeline.Folders is null)
                throw new ArgumentException($"Folders for '{key}' must not be null.", nameof(document));

            var folders = timeline.Folders
                .Select(x => x ?? throw new ArgumentException($"Folder in '{key}' must not be null.", nameof(document)))
                .Select(x => x with { Name = x.Name ?? string.Empty })
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End)
                .ThenBy(x => x.Id)
                .ToArray();

            FolderRangeTracker.Validate(folders.Select(x => new FolderRange(x.Id, x.Start, x.End)));

            normalized.Add(timeline with
            {
                TimelineKey = key,
                Folders = Array.AsReadOnly(folders)
            });
        }

        return document with
        {
            Timelines = Array.AsReadOnly(normalized
                .OrderBy(x => x.TimelineKey, StringComparer.Ordinal)
                .ToArray())
        };
    }

    public static TimelineFolderState? FindTimeline(FolderDocument document, string timelineKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        return document.Timelines.FirstOrDefault(x =>
            string.Equals(x.TimelineKey, timelineKey.Trim(), StringComparison.Ordinal));
    }

    public static FolderDocument ReplaceTimeline(
        FolderDocument document,
        string timelineKey,
        IEnumerable<PersistedFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(folders);

        var key = timelineKey.Trim();
        var entries = document.Timelines
            .Where(x => !string.Equals(x.TimelineKey, key, StringComparison.Ordinal))
            .Append(new TimelineFolderState { TimelineKey = key, Folders = folders.ToArray() })
            .ToArray();

        return NormalizeAndValidate(document with { Timelines = entries });
    }

    public static FolderDocument RemoveTimeline(FolderDocument document, string timelineKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        var key = timelineKey.Trim();

        return NormalizeAndValidate(document with
        {
            Timelines = document.Timelines
                .Where(x => !string.Equals(x.TimelineKey, key, StringComparison.Ordinal))
                .ToArray()
        });
    }
}
