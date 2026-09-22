using System.Text.Json;
using Ymm4NoHarmonyPersistence;

namespace Ymm4NoHarmonyProductState;

public sealed record FolderProductStateLoadResult(
    FolderDocumentLoadStatus Status,
    FolderProductState? State,
    string? Error,
    bool MigratedFromV1 = false)
{
    public bool Success =>
        Status is FolderDocumentLoadStatus.Loaded
            or FolderDocumentLoadStatus.Empty;
}

public static class FolderProductStateCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string Save(FolderProductState state)
    {
        var normalized = FolderProductStateRules.NormalizeAndValidate(state);
        return JsonSerializer.Serialize(normalized, Options);
    }

    public static FolderProductStateLoadResult Load(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(
                FolderDocumentLoadStatus.Empty,
                FolderProductState.Empty,
                null);
        }

        int schema;
        try
        {
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("schemaVersion", out var version)
                || !version.TryGetInt32(out schema))
            {
                return new(
                    FolderDocumentLoadStatus.Malformed,
                    null,
                    "Missing integer schemaVersion.");
            }
        }
        catch (JsonException ex)
        {
            return new(
                FolderDocumentLoadStatus.Malformed,
                null,
                ex.Message);
        }

        if (schema == FolderDocument.CurrentSchemaVersion)
        {
            var v1 = FolderDocumentCodec.Load(text);
            return v1.Success
                ? new(
                    v1.Status,
                    new FolderProductState
                    {
                        Core = v1.Document ?? FolderDocument.Empty
                    },
                    v1.Error,
                    MigratedFromV1: true)
                : new(v1.Status, null, v1.Error);
        }

        if (schema != FolderProductState.CurrentSchemaVersion)
        {
            return new(
                FolderDocumentLoadStatus.UnsupportedVersion,
                null,
                $"Unsupported product schema version {schema}.");
        }

        FolderProductState? state;
        try
        {
            state = JsonSerializer.Deserialize<FolderProductState>(
                text,
                Options);
        }
        catch (JsonException ex)
        {
            return new(
                FolderDocumentLoadStatus.Malformed,
                null,
                ex.Message);
        }

        if (state is null)
        {
            return new(
                FolderDocumentLoadStatus.Malformed,
                null,
                "Product state was null.");
        }

        try
        {
            return new(
                FolderDocumentLoadStatus.Loaded,
                FolderProductStateRules.NormalizeAndValidate(state),
                null);
        }
        catch (Exception ex)
            when (ex is ArgumentException or InvalidOperationException)
        {
            return new(
                FolderDocumentLoadStatus.Invalid,
                null,
                ex.Message);
        }
    }
}
