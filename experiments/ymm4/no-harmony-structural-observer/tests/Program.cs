using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyStructuralObserver;

internal static class Program
{
    private static int assertions;
    private static readonly Guid Folder = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static void Equal<T>(string name, T expected, T actual)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}");
        Console.WriteLine($"PASS {name}");
    }

    private static void True(string name, bool value)
    {
        assertions++;
        if (!value)
            throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {name}");
    }

    private static string Edits(StructuralDetection detection) =>
        string.Join(
            "|",
            detection.Edits.Select(
                e => e switch
                {
                    InsertLayers x => $"I:{x.Position}:{x.Count}",
                    DeleteLayers x => $"D:{x.Position}:{x.Count}",
                    SwapAdjacentLayers x => $"S:{x.FirstLayer}",
                    _ => e.GetType().Name
                }));

    private static LayerPair[] DenseIdentity(int max) =>
        Enumerable.Range(0, max + 1)
            .Select(x => new LayerPair(x, x))
            .ToArray();

    private static StructuralDetection Detect(params LayerPair[] pairs) =>
        StructuralDeltaDetector.Detect(pairs);

    public static int Main()
    {
        try
        {
            InsertCases();
            DeleteCases();
            SwapCases();
            AmbiguityCases();
            TrackerBoundaryCases();

            Console.WriteLine("status=PASS_STRUCTURAL_DELTA_DETECTOR");
            Console.WriteLine($"assertion_count={assertions}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.WriteLine("status=FAIL_STRUCTURAL_DELTA_DETECTOR");
            Console.WriteLine($"assertion_count={assertions}");
            return 1;
        }
    }

    private static void InsertCases()
    {
        var dense = DenseIdentity(6).Select(
            p => p.OldLayer >= 3
                ? new LayerPair(p.OldLayer, p.NewLayer + 1)
                : p).ToArray();

        var result = Detect(dense);
        Equal("insert_dense_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("insert_dense_edit", "I:3:1", Edits(result));

        var sparse = new[]
        {
            new LayerPair(1, 1),
            new LayerPair(5, 6)
        };

        result = StructuralDeltaDetector.Detect(sparse);
        Equal("insert_sparse_without_hint_is_ambiguous", StructuralDetectionStatus.Ambiguous, result.Status);

        result = StructuralDeltaDetector.Detect(
            sparse,
            insertedHints: new HashSet<int> { 3 });

        Equal("insert_sparse_hint_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("insert_sparse_hint_position", "I:3:1", Edits(result));

        var multi = DenseIdentity(6).Select(
            p => p.OldLayer >= 2
                ? new LayerPair(p.OldLayer, p.NewLayer + 2)
                : p).ToArray();

        result = StructuralDeltaDetector.Detect(
            multi,
            insertedHints: new HashSet<int> { 2, 3 });

        Equal("insert_multi_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("insert_multi_edit", "I:2:2", Edits(result));
    }

    private static void DeleteCases()
    {
        var dense = new[]
        {
            new LayerPair(0, 0),
            new LayerPair(1, 1),
            new LayerPair(2, 2),
            new LayerPair(4, 3),
            new LayerPair(5, 4),
            new LayerPair(6, 5)
        };

        var result = StructuralDeltaDetector.Detect(
            dense,
            vanishedLayers: new HashSet<int> { 3 });

        Equal("delete_dense_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("delete_dense_edit", "D:3:1", Edits(result));

        var sparse = new[]
        {
            new LayerPair(1, 1),
            new LayerPair(5, 4)
        };

        result = StructuralDeltaDetector.Detect(sparse);
        Equal("delete_sparse_without_hint_is_ambiguous", StructuralDetectionStatus.Ambiguous, result.Status);

        result = StructuralDeltaDetector.Detect(
            sparse,
            vanishedLayers: new HashSet<int> { 3 });

        Equal("delete_sparse_vanished_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("delete_sparse_vanished_position", "D:3:1", Edits(result));

        var multi = new[]
        {
            new LayerPair(0, 0),
            new LayerPair(1, 1),
            new LayerPair(2, 2),
            new LayerPair(5, 3),
            new LayerPair(6, 4)
        };

        result = StructuralDeltaDetector.Detect(
            multi,
            vanishedLayers: new HashSet<int> { 3, 4 });

        Equal("delete_multi_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("delete_multi_merged", "D:3:2", Edits(result));
    }

    private static void SwapCases()
    {
        var pairs = DenseIdentity(7)
            .Select(
                p => p.OldLayer == 3
                    ? new LayerPair(3, 4)
                    : p.OldLayer == 4
                        ? new LayerPair(4, 3)
                        : p)
            .ToArray();

        var result = Detect(pairs);
        Equal("swap_status", StructuralDetectionStatus.Exact, result.Status);
        Equal("swap_edit", "S:3", Edits(result));

        var duplicateItemsSameLayer = new[]
        {
            new LayerPair(2, 2),
            new LayerPair(3, 4),
            new LayerPair(3, 4),
            new LayerPair(4, 3),
            new LayerPair(4, 3),
            new LayerPair(5, 5)
        };

        result = Detect(duplicateItemsSameLayer);
        Equal("swap_multiple_items_same_rows", StructuralDetectionStatus.Exact, result.Status);
        Equal("swap_multiple_items_edit", "S:3", Edits(result));
    }

    private static void AmbiguityCases()
    {
        var inconsistentSameLayer = new[]
        {
            new LayerPair(3, 4),
            new LayerPair(3, 5)
        };

        var result = Detect(inconsistentSameLayer);
        Equal("same_old_layer_split_is_ambiguous", StructuralDetectionStatus.Ambiguous, result.Status);

        var arbitraryMove = new[]
        {
            new LayerPair(1, 1),
            new LayerPair(2, 5),
            new LayerPair(3, 3),
            new LayerPair(4, 4)
        };

        result = Detect(arbitraryMove);
        Equal("individual_move_not_structural", StructuralDetectionStatus.Ambiguous, result.Status);

        result = Detect(DenseIdentity(5));
        Equal("identity_is_none", StructuralDetectionStatus.None, result.Status);
        Equal("identity_has_no_edits", 0, result.Edits.Count);

        result = StructuralDeltaDetector.Detect(Array.Empty<LayerPair>());
        Equal("empty_observation_is_none", StructuralDetectionStatus.None, result.Status);
    }

    private static void TrackerBoundaryCases()
    {
        var ranges = new[]
        {
            new FolderRange(Folder, 2, 6)
        };

        var insertPairs = DenseIdentity(7)
            .Select(
                p => p.OldLayer >= 4
                    ? new LayerPair(p.OldLayer, p.NewLayer + 1)
                    : p)
            .ToArray();

        var insert = Detect(insertPairs);
        True("tracker_insert_detection_exact", insert.Status == StructuralDetectionStatus.Exact);
        var afterInsert = FolderRangeTracker.Apply(ranges, insert.Edits.Single());
        Equal("tracker_insert_applies_frozen_policy", 7, afterInsert.Ranges.Single().End);

        var deletePairs = new[]
        {
            new LayerPair(0, 0),
            new LayerPair(1, 1),
            new LayerPair(3, 2),
            new LayerPair(4, 3),
            new LayerPair(5, 4),
            new LayerPair(6, 5)
        };

        var delete = StructuralDeltaDetector.Detect(
            deletePairs,
            vanishedLayers: new HashSet<int> { 2 });

        True("tracker_delete_detection_exact", delete.Status == StructuralDetectionStatus.Exact);
        var afterDelete = FolderRangeTracker.Apply(ranges, delete.Edits.Single());
        Equal("tracker_delete_head_policy", 2, afterDelete.Ranges.Single().Start);
        Equal("tracker_delete_shrinks", 5, afterDelete.Ranges.Single().End);

        var swapPairs = DenseIdentity(7)
            .Select(
                p => p.OldLayer == 2
                    ? new LayerPair(2, 3)
                    : p.OldLayer == 3
                        ? new LayerPair(3, 2)
                        : p)
            .ToArray();

        var swap = Detect(swapPairs);
        True("tracker_swap_detection_exact", swap.Status == StructuralDetectionStatus.Exact);
        var afterSwap = FolderRangeTracker.Apply(ranges, swap.Edits.Single());
        Equal("tracker_swap_keeps_positional_range_start", 2, afterSwap.Ranges.Single().Start);
        Equal("tracker_swap_keeps_positional_range_end", 6, afterSwap.Ranges.Single().End);
    }
}
