using Ymm4NoHarmonyFolderRanges;

internal static class Program
{
    private static int assertions;

    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid C = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static FolderRange R(Guid id, int start, int end) => new(id, start, end);

    private static string Show(IEnumerable<FolderRange> ranges) =>
        string.Join(
            "|",
            ranges
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End)
                .Select(x => $"{Short(x.Id)}:{x.Start}-{x.End}"));

    private static string Short(Guid id) =>
        id == A ? "A" : id == B ? "B" : id == C ? "C" : id.ToString("N")[..4];

    private static void Equal<TValue>(string name, TValue expected, TValue actual)
    {
        assertions++;
        if (!EqualityComparer<TValue>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"{name}: expected={expected}, actual={actual}");
        Console.WriteLine($"PASS {name}");
    }

    private static void True(string name, bool value)
    {
        assertions++;
        if (!value)
            throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {name}");
    }

    private static void Throws<TException>(string name, Action action)
        where TException : Exception
    {
        assertions++;
        try
        {
            action();
        }
        catch (TException)
        {
            Console.WriteLine($"PASS {name}");
            return;
        }

        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
    }

    public static int Main()
    {
        try
        {
            Validation();
            InsertCases();
            DeleteCases();
            SwapCases();
            RepeatedSequence();

            Console.WriteLine($"status=PASS_FOLDER_RANGE_TRACKER");
            Console.WriteLine($"assertion_count={assertions}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.WriteLine("status=FAIL_FOLDER_RANGE_TRACKER");
            Console.WriteLine($"assertion_count={assertions}");
            return 1;
        }
    }

    private static void Validation()
    {
        FolderRangeTracker.Validate([R(A, 2, 8), R(B, 4, 6), R(C, 10, 12)]);
        True("validation_nested_disjoint", true);

        Throws<ArgumentException>(
            "validation_crossing_rejected",
            () => FolderRangeTracker.Validate([R(A, 2, 6), R(B, 5, 8)]));

        Throws<ArgumentException>(
            "validation_duplicate_head_rejected",
            () => FolderRangeTracker.Validate([R(A, 2, 8), R(B, 2, 5)]));

        Throws<ArgumentException>(
            "validation_duplicate_id_rejected",
            () => FolderRangeTracker.Validate([R(A, 2, 4), R(A, 8, 9)]));
    }

    private static void InsertCases()
    {
        var source = new[] { R(A, 3, 6) };

        Equal(
            "insert_before_shifts_folder",
            "A:4-7",
            Show(FolderRangeTracker.Apply(source, new InsertLayers(2, 1)).Ranges));

        Equal(
            "insert_at_head_is_before_folder",
            "A:4-7",
            Show(FolderRangeTracker.Apply(source, new InsertLayers(3, 1)).Ranges));

        Equal(
            "insert_inside_expands_folder",
            "A:3-7",
            Show(FolderRangeTracker.Apply(source, new InsertLayers(4, 1)).Ranges));

        Equal(
            "insert_at_existing_end_expands_folder",
            "A:3-8",
            Show(FolderRangeTracker.Apply(source, new InsertLayers(6, 2)).Ranges));

        Equal(
            "insert_after_folder_unchanged",
            "A:3-6",
            Show(FolderRangeTracker.Apply(source, new InsertLayers(7, 1)).Ranges));

        var nested = new[] { R(A, 2, 8), R(B, 4, 6), R(C, 10, 12) };
        var plan = FolderRangeTracker.Apply(nested, new InsertLayers(5, 2));

        Equal(
            "insert_nested_and_later_disjoint",
            "A:2-10|B:4-8|C:12-14",
            Show(plan.Ranges));

        Equal("insert_map_before", 1, plan.MapLayer(1));
        Equal("insert_map_at", 7, plan.MapLayer(5));
        Equal("insert_map_after", 12, plan.MapLayer(10));
    }

    private static void DeleteCases()
    {
        var source = new[] { R(A, 3, 6) };

        Equal(
            "delete_before_shifts_folder",
            "A:2-5",
            Show(FolderRangeTracker.Apply(source, new DeleteLayers(1, 1)).Ranges));

        Equal(
            "delete_head_promotes_next_row",
            "A:3-5",
            Show(FolderRangeTracker.Apply(source, new DeleteLayers(3, 1)).Ranges));

        Equal(
            "delete_inside_shrinks_folder",
            "A:3-5",
            Show(FolderRangeTracker.Apply(source, new DeleteLayers(5, 1)).Ranges));

        Equal(
            "delete_end_shrinks_folder",
            "A:3-5",
            Show(FolderRangeTracker.Apply(source, new DeleteLayers(6, 1)).Ranges));

        Equal(
            "delete_after_folder_unchanged",
            "A:3-6",
            Show(FolderRangeTracker.Apply(source, new DeleteLayers(7, 1)).Ranges));

        Equal(
            "delete_whole_folder_prunes_it",
            "",
            Show(FolderRangeTracker.Apply(source, new DeleteLayers(3, 4)).Ranges));

        Equal(
            "delete_only_child_leaves_one_row_folder",
            "A:3-3",
            Show(FolderRangeTracker.Apply([R(A, 3, 4)], new DeleteLayers(4, 1)).Ranges));

        var nested = new[] { R(A, 2, 6), R(B, 3, 4) };
        Equal(
            "delete_outer_head_normalizes_nested_head",
            "A:2-5|B:3-3",
            Show(FolderRangeTracker.Apply(nested, new DeleteLayers(2, 1)).Ranges));

        Equal(
            "delete_two_heads_prunes_empty_nested",
            "A:2-4",
            Show(FolderRangeTracker.Apply(nested, new DeleteLayers(2, 2)).Ranges));

        var plan = FolderRangeTracker.Apply(
            [R(A, 2, 8), R(B, 4, 6), R(C, 10, 12)],
            new DeleteLayers(5, 2));

        Equal(
            "delete_nested_range_and_shift_later",
            "A:2-6|B:4-4|C:8-10",
            Show(plan.Ranges));

        Equal("delete_map_before", 4, plan.MapLayer(4));
        Equal("delete_map_removed_first", -1, plan.MapLayer(5));
        Equal("delete_map_removed_last", -1, plan.MapLayer(6));
        Equal("delete_map_after", 5, plan.MapLayer(7));
    }

    private static void SwapCases()
    {
        var source = new[] { R(A, 3, 6), R(B, 4, 5), R(C, 9, 10) };

        var inside = FolderRangeTracker.Apply(source, new SwapAdjacentLayers(4));
        Equal("swap_inside_keeps_ranges_positional", "A:3-6|B:4-5|C:9-10", Show(inside.Ranges));
        Equal("swap_inside_maps_first_down", 5, inside.MapLayer(4));
        Equal("swap_inside_maps_second_up", 4, inside.MapLayer(5));

        var head = FolderRangeTracker.Apply(source, new SwapAdjacentLayers(3));
        Equal("swap_folder_head_keeps_numeric_folder_head", "A:3-6|B:4-5|C:9-10", Show(head.Ranges));
        Equal("swap_old_owner_moves_to_child_position", 4, head.MapLayer(3));
        Equal("swap_old_child_moves_to_owner_position", 3, head.MapLayer(4));

        var boundary = FolderRangeTracker.Apply(source, new SwapAdjacentLayers(2));
        Equal("swap_across_outer_boundary_keeps_range", "A:3-6|B:4-5|C:9-10", Show(boundary.Ranges));
        Equal("swap_outside_row_enters_folder_position", 3, boundary.MapLayer(2));
        Equal("swap_old_owner_leaves_folder_position", 2, boundary.MapLayer(3));

        var after = FolderRangeTracker.Apply(source, new SwapAdjacentLayers(7));
        Equal("swap_outside_all_folders_unchanged", "A:3-6|B:4-5|C:9-10", Show(after.Ranges));
    }

    private static void RepeatedSequence()
    {
        IReadOnlyList<FolderRange> ranges =
        [
            R(A, 2, 7),
            R(B, 4, 5),
            R(C, 10, 12)
        ];

        ranges = FolderRangeTracker.Apply(ranges, new InsertLayers(4, 1)).Ranges;
        Equal("sequence_after_insert", "A:2-8|B:5-6|C:11-13", Show(ranges));

        ranges = FolderRangeTracker.Apply(ranges, new DeleteLayers(2, 1)).Ranges;
        Equal("sequence_after_delete", "A:2-7|B:4-5|C:10-12", Show(ranges));

        ranges = FolderRangeTracker.Apply(ranges, new SwapAdjacentLayers(4)).Ranges;
        Equal("sequence_after_swap", "A:2-7|B:4-5|C:10-12", Show(ranges));

        FolderRangeTracker.Validate(ranges);
        True("sequence_remains_valid", true);
    }
}
