using Ymm4NoHarmonyFolderRanges;

namespace Ymm4NoHarmonyNavigation;

/// <summary>Candidate view policy only; folder identities/ranges and host items never change.</summary>
public static class HiddenDestinationPolicy
{
    public static IReadOnlyList<FolderRange> RemainingCollapsed(
        IEnumerable<FolderRange> collapsed, int targetLayer, int maxLayer)
    {
        ArgumentNullException.ThrowIfNull(collapsed);
        if (maxLayer < 0 || targetLayer < 0 || targetLayer > maxLayer)
            throw new ArgumentOutOfRangeException(nameof(targetLayer));
        var snapshot = collapsed.ToArray();
        FolderRangeTracker.Validate(snapshot);
        if (snapshot.Any(x => x.End > maxLayer))
            throw new ArgumentException("Collapsed range exceeds the known layout.", nameof(collapsed));

        // A folder's owner remains visible. Reveal only ancestors hiding the target;
        // targeting a nested owner opens its parent but not the nested folder itself.
        return Array.AsReadOnly(snapshot
            .Where(x => !(x.Start < targetLayer && targetLayer <= x.End))
            .ToArray());
    }
}
