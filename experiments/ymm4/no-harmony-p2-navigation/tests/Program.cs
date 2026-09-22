using Ymm4NoHarmonyFolderRanges;
using Ymm4NoHarmonyNavigation;

var a = new FolderRange(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, 10);
var b = new FolderRange(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 2, 5);
var c = new FolderRange(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 16, 20);
var source = new[] { a, b, c };
var count = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{name}={pass}");
    if (!pass) throw new InvalidOperationException(name);
    count++;
}
void Reject<T>(string name, Action action) where T : Exception
{
    try { action(); }
    catch (T) { Check(name, true); return; }
    throw new InvalidOperationException("Did not reject: " + name);
}
IReadOnlyList<FolderRange> Plan(int layer) =>
    HiddenDestinationPolicy.RemainingCollapsed(source, layer, 60);
Check("nested_child_opens_only_blockers", Plan(4).SequenceEqual(new[] { c }));
Check("nested_owner_keeps_itself_closed", Plan(2).SequenceEqual(new[] { b, c }));
Check("outer_owner_keeps_everything_closed", Plan(1).SequenceEqual(source));
Check("end_is_inclusive", Plan(10).SequenceEqual(new[] { b, c }));
Check("outside_changes_nothing", Plan(12).SequenceEqual(source));
Check("second_folder_is_independent", Plan(20).SequenceEqual(new[] { a, b }));
Check("last_layer_allowed", Plan(60).SequenceEqual(source));
Check("empty_state", HiddenDestinationPolicy.RemainingCollapsed([], 0, 0).Count == 0);
Check("source_not_mutated", source.SequenceEqual(new[] { a, b, c }));
var once = Plan(4);
Check("idempotent", HiddenDestinationPolicy.RemainingCollapsed(once, 4, 60).SequenceEqual(once));
var singleton = a with { End = a.Start };
Check("singleton_owner_visible", HiddenDestinationPolicy.RemainingCollapsed([singleton], 1, 60).SequenceEqual(new[] { singleton }));
Reject<ArgumentOutOfRangeException>("negative_target", () => Plan(-1));
Reject<ArgumentOutOfRangeException>("target_beyond_layout", () => Plan(61));
Reject<ArgumentException>("range_beyond_layout", () => HiddenDestinationPolicy.RemainingCollapsed(source, 1, 5));
Reject<ArgumentException>("crossing_ranges", () => HiddenDestinationPolicy.RemainingCollapsed([a, b with { End = 11 }], 4, 60));
Reject<ArgumentException>("duplicate_identity", () => HiddenDestinationPolicy.RemainingCollapsed([a, a], 4, 60));
Reject<ArgumentNullException>("null_state", () => HiddenDestinationPolicy.RemainingCollapsed(null!, 4, 60));
Console.WriteLine($"status=PASS_P2_NAVIGATION_POLICY\nassertion_count={count}");
