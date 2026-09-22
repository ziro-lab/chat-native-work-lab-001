using Ymm4NoHarmonyPersistence;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Runtime wrapper for the frozen P3 persistence policy.
/// Unreadable state is usable-as-empty but preserved byte-for-byte until the
/// user explicitly replaces or resets it.
/// </summary>
internal sealed class FolderPersistenceSession
{
    internal FolderDocument Document { get; private set; } = FolderDocument.Empty;
    internal FolderDocumentLoadStatus LastLoadStatus { get; private set; } = FolderDocumentLoadStatus.Empty;
    internal string? LastError { get; private set; }
    internal string? PreservedUnreadableState { get; private set; }
    internal bool IsRecoveryBlocked => PreservedUnreadableState is not null;

    internal FolderDocumentLoadResult Load(string? savedState)
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
            Document = FolderDocument.Empty;
            PreservedUnreadableState = savedState;
        }

        return result;
    }

    internal string? Save()
    {
        if (PreservedUnreadableState is not null)
            return PreservedUnreadableState;

        var normalized = FolderDocumentRules.NormalizeAndValidate(Document);
        return normalized.Timelines.Count == 0 ? null : FolderDocumentCodec.Save(normalized);
    }

    internal void Replace(FolderDocument document)
    {
        Document = FolderDocumentRules.NormalizeAndValidate(document);
        PreservedUnreadableState = null;
        LastLoadStatus = FolderDocumentLoadStatus.Loaded;
        LastError = null;
    }

    internal void Reset()
    {
        Document = FolderDocument.Empty;
        PreservedUnreadableState = null;
        LastLoadStatus = FolderDocumentLoadStatus.Empty;
        LastError = null;
    }
}
