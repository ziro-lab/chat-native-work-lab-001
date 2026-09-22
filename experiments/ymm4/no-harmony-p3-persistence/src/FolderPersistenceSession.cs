namespace Ymm4NoHarmonyPersistence;

/// <summary>
/// Runtime persistence boundary for project-owned ToolState.
/// Unreadable state is quarantined: the runtime uses an empty document, while
/// Save() returns the exact original text until the user explicitly replaces
/// or resets the folder document. This prevents an older/broken plugin from
/// silently destroying recoverable project data during an unrelated save.
/// </summary>
public sealed class FolderPersistenceSession
{
    public FolderDocument Document { get; private set; } = FolderDocument.Empty;
    public FolderDocumentLoadStatus LastLoadStatus { get; private set; } = FolderDocumentLoadStatus.Empty;
    public string? LastError { get; private set; }
    public string? PreservedUnreadableState { get; private set; }
    public bool IsRecoveryBlocked => PreservedUnreadableState is not null;

    public FolderDocumentLoadResult Load(string? savedState)
    {
        var result = FolderDocumentCodec.Load(savedState);
        LastLoadStatus = result.Status;
        LastError = result.Error;

        if (result.Success)
        {
            Document = result.Document ?? FolderDocument.Empty;
            PreservedUnreadableState = null;
        }
        else
        {
            // Fail open visually, but fail closed for persistence: do not let an
            // empty runtime document overwrite bytes we could not understand.
            Document = FolderDocument.Empty;
            PreservedUnreadableState = savedState;
        }

        return result;
    }

    public string? Save()
    {
        if (PreservedUnreadableState is not null)
            return PreservedUnreadableState;

        var normalized = FolderDocumentRules.NormalizeAndValidate(Document);
        return normalized.Timelines.Count == 0
            ? null
            : FolderDocumentCodec.Save(normalized);
    }

    public void Replace(FolderDocument document)
    {
        Document = FolderDocumentRules.NormalizeAndValidate(document);
        PreservedUnreadableState = null;
        LastLoadStatus = FolderDocumentLoadStatus.Loaded;
        LastError = null;
    }

    public void Reset()
    {
        Document = FolderDocument.Empty;
        PreservedUnreadableState = null;
        LastLoadStatus = FolderDocumentLoadStatus.Empty;
        LastError = null;
    }
}
