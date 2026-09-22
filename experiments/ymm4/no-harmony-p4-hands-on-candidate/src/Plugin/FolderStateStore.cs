using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyState;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed class FolderStateStore
{
    private readonly FolderPersistenceSession session = new();

    internal static FolderStateStore Shared { get; } = new();

    internal event EventHandler? Changed;

    internal FolderSessionDocument State => session.State;
    internal FolderDocument Document => session.Document;
    internal bool IsRecoveryBlocked => session.IsRecoveryBlocked;
    internal FolderSessionLoadStatus LastLoadStatus => session.LastLoadStatus;
    internal string? LastError => session.LastError;

    internal void LoadRaw(string? savedState)
    {
        session.Load(savedState);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal string? SaveRaw() => session.Save();

    internal void ReplaceDocument(FolderDocument document)
    {
        session.ReplaceDocument(document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ReplaceState(FolderSessionDocument state)
    {
        session.ReplaceState(state);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ResetDocument()
    {
        session.Reset();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
