using Ymm4NoHarmonyPanel;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;

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

Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
Guid C = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

var core = FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState
        {
            TimelineKey = "scene",
            Folders =
            [
                new PersistedFolder
                {
                    Id = A,
                    Start = 1,
                    End = 8,
                    Name = "Outer",
                    IsCollapsed = false
                },
                new PersistedFolder
                {
                    Id = B,
                    Start = 3,
                    End = 5,
                    Name = "Inner",
                    IsCollapsed = true
                },
                new PersistedFolder
                {
                    Id = C,
                    Start = 10,
                    End = 12,
                    Name = "Later",
                    IsCollapsed = false
                }
            ]
        }
    ]
});

var product = FolderProductStateRules.ReplaceCore(
    FolderProductState.Empty,
    core);
product = FolderProductStateRules.SetFolderHidden(
    product,
    "scene",
    B,
    true);

var itemCounts = new Dictionary<int, int>
{
    [1] = 1,
    [3] = 2,
    [4] = 1,
    [10] = 3
};

var groups = new[]
{
    new GroupSpan(1, 7),
    new GroupSpan(3, 2),
    new GroupSpan(10, 2)
};

var rows = PanelProjection.Build(
    product,
    "scene",
    maxLayer: 12,
    itemCounts,
    groups);

Check("projection_has_trailing_empty",
    rows.Last() is
    {
        Kind: PanelRowKind.Layer,
        First: 13,
        ParentFolderId: null
    });

var rowA = rows.Single(x => x.FolderId == A);
var rowB = rows.Single(x => x.FolderId == B);
var rowC = rows.Single(x => x.FolderId == C);

Check("projection_outer_depth",
    rowA.Depth == 0 && rowA.ParentFolderId is null);
Check("projection_inner_depth_parent",
    rowB.Depth == 1 && rowB.ParentFolderId == A);
Check("projection_inner_hidden",
    rowB.IsHidden);
Check("projection_inner_collapsed_hides_children",
    rows.All(x =>
        !(x.Kind == PanelRowKind.Layer
          && 3 <= x.First
          && x.First <= 5)));
Check("projection_outer_item_count",
    rowA.ItemCount == 4);
Check("projection_later_item_count",
    rowC.ItemCount == 3);

var expandedCore = FolderDocumentRules.ReplaceTimeline(
    core,
    "scene",
    FolderDocumentRules.FindTimeline(core, "scene")!.Folders
        .Select(x => x.Id == B
            ? x with { IsCollapsed = false }
            : x));
var expandedProduct = FolderProductStateRules.ReplaceCore(
    product,
    expandedCore);
var expandedRows = PanelProjection.Build(
    expandedProduct,
    "scene",
    12,
    itemCounts,
    groups);

Check("projection_expand_inner_layers",
    expandedRows.Any(x =>
        x.Kind == PanelRowKind.Layer
        && x.First == 3
        && x.Depth == 2
        && x.ParentFolderId == B)
    && expandedRows.Any(x =>
        x.Kind == PanelRowKind.Layer
        && x.First == 5
        && x.Depth == 2
        && x.ParentFolderId == B));

var parents = PanelProjection.ParentMap(
    FolderDocumentRules.FindTimeline(core, "scene")!.Folders);
Check("parent_map_outer",
    parents[A] is null);
Check("parent_map_inner",
    parents[B] == A);
Check("parent_map_later",
    parents[C] is null);
Check("depth_inner",
    PanelProjection.DepthOf(parents, B) == 1);

var layer1 = expandedRows.Single(x =>
    x.Kind == PanelRowKind.Layer && x.First == 1);
var layer2 = expandedRows.Single(x =>
    x.Kind == PanelRowKind.Layer && x.First == 2);
var layer6 = expandedRows.Single(x =>
    x.Kind == PanelRowKind.Layer && x.First == 6);
var layer7 = expandedRows.Single(x =>
    x.Kind == PanelRowKind.Layer && x.First == 7);
var layer8 = expandedRows.Single(x =>
    x.Kind == PanelRowKind.Layer && x.First == 8);
var innerFolder = expandedRows.Single(x => x.FolderId == B);
var laterFolder = expandedRows.Single(x => x.FolderId == C);

var topLevel = PanelMoveRules.TopLevelSelectedRows(
    [rowA, layer1, layer2, innerFolder]);
Check("selection_parent_suppresses_descendants",
    topLevel.Count == 1 && topLevel[0].FolderId == A);

var block67 = PanelMoveRules.TryGetDragBlock(
    [layer6, layer7]);
Check("contiguous_same_parent_block",
    block67 is
    {
        Start: 6,
        Count: 2,
        SourceParentFolderId: var parent67
    } && parent67 == A);

Check("noncontiguous_block_rejected",
    PanelMoveRules.TryGetDragBlock(
        [layer6, layer8]) is null);

Check("cross_parent_block_rejected",
    PanelMoveRules.TryGetDragBlock(
        [layer2, laterFolder]) is null);

var beforeLater = PanelMoveRules.ResolveDrop(
    laterFolder,
    PanelDropZone.Before);
Check("drop_before_folder",
    beforeLater.OriginalInsertionBoundary == 10
    && beforeLater.IntoFolderId is null);

var intoLater = PanelMoveRules.ResolveDrop(
    laterFolder,
    PanelDropZone.Into);
Check("drop_into_folder",
    intoLater.OriginalInsertionBoundary == 13
    && intoLater.IntoFolderId == C);

var afterExpandedLater = PanelMoveRules.ResolveDrop(
    laterFolder,
    PanelDropZone.After);
Check("drop_after_expanded_folder_enters_front",
    afterExpandedLater.OriginalInsertionBoundary == 11
    && afterExpandedLater.IntoFolderId == C);

var collapsedLater = laterFolder with { IsCollapsed = true };
var afterCollapsedLater = PanelMoveRules.ResolveDrop(
    collapsedLater,
    PanelDropZone.After);
Check("drop_after_collapsed_folder_is_sibling_after",
    afterCollapsedLater.OriginalInsertionBoundary == 13
    && afterCollapsedLater.IntoFolderId is null);

Reject<ArgumentException>(
    "layer_into_rejected",
    () => PanelMoveRules.ResolveDrop(
        layer6,
        PanelDropZone.Into));

var blockInner = PanelMoveRules.TryGetDragBlock(
    [innerFolder])
    ?? throw new InvalidOperationException("Inner block missing.");

Check("same_boundary_same_parent_noop",
    !PanelMoveRules.CanDrop(
        expandedCore,
        "scene",
        blockInner,
        new PanelDropTarget(
            OriginalInsertionBoundary: 3,
            IntoFolderId: A)));

Check("move_inner_before_later_allowed",
    PanelMoveRules.CanDrop(
        expandedCore,
        "scene",
        blockInner,
        beforeLater));

Check("move_inner_into_self_rejected",
    !PanelMoveRules.CanDrop(
        expandedCore,
        "scene",
        blockInner,
        new PanelDropTarget(
            OriginalInsertionBoundary: 6,
            IntoFolderId: B)));

var moveInner = PanelMoveRules.PlanMove(
    expandedCore,
    "scene",
    blockInner,
    beforeLater,
    groups);
var movedInnerFolders = FolderDocumentRules.FindTimeline(
    moveInner.Core,
    "scene")!.Folders;

Check("move_inner_out_parent_shrinks",
    movedInnerFolders.Single(x => x.Id == A) is
    {
        Start: 1,
        End: 5
    });
Check("move_inner_out_becomes_top_level",
    movedInnerFolders.Single(x => x.Id == B) is
    {
        Start: 7,
        End: 9
    });
Check("move_inner_later_stays_after",
    movedInnerFolders.Single(x => x.Id == C) is
    {
        Start: 10,
        End: 12
    });

var movedParents = PanelProjection.ParentMap(
    movedInnerFolders);
Check("move_inner_parent_removed",
    movedParents[B] is null);
Check("move_map_inside_block",
    moveInner.MapLayer(3) == 7
    && moveInner.MapLayer(5) == 9);
Check("move_map_stationary_inside_parent",
    moveInner.MapLayer(6) == 3);
Check("move_inner_group_moves_relative",
    moveInner.GroupRanges[1] == 2);
Check("move_outer_group_shrinks_with_source",
    moveInner.GroupRanges[0] == 4);

var blockOuter = PanelMoveRules.TryGetDragBlock(
    [rowA])
    ?? throw new InvalidOperationException("Outer block missing.");

Check("move_outer_into_descendant_rejected",
    !PanelMoveRules.CanDrop(
        expandedCore,
        "scene",
        blockOuter,
        new PanelDropTarget(
            OriginalInsertionBoundary: 6,
            IntoFolderId: B)));

var moveOuterIntoLater = PanelMoveRules.PlanMove(
    expandedCore,
    "scene",
    blockOuter,
    intoLater,
    groups);
var outerIntoFolders = FolderDocumentRules.FindTimeline(
    moveOuterIntoLater.Core,
    "scene")!.Folders;
var outerIntoParents = PanelProjection.ParentMap(
    outerIntoFolders);

Check("move_outer_into_later_parent",
    outerIntoParents[A] == C);
Check("move_outer_carries_inner_descendant",
    outerIntoParents[B] == A);
Check("move_outer_target_expands",
    outerIntoFolders.Single(x => x.Id == C)
        is { Start: 2, End: 12 });
Check("move_outer_range_relative",
    outerIntoFolders.Single(x => x.Id == A)
        is { Start: 5, End: 12 });
Check("move_inner_range_relative_with_outer",
    outerIntoFolders.Single(x => x.Id == B)
        is { Start: 7, End: 9 });

var mappedRestoreSource = FolderProductStateRules.ReplaceRestoreMap(
    product,
    "scene",
    new Dictionary<int, bool>
    {
        [3] = true,
        [4] = false,
        [5] = true
    });
var mappedRestore = FolderProductStateRules.RemapRestoreLayers(
    mappedRestoreSource,
    "scene",
    moveInner.MapLayer);
var restore = FolderProductStateRules.RestoreMap(
    mappedRestore,
    "scene");
Check("move_map_reuses_visibility_restore_mapping",
    restore.OrderBy(x => x.Key)
        .SequenceEqual(new Dictionary<int, bool>
        {
            [7] = true,
            [8] = false,
            [9] = true
        }.OrderBy(x => x.Key)));

Check("move_result_serializable",
    FolderDocumentCodec.Load(
        FolderDocumentCodec.Save(moveInner.Core)).Success
    && FolderDocumentCodec.Load(
        FolderDocumentCodec.Save(moveOuterIntoLater.Core)).Success);

Console.WriteLine(
    $"status=PASS_S4_PANEL_PURE\nassertion_count={count}");
