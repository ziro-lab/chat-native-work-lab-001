using System.Text.Json;

namespace Ymm4NoHarmonyPersistence;

public enum FolderDocumentLoadStatus
{
    Loaded,
    Empty,
    Malformed,
    UnsupportedVersion,
    Invalid
}

public sealed record FolderDocumentLoadResult(
    FolderDocumentLoadStatus Status,
    FolderDocument? Document,
    string? Error)
{
    public bool Success => Status is FolderDocumentLoadStatus.Loaded or FolderDocumentLoadStatus.Empty;
}

public static class FolderDocumentCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string Save(FolderDocument document)
    {
        var normalized = FolderDocumentRules.NormalizeAndValidate(document);
        return JsonSerializer.Serialize(normalized, Options);
    }

    public static FolderDocumentLoadResult Load(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(FolderDocumentLoadStatus.Empty, FolderDocument.Empty, null);

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
            return new(FolderDocumentLoadStatus.Malformed, null, ex.Message);
        }

        if (schema != FolderDocument.CurrentSchemaVersion)
        {
            return new(
                FolderDocumentLoadStatus.UnsupportedVersion,
                null,
                $"Unsupported schema version {schema}.");
        }

        FolderDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<FolderDocument>(text, Options);
        }
        catch (JsonException ex)
        {
            return new(FolderDocumentLoadStatus.Malformed, null, ex.Message);
        }

        if (document is null)
            return new(FolderDocumentLoadStatus.Malformed, null, "Document was null.");

        try
        {
            var normalized = FolderDocumentRules.NormalizeAndValidate(document);
            return new(FolderDocumentLoadStatus.Loaded, normalized, null);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new(FolderDocumentLoadStatus.Invalid, null, ex.Message);
        }
    }
}
