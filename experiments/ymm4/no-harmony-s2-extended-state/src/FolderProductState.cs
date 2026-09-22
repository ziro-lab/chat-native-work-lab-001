using Ymm4NoHarmonyPersistence;

namespace Ymm4NoHarmonyProductState;

public sealed record FolderOptionState
{
    public required string TimelineKey { get; init; }
    public required Guid FolderId { get; init; }
    public string? Color { get; init; }
    public bool Hidden { get; init; }
}

public sealed record LayerVisibilityRestore
{
    public required int Layer { get; init; }
    public required bool Visible { get; init; }
}

public sealed record TimelineVisibilityRestore
{
    public required string TimelineKey { get; init; }
    public IReadOnlyList<LayerVisibilityRestore> Layers { get; init; } = [];
}

public sealed record FolderProductState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public FolderDocument Core { get; init; } = FolderDocument.Empty;
    public IReadOnlyList<FolderOptionState> FolderOptions { get; init; } = [];
    public IReadOnlyList<TimelineVisibilityRestore> VisibilityRestore { get; init; } = [];

    public static FolderProductState Empty { get; } = new();
}

public static class FolderProductStateRules
{
    public static FolderProductState NormalizeAndValidate(FolderProductState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != FolderProductState.CurrentSchemaVersion)
            throw new ArgumentException(
                $"Unsupported product schema version {state.SchemaVersion}.",
                nameof(state));

        var core = FolderDocumentRules.NormalizeAndValidate(
            state.Core ?? throw new ArgumentException("Core must not be null.", nameof(state)));

        var folders = core.Timelines
            .SelectMany(t => t.Folders.Select(f => (t.TimelineKey, f.Id)))
            .ToHashSet();

        if (state.FolderOptions is null)
            throw new ArgumentException("FolderOptions must not be null.", nameof(state));

        var optionKeys = new HashSet<(string TimelineKey, Guid FolderId)>();
        var options = new List<FolderOptionState>();

        foreach (var option in state.FolderOptions)
        {
            if (option is null)
                throw new ArgumentException("Folder option must not be null.", nameof(state));

            var key = option.TimelineKey?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Folder option TimelineKey must not be empty.", nameof(state));
            if (option.FolderId == Guid.Empty)
                throw new ArgumentException("Folder option FolderId must not be empty.", nameof(state));
            if (!folders.Contains((key, option.FolderId)))
                throw new ArgumentException(
                    $"Folder option references missing folder {key}/{option.FolderId}.",
                    nameof(state));
            if (!optionKeys.Add((key, option.FolderId)))
                throw new ArgumentException(
                    $"Duplicate folder option {key}/{option.FolderId}.",
                    nameof(state));

            var color = string.IsNullOrWhiteSpace(option.Color)
                ? null
                : option.Color.Trim();
            if (color is { Length: > 64 })
                throw new ArgumentException("Folder color text is too long.", nameof(state));

            // Default-only entries carry no information and are dropped.
            if (!option.Hidden && color is null)
                continue;

            options.Add(option with
            {
                TimelineKey = key,
                Color = color
            });
        }

        if (state.VisibilityRestore is null)
            throw new ArgumentException("VisibilityRestore must not be null.", nameof(state));

        var timelineKeys = core.Timelines
            .Select(x => x.TimelineKey)
            .ToHashSet(StringComparer.Ordinal);
        var restoreTimelineKeys = new HashSet<string>(StringComparer.Ordinal);
        var restores = new List<TimelineVisibilityRestore>();

        foreach (var restore in state.VisibilityRestore)
        {
            if (restore is null)
                throw new ArgumentException("Visibility restore entry must not be null.", nameof(state));

            var key = restore.TimelineKey?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Visibility restore TimelineKey must not be empty.", nameof(state));
            if (!timelineKeys.Contains(key))
                throw new ArgumentException(
                    $"Visibility restore references missing timeline '{key}'.",
                    nameof(state));
            if (!restoreTimelineKeys.Add(key))
                throw new ArgumentException(
                    $"Duplicate visibility restore timeline '{key}'.",
                    nameof(state));
            if (restore.Layers is null)
                throw new ArgumentException(
                    $"Visibility restore layers for '{key}' must not be null.",
                    nameof(state));

            var seenLayers = new HashSet<int>();
            var layers = restore.Layers
                .Select(x => x ?? throw new ArgumentException(
                    $"Visibility restore layer in '{key}' must not be null.",
                    nameof(state)))
                .Select(x =>
                {
                    if (x.Layer < 0)
                        throw new ArgumentException(
                            $"Visibility restore layer must be non-negative: {x.Layer}.",
                            nameof(state));
                    if (!seenLayers.Add(x.Layer))
                        throw new ArgumentException(
                            $"Duplicate visibility restore layer {key}/L{x.Layer}.",
                            nameof(state));
                    return x;
                })
                .OrderBy(x => x.Layer)
                .ToArray();

            if (layers.Length == 0)
                continue;

            restores.Add(restore with
            {
                TimelineKey = key,
                Layers = Array.AsReadOnly(layers)
            });
        }

        return state with
        {
            Core = core,
            FolderOptions = Array.AsReadOnly(options
                .OrderBy(x => x.TimelineKey, StringComparer.Ordinal)
                .ThenBy(x => x.FolderId)
                .ToArray()),
            VisibilityRestore = Array.AsReadOnly(restores
                .OrderBy(x => x.TimelineKey, StringComparer.Ordinal)
                .ToArray())
        };
    }

    public static FolderOptionState? FindOption(
        FolderProductState state,
        string timelineKey,
        Guid folderId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);
        return state.FolderOptions.FirstOrDefault(
            x => string.Equals(
                    x.TimelineKey,
                    timelineKey.Trim(),
                    StringComparison.Ordinal)
                && x.FolderId == folderId);
    }

    public static IReadOnlyDictionary<int, bool> RestoreMap(
        FolderProductState state,
        string timelineKey)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(timelineKey);

        return (state.VisibilityRestore.FirstOrDefault(
                x => string.Equals(
                    x.TimelineKey,
                    timelineKey.Trim(),
                    StringComparison.Ordinal))
            ?.Layers ?? [])
            .ToDictionary(x => x.Layer, x => x.Visible);
    }

    public static FolderProductState ReplaceCore(
        FolderProductState state,
        FolderDocument core)
    {
        ArgumentNullException.ThrowIfNull(state);
        var normalizedCore = FolderDocumentRules.NormalizeAndValidate(core);
        var alive = normalizedCore.Timelines
            .SelectMany(t => t.Folders.Select(f => (t.TimelineKey, f.Id)))
            .ToHashSet();
        var timelineKeys = normalizedCore.Timelines
            .Select(t => t.TimelineKey)
            .ToHashSet(StringComparer.Ordinal);

        return NormalizeAndValidate(state with
        {
            Core = normalizedCore,
            FolderOptions = state.FolderOptions
                .Where(x => alive.Contains((x.TimelineKey, x.FolderId)))
                .ToArray(),
            VisibilityRestore = state.VisibilityRestore
                .Where(x => timelineKeys.Contains(x.TimelineKey))
                .ToArray()
        });
    }

    public static FolderProductState SetFolderOption(
        FolderProductState state,
        string timelineKey,
        Guid folderId,
        string? color = null,
        bool? hidden = null)
    {
        var normalized = NormalizeAndValidate(state);
        var key = timelineKey.Trim();
        var existing = FindOption(normalized, key, folderId);
        var next = new FolderOptionState
        {
            TimelineKey = key,
            FolderId = folderId,
            Color = color ?? existing?.Color,
            Hidden = hidden ?? existing?.Hidden ?? false
        };

        var options = normalized.FolderOptions
            .Where(x => !(x.TimelineKey == key && x.FolderId == folderId))
            .Append(next)
            .ToArray();

        return NormalizeAndValidate(normalized with { FolderOptions = options });
    }

    public static FolderProductState ReplaceRestoreMap(
        FolderProductState state,
        string timelineKey,
        IReadOnlyDictionary<int, bool> restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        var normalized = NormalizeAndValidate(state);
        var key = timelineKey.Trim();

        var entries = normalized.VisibilityRestore
            .Where(x => !string.Equals(x.TimelineKey, key, StringComparison.Ordinal))
            .ToList();

        if (restore.Count > 0)
        {
            entries.Add(new TimelineVisibilityRestore
            {
                TimelineKey = key,
                Layers = restore
                    .OrderBy(x => x.Key)
                    .Select(x => new LayerVisibilityRestore
                    {
                        Layer = x.Key,
                        Visible = x.Value
                    })
                    .ToArray()
            });
        }

        return NormalizeAndValidate(normalized with
        {
            VisibilityRestore = entries
        });
    }

    public static IReadOnlySet<int> HiddenLayers(
        FolderProductState state,
        string timelineKey)
    {
        var normalized = NormalizeAndValidate(state);
        var timeline = FolderDocumentRules.FindTimeline(
            normalized.Core,
            timelineKey);

        if (timeline is null)
            return new HashSet<int>();

        var hiddenIds = normalized.FolderOptions
            .Where(x =>
                x.TimelineKey == timelineKey.Trim()
                && x.Hidden)
            .Select(x => x.FolderId)
            .ToHashSet();

        return timeline.Folders
            .Where(x => hiddenIds.Contains(x.Id))
            .SelectMany(x => Enumerable.Range(x.Start, x.End - x.Start + 1))
            .ToHashSet();
    }
}
