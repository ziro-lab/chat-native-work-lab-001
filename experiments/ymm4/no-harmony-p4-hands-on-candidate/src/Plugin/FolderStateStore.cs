using Ymm4NoHarmonyPersistence;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed class FolderStateStore
{
    private readonly FolderPersistenceSession session = new();

    internal static FolderStateStore Shared { get; } = new();

    internal event EventHandler? Changed;

    internal FolderDocument Document => session.Document;
    internal bool IsRecoveryBlocked => session.IsRecoveryBlocked;
    internal FolderDocumentLoadStatus LastLoadStatus => session.LastLoadStatus;
    internal string? LastError => session.LastError;

    internal void LoadRaw(string? savedState)
    {
        session.Load(savedState);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal string? SaveRaw() => session.Save();

    internal void ReplaceDocument(FolderDocument document)
    {
        session.Replace(document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ResetDocument()
    {
        session.Reset();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
