using System.Text.Json;

namespace Ymm4NoHarmonyState;

using Ymm4NoHarmonyPersistence;

public sealed record FolderOptionState
{
    public required string TimelineKey { get; init; }
    public required Guid FolderId { get; init; }
    public string? Color { get; init; }
    public bool Hidden { get; init; }
}

public sealed record VisibilityRestoreState
{
    public required string TimelineKey { get; init; }
    public required int Layer { get; init; }
    public required bool RestoreVisible { get; init; }
    public bool UserOverride { get; init; }
}

public sealed record FolderSessionDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public FolderDocument Core { get; init; } = FolderDocument.Empty;
    public IReadOnlyList<FolderOptionState> FolderOptions { get; init; } = [];
    public IReadOnlyList<VisibilityRestoreState> VisibilityRestore { get; init; } = [];

    public static FolderSessionDocument Empty { get; } = new();
}

public static class FolderSessionDocumentRules
{
    public static FolderSessionDocument NormalizeAndValidate(
        FolderSessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != FolderSessionDocument.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Unsupported session schema version {document.SchemaVersion}.",
                nameof(document));
        }

        var core = FolderDocumentRules.NormalizeAndValidate(
            document.Core ?? throw new ArgumentException(
                "Core must not be null.",
                nameof(document)));

        if (document.FolderOptions is null)
            throw new ArgumentException("FolderOptions must not be null.", nameof(document));
        if (document.VisibilityRestore is null)
            throw new ArgumentException("VisibilityRestore must not be null.", nameof(document));

        var folderIdsByTimeline = core.Timelines.ToDictionary(
            x => x.TimelineKey,
            x => x.Folders.Select(f => f.Id).ToHashSet(),
            StringComparer.Ordinal);

        var optionKeys = new HashSet<(string TimelineKey, Guid FolderId)>();
        var options = new List<FolderOptionState>();

        foreach (var option in document.FolderOptions)
        {
            if (option is null)
                throw new ArgumentException("Folder option must not be null.", nameof(document));

            var timelineKey = NormalizeTimelineKey(option.TimelineKey);
            if (option.FolderId == Guid.Empty)
                throw new ArgumentException("Folder option ID must not be empty.", nameof(document));

            if (!folderIdsByTimeline.TryGetValue(timelineKey, out var folderIds)
                || !folderIds.Contains(option.FolderId))
            {
                throw new ArgumentException(
                    $"Folder option points to an unknown folder: {timelineKey}/{option.FolderId}.",
                    nameof(document));
            }

            if (!optionKeys.Add((timelineKey, option.FolderId)))
            {
                throw new ArgumentException(
                    $"Duplicate folder option: {timelineKey}/{option.FolderId}.",
                    nameof(document));
            }

            var color = NormalizeColor(option.Color);

            // Default options carry no information and are not persisted.
            if (color is null && !option.Hidden)
                continue;

            options.Add(option with
            {
                TimelineKey = timelineKey,
                Color = color
            });
        }

        var restoreKeys = new HashSet<(string TimelineKey, int Layer)>();
        var restore = new List<VisibilityRestoreState>();

        foreach (var entry in document.VisibilityRestore)
        {
            if (entry is null)
                throw new ArgumentException(
                    "Visibility restore entry must not be null.",
                    nameof(document));

            var timelineKey = NormalizeTimelineKey(entry.TimelineKey);
            if (entry.Layer < 0)
            {
                throw new ArgumentException(
                    $"Visibility restore layer must not be negative: {entry.Layer}.",
                    nameof(document));
            }

            if (!folderIdsByTimeline.ContainsKey(timelineKey))
            {
                throw new ArgumentException(
                    $"Visibility restore points to an unknown timeline: {timelineKey}.",
                    nameof(document));
            }

            if (!restoreKeys.Add((timelineKey, entry.Layer)))
            {
                throw new ArgumentException(
                    $"Duplicate visibility restore entry: {timelineKey}/L{entry.Layer}.",
                    nameof(document));
            }

            restore.Add(entry with { TimelineKey = timelineKey });
        }

        return document with
        {
            Core = core,
            FolderOptions = Array.AsReadOnly(
                options
                    .OrderBy(x => x.TimelineKey, StringComparer.Ordinal)
                    .ThenBy(x => x.FolderId)
                    .ToArray()),
            VisibilityRestore = Array.AsReadOnly(
                restore
                    .OrderBy(x => x.TimelineKey, StringComparer.Ordinal)
                    .ThenBy(x => x.Layer)
                    .ToArray())
        };
    }

    public static FolderOptionState? FindOption(
        FolderSessionDocument document,
        string timelineKey,
        Guid folderId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var key = NormalizeTimelineKey(timelineKey);
        return document.FolderOptions.FirstOrDefault(
            x => x.FolderId == folderId
                && string.Equals(x.TimelineKey, key, StringComparison.Ordinal));
    }

    public static FolderSessionDocument ReplaceCore(
        FolderSessionDocument document,
        FolderDocument core)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(core);

        var normalizedCore = FolderDocumentRules.NormalizeAndValidate(core);
        var alive = normalizedCore.Timelines.ToDictionary(
            x => x.TimelineKey,
            x => x.Folders.Select(f => f.Id).ToHashSet(),
            StringComparer.Ordinal);

        return NormalizeAndValidate(document with
        {
            Core = normalizedCore,
            FolderOptions = document.FolderOptions
                .Where(x =>
                    alive.TryGetValue(x.TimelineKey, out var ids)
                    && ids.Contains(x.FolderId))
                .ToArray(),
            VisibilityRestore = document.VisibilityRestore
                .Where(x => alive.ContainsKey(x.TimelineKey))
                .ToArray()
        });
    }

    public static FolderSessionDocument ReplaceCoreAfterStructuralEdit(
        FolderSessionDocument document,
        FolderDocument core,
        string timelineKey,
        Ymm4NoHarmonyFolderRanges.FolderRangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(core);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        ArgumentNullException.ThrowIfNull(plan);

        var source = NormalizeAndValidate(document);
        var key = timelineKey.Trim();
        var replaced = ReplaceCore(source, core);

        var remappedTarget = source.VisibilityRestore
            .Where(x =>
                string.Equals(
                    x.TimelineKey,
                    key,
                    StringComparison.Ordinal))
            .Select(x => (Entry: x, Layer: plan.MapLayer(x.Layer)))
            .Where(x => x.Layer >= 0)
            .Select(x => x.Entry with { Layer = x.Layer });

        var other = replaced.VisibilityRestore
            .Where(x =>
                !string.Equals(
                    x.TimelineKey,
                    key,
                    StringComparison.Ordinal));

        return NormalizeAndValidate(replaced with
        {
            VisibilityRestore = other
                .Concat(remappedTarget)
                .ToArray()
        });
    }

    public static FolderSessionDocument SetFolderOption(
        FolderSessionDocument document,
        string timelineKey,
        Guid folderId,
        string? color,
        bool hidden)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeAndValidate(document);
        var key = NormalizeTimelineKey(timelineKey);

        var existing = normalized.FolderOptions
            .Where(x =>
                !(x.FolderId == folderId
                  && string.Equals(x.TimelineKey, key, StringComparison.Ordinal)))
            .Append(new FolderOptionState
            {
                TimelineKey = key,
                FolderId = folderId,
                Color = color,
                Hidden = hidden
            });

        return NormalizeAndValidate(normalized with
        {
            FolderOptions = existing.ToArray()
        });
    }

    private static string NormalizeTimelineKey(string? timelineKey)
    {
        var key = timelineKey?.Trim();
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("TimelineKey must not be empty.");
        return key;
    }

    private static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        var value = color.Trim().ToUpperInvariant();
        if (value.Length != 9 || value[0] != '#')
            throw new ArgumentException(
                "Folder color must use #AARRGGBB format.");

        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            var hex =
                ('0' <= ch && ch <= '9')
                || ('A' <= ch && ch <= 'F');
            if (!hex)
                throw new ArgumentException(
                    "Folder color must use #AARRGGBB format.");
        }

        return value;
    }
}

public enum FolderSessionLoadStatus
{
    Empty,
    LoadedV1,
    LoadedV2,
    Malformed,
    UnsupportedVersion,
    Invalid
}

public sealed record FolderSessionLoadResult(
    FolderSessionLoadStatus Status,
    FolderSessionDocument? Document,
    string? Error)
{
    public bool Success =>
        Status is FolderSessionLoadStatus.Empty
            or FolderSessionLoadStatus.LoadedV1
            or FolderSessionLoadStatus.LoadedV2;
}

public static class FolderSessionDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string Save(FolderSessionDocument document)
    {
        var normalized =
            FolderSessionDocumentRules.NormalizeAndValidate(document);
        return JsonSerializer.Serialize(normalized, Options);
    }

    public static FolderSessionLoadResult Load(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(
                FolderSessionLoadStatus.Empty,
                FolderSessionDocument.Empty,
                null);
        }

        int schema;
        try
        {
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty(
                    "schemaVersion",
                    out var version)
                || !version.TryGetInt32(out schema))
            {
                return new(
                    FolderSessionLoadStatus.Malformed,
                    null,
                    "Missing integer schemaVersion.");
            }
        }
        catch (JsonException ex)
        {
            return new(
                FolderSessionLoadStatus.Malformed,
                null,
                ex.Message);
        }

        if (schema == FolderDocument.CurrentSchemaVersion)
        {
            var legacy = FolderDocumentCodec.Load(text);
            if (!legacy.Success)
            {
                return new(
                    legacy.Status switch
                    {
                        FolderDocumentLoadStatus.Malformed =>
                            FolderSessionLoadStatus.Malformed,
                        FolderDocumentLoadStatus.UnsupportedVersion =>
                            FolderSessionLoadStatus.UnsupportedVersion,
                        _ => FolderSessionLoadStatus.Invalid
                    },
                    null,
                    legacy.Error);
            }

            return new(
                legacy.Status == FolderDocumentLoadStatus.Empty
                    ? FolderSessionLoadStatus.Empty
                    : FolderSessionLoadStatus.LoadedV1,
                new FolderSessionDocument
                {
                    Core = legacy.Document ?? FolderDocument.Empty
                },
                null);
        }

        if (schema != FolderSessionDocument.CurrentSchemaVersion)
        {
            return new(
                FolderSessionLoadStatus.UnsupportedVersion,
                null,
                $"Unsupported session schema version {schema}.");
        }

        FolderSessionDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<FolderSessionDocument>(
                text,
                Options);
        }
        catch (JsonException ex)
        {
            return new(
                FolderSessionLoadStatus.Malformed,
                null,
                ex.Message);
        }

        if (document is null)
        {
            return new(
                FolderSessionLoadStatus.Malformed,
                null,
                "Session document was null.");
        }

        try
        {
            return new(
                FolderSessionLoadStatus.LoadedV2,
                FolderSessionDocumentRules.NormalizeAndValidate(document),
                null);
        }
        catch (Exception ex)
            when (ex is ArgumentException or InvalidOperationException)
        {
            return new(
                FolderSessionLoadStatus.Invalid,
                null,
                ex.Message);
        }
    }
}
