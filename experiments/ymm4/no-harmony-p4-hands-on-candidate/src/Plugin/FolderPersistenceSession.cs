using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyState;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Runtime wrapper for the frozen P3 raw-preservation policy and the S2 outer
/// session document. FolderDocument v1 remains the core structural model; new
/// visual metadata lives only in the outer v2 document.
/// </summary>
internal sealed class FolderPersistenceSession
{
    internal FolderSessionDocument State { get; private set; } =
        FolderSessionDocument.Empty;

    internal FolderDocument Document => State.Core;

    internal FolderSessionLoadStatus LastLoadStatus { get; private set; } =
        FolderSessionLoadStatus.Empty;

    internal string? LastError { get; private set; }
    internal string? PreservedUnreadableState { get; private set; }
    internal bool IsRecoveryBlocked => PreservedUnreadableState is not null;

    internal FolderSessionLoadResult Load(string? savedState)
    {
        var result = FolderSessionDocumentCodec.Load(savedState);
        LastLoadStatus = result.Status;
        LastError = result.Error;

        if (result.Success)
        {
            State = FolderSessionDocumentRules.NormalizeAndValidate(
                result.Document ?? FolderSessionDocument.Empty);
            PreservedUnreadableState = null;
        }
        else
        {
            State = FolderSessionDocument.Empty;
            PreservedUnreadableState = savedState;
        }

        return result;
    }

    internal string? Save()
    {
        if (PreservedUnreadableState is not null)
            return PreservedUnreadableState;

        var normalized =
            FolderSessionDocumentRules.NormalizeAndValidate(State);

        var empty =
            normalized.Core.Timelines.Count == 0
            && normalized.FolderOptions.Count == 0
            && normalized.VisibilityRestore.Count == 0;

        return empty
            ? null
            : FolderSessionDocumentCodec.Save(normalized);
    }

    internal void ReplaceDocument(FolderDocument document)
    {
        State = FolderSessionDocumentRules.ReplaceCore(State, document);
        ClearRecovery(FolderSessionLoadStatus.LoadedV2);
    }

    internal void ReplaceState(FolderSessionDocument state)
    {
        State = FolderSessionDocumentRules.NormalizeAndValidate(state);
        ClearRecovery(FolderSessionLoadStatus.LoadedV2);
    }

    internal void Reset()
    {
        State = FolderSessionDocument.Empty;
        PreservedUnreadableState = null;
        LastLoadStatus = FolderSessionLoadStatus.Empty;
        LastError = null;
    }

    private void ClearRecovery(FolderSessionLoadStatus status)
    {
        PreservedUnreadableState = null;
        LastLoadStatus = status;
        LastError = null;
    }
}
