using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;

namespace Ymm4NoHarmonyFolderLayoutProbe;

/// <summary>
/// Runtime wrapper for P3 raw-preservation plus the S2 outer product state.
/// v1 FolderDocument input is migrated in memory; successful saves use v2.
/// Unreadable/unknown raw state remains byte-for-byte preserved until an
/// explicit reset/replacement.
/// </summary>
internal sealed class FolderPersistenceSession
{
    internal FolderProductState ProductState { get; private set; } =
        FolderProductState.Empty;

    internal FolderDocument Document => ProductState.Core;
    internal FolderDocumentLoadStatus LastLoadStatus { get; private set; } =
        FolderDocumentLoadStatus.Empty;
    internal string? LastError { get; private set; }
    internal string? PreservedUnreadableState { get; private set; }
    internal bool IsRecoveryBlocked => PreservedUnreadableState is not null;
    internal bool LastLoadMigratedFromV1 { get; private set; }

    internal FolderProductStateLoadResult Load(string? savedState)
    {
        var result = FolderProductStateCodec.Load(savedState);
        LastLoadStatus = result.Status;
        LastError = result.Error;
        LastLoadMigratedFromV1 = result.MigratedFromV1;

        if (result.Success)
        {
            ProductState = result.State ?? FolderProductState.Empty;
            PreservedUnreadableState = null;
        }
        else
        {
            ProductState = FolderProductState.Empty;
            PreservedUnreadableState = savedState;
        }

        return result;
    }

    internal string? Save()
    {
        if (PreservedUnreadableState is not null)
            return PreservedUnreadableState;

        var normalized =
            FolderProductStateRules.NormalizeAndValidate(ProductState);

        var isEmpty =
            normalized.Core.Timelines.Count == 0
            && normalized.FolderOptions.Count == 0
            && normalized.VisibilityRestore.Count == 0;

        return isEmpty
            ? null
            : FolderProductStateCodec.Save(normalized);
    }

    internal void ReplaceCore(FolderDocument document)
    {
        ProductState = FolderProductStateRules.ReplaceCore(
            ProductState,
            document);
        MarkLoaded();
    }

    internal void ReplaceProductState(FolderProductState productState)
    {
        ProductState =
            FolderProductStateRules.NormalizeAndValidate(productState);
        MarkLoaded();
    }

    internal void Reset()
    {
        ProductState = FolderProductState.Empty;
        PreservedUnreadableState = null;
        LastLoadStatus = FolderDocumentLoadStatus.Empty;
        LastError = null;
        LastLoadMigratedFromV1 = false;
    }

    private void MarkLoaded()
    {
        PreservedUnreadableState = null;
        LastLoadStatus = FolderDocumentLoadStatus.Loaded;
        LastError = null;
        LastLoadMigratedFromV1 = false;
    }
}
