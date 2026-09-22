using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyStructuralConvenience;
using Ymm4NoHarmonyFolderRanges;

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
                new PersistedFolder { Id = A, Start = 1, End = 8, Name = "Outer" },
                new PersistedFolder { Id = B, Start = 3, End = 5, Name = "Inner" },
                new PersistedFolder { Id = C, Start = 10, End = 12, Name = "Later" }
            ]
        }
    ]
});

var groups = new[]
{
    new GroupSpan(1, 7),   // Outer exactly to L8
    new GroupSpan(3, 2),   // Inner exactly to L5
    new GroupSpan(0, 4),   // Covers into Inner only partially
    new GroupSpan(10, 2)   // Later
};

var tail = StructuralConvenienceRules.PlanInsertIntoFolder(
    core, "scene", B, 6, 1, groups);
var tailFolders = FolderDocumentRules.FindTimeline(tail.Core, "scene")!.Folders;
Check("tail_inner_extends",
    tailFolders.Single(x => x.Id == B) is { Start: 3, End: 6 });
Check("tail_outer_extends",
    tailFolders.Single(x => x.Id == A) is { Start: 1, End: 9 });
Check("tail_later_shifts",
    tailFolders.Single(x => x.Id == C) is { Start: 11, End: 13 });
Check("tail_map",
    tail.MapLayer(5) == 5 && tail.MapLayer(6) == 7 && tail.MapLayer(10) == 11);
Check("tail_group_outer_extends",
    tail.GroupRanges[0] == 8);
Check("tail_group_inner_extends",
    tail.GroupRanges[1] == 3);
Check("tail_unrelated_group_stable",
    tail.GroupRanges[3] == 2);

var inside = StructuralConvenienceRules.PlanInsertIntoFolder(
    core, "scene", B, 4, 2, groups);
var insideFolders = FolderDocumentRules.FindTimeline(inside.Core, "scene")!.Folders;
Check("inside_target_extends",
    insideFolders.Single(x => x.Id == B) is { Start: 3, End: 7 });
Check("inside_parent_extends",
    insideFolders.Single(x => x.Id == A) is { Start: 1, End: 10 });
Check("inside_groups_cover_new_rows",
    inside.GroupRanges[0] == 9 && inside.GroupRanges[1] == 4);

var atHead = StructuralConvenienceRules.PlanInsertIntoFolder(
    core, "scene", B, 3, 1, groups);
var headFolders = FolderDocumentRules.FindTimeline(atHead.Core, "scene")!.Folders;
Check("head_target_keeps_owner_row",
    headFolders.Single(x => x.Id == B) is { Start: 3, End: 6 });
Check("head_parent_extends",
    headFolders.Single(x => x.Id == A) is { Start: 1, End: 9 });
Check("head_later_shifts",
    headFolders.Single(x => x.Id == C) is { Start: 11, End: 13 });

Reject<ArgumentOutOfRangeException>("insert_outside_folder_rejected",
    () => StructuralConvenienceRules.PlanInsertIntoFolder(
        core, "scene", B, 7, 1, groups));

var deleteInner = StructuralConvenienceRules.PlanDeleteFolderContents(
    core, "scene", B, groups);
var deleteFolders = FolderDocumentRules.FindTimeline(deleteInner.Core, "scene")!.Folders;
Check("delete_target_removed",
    deleteFolders.All(x => x.Id != B));
Check("delete_parent_shrinks",
    deleteFolders.Single(x => x.Id == A) is { Start: 1, End: 5 });
Check("delete_later_shifts",
    deleteFolders.Single(x => x.Id == C) is { Start: 7, End: 9 });
Check("delete_map_removed_rows",
    deleteInner.MapLayer(3) == -1
    && deleteInner.MapLayer(5) == -1
    && deleteInner.MapLayer(6) == 3
    && deleteInner.MapLayer(10) == 7);
Check("delete_outer_group_shrinks",
    deleteInner.GroupRanges[0] == 4);
Check("delete_removed_group_range_value_irrelevant",
    deleteInner.GroupRanges[1] == 2);

var addGroup = StructuralConvenienceRules.PlanAddGroupControl(
    core, "scene", B, groups);
var groupFolders = FolderDocumentRules.FindTimeline(
    addGroup.Structural.Core, "scene")!.Folders;
var updatedInner = groupFolders.Single(x => x.Id == B);
Check("group_add_inserts_at_folder_head",
    updatedInner.Start == 3 && updatedInner.End == 6);
Check("group_add_span_matches_updated_folder",
    addGroup.GroupLayer == 3
    && addGroup.GroupRange == 3);

var issueFolder = new PersistedFolder
{
    Id = A,
    Start = 3,
    End = 7,
    Name = "Issue"
};
var issueGroups = new[]
{
    new GroupSpan(4, 5), // leaks to 9
    new GroupSpan(1, 4), // controls 2..5 -> partial folder
    new GroupSpan(3, 4), // exact to 7
    new GroupSpan(8, 2)  // unrelated
};
var issues = StructuralConvenienceRules.FindGroupIssues(
    issueFolder, issueGroups);
Check("group_issue_count", issues.Count == 2);
Check("group_issue_leak",
    issues.Any(x =>
        x.GroupIndex == 0
        && x.Kind == GroupIssueKind.LeaksOutOfFolder
        && x.SuggestedRange == 3));
Check("group_issue_partial",
    issues.Any(x =>
        x.GroupIndex == 1
        && x.Kind == GroupIssueKind.CoversFolderPartially
        && x.SuggestedRange == 6));

var fit = StructuralConvenienceRules.PlanFitGroupRanges(
    issueFolder, issueGroups);
Check("fit_only_problem_groups",
    fit.SequenceEqual(new[] { 3, 6, 4, 2 }));

var externalInsert = StructuralConvenienceRules.SuggestExternalGroupRangeFixes(
    core,
    "scene",
    new InsertLayers(4, 1),
    new[]
    {
        new GroupSpan(1, 7),
        new GroupSpan(3, 2),
        new GroupSpan(11, 2)
    });
Check("external_insert_fix",
    externalInsert[0] == 8
    && externalInsert[1] == 3
    && externalInsert[2] is null);

var externalDelete = StructuralConvenienceRules.SuggestExternalGroupRangeFixes(
    core,
    "scene",
    new DeleteLayers(4, 1),
    new[]
    {
        new GroupSpan(1, 7),
        new GroupSpan(3, 2),
        new GroupSpan(9, 2)
    });
Check("external_delete_fix",
    externalDelete[0] == 6
    && externalDelete[1] == 1
    && externalDelete[2] is null);

var externalSwap = StructuralConvenienceRules.SuggestExternalGroupRangeFixes(
    core,
    "scene",
    new SwapAdjacentLayers(4),
    groups);
Check("external_swap_no_range_fix",
    externalSwap.All(x => x is null));

Check("planned_docs_serializable",
    FolderDocumentCodec.Load(
        FolderDocumentCodec.Save(addGroup.Structural.Core)).Success);

Console.WriteLine($"status=PASS_S3_STRUCTURAL_RULES\nassertion_count={count}");
