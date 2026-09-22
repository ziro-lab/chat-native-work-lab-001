using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyS3;

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
                    End = 6,
                    Name = "Outer",
                    IsCollapsed = false
                },
                new PersistedFolder
                {
                    Id = B,
                    Start = 3,
                    End = 4,
                    Name = "Inner",
                    IsCollapsed = true
                },
                new PersistedFolder
                {
                    Id = C,
                    Start = 8,
                    End = 9,
                    Name = "Later",
                    IsCollapsed = false
                }
            ]
        }
    ]
});

var state = FolderProductStateRules.ReplaceCore(
    FolderProductState.Empty,
    core);
state = FolderProductStateRules.SetFolderColor(
    state,
    "scene",
    B,
    "#FF4F7FE0");
state = FolderProductStateRules.ReplaceRestoreMap(
    state,
    "scene",
    new Dictionary<int, bool>
    {
        [1] = true,
        [3] = false,
        [8] = true
    });

var groups = new[]
{
    new GroupSpan(0, 6),
    new GroupSpan(2, 2),
    new GroupSpan(7, 2)
};

// Explicit insert at the inner folder tail. Standard positional insertion
// would leave Inner at 3..4; owned insertion must extend Inner and Outer.
var tail = StructuralConvenienceRules.PlanInsertIntoFolder(
    state,
    "scene",
    B,
    position: 5,
    count: 1,
    groups);

var tailFolders = FolderDocumentRules.FindTimeline(
    tail.State.Core,
    "scene")!.Folders;
Check("tail_inner_extends",
    tailFolders.Single(x => x.Id == B) is { Start: 3, End: 5 });
Check("tail_outer_extends",
    tailFolders.Single(x => x.Id == A) is { Start: 1, End: 7 });
Check("tail_later_shifts",
    tailFolders.Single(x => x.Id == C) is { Start: 9, End: 10 });
Check("tail_color_option_preserved",
    FolderProductStateRules.FindOption(tail.State, "scene", B)?.Color
        == "#FF4F7FE0");
Check("tail_restore_remaps",
    FolderProductStateRules.RestoreMap(tail.State, "scene")
        .Keys.OrderBy(x => x)
        .SequenceEqual(new[] { 1, 3, 9 }));
Check("tail_group_inside_extends",
    tail.GroupRanges[1] == 3);
Check("tail_group_covering_insert_extends",
    tail.GroupRanges[0] == 7);
Check("tail_unrelated_group_unchanged",
    tail.GroupRanges[2] == 2);
Check("tail_map_shifts_old_later_layer",
    tail.MapLayer(8) == 9);

// Insert at the folder head keeps that folder's head in place while its old
// content moves down.
var head = StructuralConvenienceRules.PlanInsertIntoFolder(
    state,
    "scene",
    B,
    position: 3,
    count: 1,
    groups);
var headFolders = FolderDocumentRules.FindTimeline(
    head.State.Core,
    "scene")!.Folders;
Check("head_inner_owner_stays",
    headFolders.Single(x => x.Id == B) is { Start: 3, End: 5 });
Check("head_outer_extends",
    headFolders.Single(x => x.Id == A) is { Start: 1, End: 7 });
Check("head_later_shifts",
    headFolders.Single(x => x.Id == C) is { Start: 9, End: 10 });

Reject<ArgumentOutOfRangeException>(
    "insert_outside_owner_rejected",
    () => StructuralConvenienceRules.PlanInsertIntoFolder(
        state,
        "scene",
        B,
        7,
        1,
        groups));

// Destructive delete removes the target folder range and shifts all surviving
// state through the same delete mapping.
var delete = StructuralConvenienceRules.PlanDeleteFolderContents(
    state,
    "scene",
    B,
    groups);
var deleteFolders = FolderDocumentRules.FindTimeline(
    delete.State.Core,
    "scene")!.Folders;
Check("delete_target_removed",
    deleteFolders.All(x => x.Id != B));
Check("delete_outer_shrinks",
    deleteFolders.Single(x => x.Id == A) is { Start: 1, End: 4 });
Check("delete_later_shifts",
    deleteFolders.Single(x => x.Id == C) is { Start: 6, End: 7 });
Check("delete_target_option_pruned",
    FolderProductStateRules.FindOption(delete.State, "scene", B) is null);
Check("delete_restore_deleted_and_shifted",
    FolderProductStateRules.RestoreMap(delete.State, "scene")
        .Keys.OrderBy(x => x)
        .SequenceEqual(new[] { 1, 6 }));
Check("delete_map_drops_target_layer",
    delete.MapLayer(3) == -1 && delete.MapLayer(8) == 6);
Check("delete_covering_group_shrinks",
    delete.GroupRanges[0] == 4);

// Group issue diagnostics.
var folder = core.Timelines.Single().Folders.Single(x => x.Id == B);
var issueGroups = new[]
{
    new GroupSpan(3, 4), // inside folder, leaks out
    new GroupSpan(1, 2), // above folder, covers only L3
    new GroupSpan(2, 2), // covers exactly through L4 -> no issue
    new GroupSpan(8, 2)
};
var issues = StructuralConvenienceRules.FindGroupIssues(
    folder,
    issueGroups);
Check("group_issue_count", issues.Count == 2);
Check("group_issue_leak",
    issues.Any(x =>
        x.GroupIndex == 0
        && x.Kind == GroupIssueKind.LeaksOutOfFolder
        && x.SuggestedRange == 1));
Check("group_issue_partial",
    issues.Any(x =>
        x.GroupIndex == 1
        && x.Kind == GroupIssueKind.CoversFolderPartially
        && x.SuggestedRange == 3));

var fitted = StructuralConvenienceRules.FitGroupRanges(
    folder,
    issueGroups);
Check("fit_only_problem_groups",
    fitted.SequenceEqual(new[] { 1, 3, 2, 2 }));

// Standard host insertion inside an existing folder should extend a GroupRange
// that previously controlled through the insertion point.
var externalInsert = StructuralConvenienceRules.FixGroupsAfterExternalEdit(
    core.Timelines.Single().Folders,
    new Ymm4NoHarmonyFolderRanges.InsertLayers(4, 1),
    new[]
    {
        new GroupSpan(1, 3),
        new GroupSpan(5, 2)
    });
Check("external_insert_group_fix",
    externalInsert[0] == 4 && externalInsert[1] is null);

// Standard delete fully inside a folder shrinks a surviving controller.
var externalDelete = StructuralConvenienceRules.FixGroupsAfterExternalEdit(
    core.Timelines.Single().Folders,
    new Ymm4NoHarmonyFolderRanges.DeleteLayers(4, 1),
    new[]
    {
        new GroupSpan(1, 4),
        new GroupSpan(6, 2)
    });
Check("external_delete_group_fix",
    externalDelete[0] == 3 && externalDelete[1] is null);

Console.WriteLine($"status=PASS_S3_STRUCTURAL_RULES\nassertion_count={count}");
