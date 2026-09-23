using Ymm4NoHarmonyVisualSummary;

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

static (
    Func<int, bool> Hidden,
    Func<int, int> Owner,
    Func<int, int> Row)
Layout(int maxLayer, params (int Start, int End)[] spans)
{
    var hidden = new bool[maxLayer + 1];
    foreach (var (start, end) in spans)
    {
        for (var layer = start + 1; layer <= end; layer++)
            hidden[layer] = true;
    }

    int Owner(int layer)
    {
        if (layer < 0 || layer > maxLayer)
            throw new ArgumentOutOfRangeException(nameof(layer));
        if (!hidden[layer])
            return layer;

        var owners = spans
            .Where(span =>
                span.Start < layer
                && layer <= span.End
                && !hidden[span.Start])
            .Select(span => span.Start)
            .ToArray();

        if (owners.Length == 0)
            throw new InvalidOperationException(
                $"No visible owner for L{layer}.");

        return owners.Min();
    }

    var visible = Enumerable.Range(0, maxLayer + 1)
        .Where(layer => !hidden[layer])
        .ToArray();
    var row = visible
        .Select((layer, index) => (layer, index))
        .ToDictionary(x => x.layer, x => x.index);

    return (
        layer => hidden[layer],
        Owner,
        layer => row[Owner(layer)]);
}

var simple = Layout(
    12,
    (2, 5),
    (7, 9));

var items = new[]
{
    new VisualItemSpan("owner-a", 2, 10, 20),
    new VisualItemSpan("a-3", 3, 15, 30),
    new VisualItemSpan("a-5", 5, 80, 10),
    new VisualItemSpan("between", 6, 20, 5),
    new VisualItemSpan("b-8", 8, 25, 40),
    new VisualItemSpan("zero", 4, 50, 0)
};

var bands = VisualSummaryRules.BuildTimingBands(
    items,
    simple.Hidden,
    simple.Owner,
    simple.Row);

Check("timing_hidden_only",
    bands.Select(x => x.Key)
        .SequenceEqual(new[] { "a-3", "a-5", "b-8" }));

Check("timing_owner_a",
    bands.Where(x => x.OwnerLayer == 2)
        .All(x => x.VisualRow == simple.Row(2))
    && bands.Single(x => x.Key == "a-3") is
    {
        SourceLayer: 3,
        Frame: 15,
        Length: 30
    });

Check("timing_owner_b",
    bands.Single(x => x.Key == "b-8") is
    {
        OwnerLayer: 7,
        SourceLayer: 8,
        Frame: 25,
        Length: 40
    });

Check("visible_owner_item_not_duplicated",
    bands.All(x => x.Key != "owner-a"));

Check("visible_between_item_not_duplicated",
    bands.All(x => x.Key != "between"));

Check("zero_length_ignored",
    bands.All(x => x.Key != "zero"));

var nestedCollapsed = Layout(
    12,
    (2, 8),
    (4, 6));

var nestedBands = VisualSummaryRules.BuildTimingBands(
    [
        new VisualItemSpan("nested-4", 4, 1, 5),
        new VisualItemSpan("nested-6", 6, 8, 7),
        new VisualItemSpan("outer-8", 8, 20, 4)
    ],
    nestedCollapsed.Hidden,
    nestedCollapsed.Owner,
    nestedCollapsed.Row);

Check("nested_collapsed_uses_outer_visible_owner",
    nestedBands.All(x =>
        x.OwnerLayer == 2
        && x.VisualRow == nestedCollapsed.Row(2)));

var innerOnly = Layout(
    12,
    (4, 6));

var innerBands = VisualSummaryRules.BuildTimingBands(
    [
        new VisualItemSpan("inner-5", 5, 10, 10),
        new VisualItemSpan("inner-6", 6, 30, 12)
    ],
    innerOnly.Hidden,
    innerOnly.Owner,
    innerOnly.Row);

Check("inner_only_uses_inner_owner",
    innerBands.All(x =>
        x.OwnerLayer == 4
        && x.VisualRow == innerOnly.Row(4)));

var groupSimple = VisualSummaryRules.BuildGroupSegments(
    groupLayer: 1,
    groupRange: 8,
    simple.Owner,
    simple.Row);

Check("group_folded_range_coalesces",
    groupSimple.Count == 1);

Check("group_folded_visual_rows",
    groupSimple[0].VisualStartRow == simple.Row(1)
    && groupSimple[0].VisualEndRow == simple.Row(9)
    && groupSimple[0].LogicalStart == 1
    && groupSimple[0].LogicalEnd == 9);

var groupInsideCollapsed = VisualSummaryRules.BuildGroupSegments(
    groupLayer: 3,
    groupRange: 2,
    simple.Owner,
    simple.Row);

Check("group_inside_collapsed_maps_to_owner_row",
    groupInsideCollapsed.Count == 1
    && groupInsideCollapsed[0].VisualStartRow == simple.Row(2)
    && groupInsideCollapsed[0].VisualEndRow == simple.Row(2)
    && groupInsideCollapsed[0].LogicalStart == 3
    && groupInsideCollapsed[0].LogicalEnd == 5);

var groupAcrossNested = VisualSummaryRules.BuildGroupSegments(
    groupLayer: 1,
    groupRange: 9,
    nestedCollapsed.Owner,
    nestedCollapsed.Row);

Check("group_nested_no_duplicate_rows",
    groupAcrossNested.Count == 1
    && groupAcrossNested[0].VisualRowCount
        == nestedCollapsed.Row(10) - nestedCollapsed.Row(1) + 1);

Reject<ArgumentOutOfRangeException>(
    "negative_group_layer_rejected",
    () => VisualSummaryRules.BuildGroupSegments(
        -1,
        2,
        simple.Owner,
        simple.Row));

Reject<ArgumentOutOfRangeException>(
    "zero_group_range_rejected",
    () => VisualSummaryRules.BuildGroupSegments(
        1,
        0,
        simple.Owner,
        simple.Row));

Reject<ArgumentOutOfRangeException>(
    "negative_item_layer_rejected",
    () => VisualSummaryRules.BuildTimingBands(
        [new VisualItemSpan("bad", -1, 0, 1)],
        _ => true,
        _ => 0,
        _ => 0));

Reject<ArgumentOutOfRangeException>(
    "negative_frame_rejected",
    () => VisualSummaryRules.BuildTimingBands(
        [new VisualItemSpan("bad", 1, -1, 1)],
        _ => true,
        _ => 0,
        _ => 0));

Reject<InvalidOperationException>(
    "hidden_without_owner_rejected",
    () => VisualSummaryRules.BuildTimingBands(
        [new VisualItemSpan("bad", 1, 0, 1)],
        _ => true,
        layer => layer,
        _ => 0));

Console.WriteLine(
    $"status=PASS_S5_VISUAL_SUMMARY\nassertion_count={count}");
