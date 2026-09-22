using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed class FolderStateStore
{
    private readonly FolderPersistenceSession session = new();

    internal static FolderStateStore Shared { get; } = new();

    internal event EventHandler? Changed;

    internal FolderProductState ProductState => session.ProductState;
    internal FolderDocument Document => session.Document;
    internal bool IsRecoveryBlocked => session.IsRecoveryBlocked;
    internal FolderDocumentLoadStatus LastLoadStatus => session.LastLoadStatus;
    internal string? LastError => session.LastError;
    internal bool LastLoadMigratedFromV1 => session.LastLoadMigratedFromV1;

    internal void LoadRaw(string? savedState)
    {
        session.Load(savedState);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal string? SaveRaw() => session.Save();

    internal void ReplaceDocument(FolderDocument document)
    {
        session.ReplaceCore(document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ReplaceProductState(FolderProductState productState)
    {
        session.ReplaceProductState(productState);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ResetDocument()
    {
        session.Reset();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
