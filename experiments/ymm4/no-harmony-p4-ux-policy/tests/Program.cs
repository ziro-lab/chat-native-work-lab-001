using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyUx;

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
Guid D = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
Guid E = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

var outer = new PersistedFolder { Id = A, Start = 1, End = 8, Name = "Outer", IsCollapsed = true };
var nested = new PersistedFolder { Id = B, Start = 3, End = 5, Name = "Nested", IsCollapsed = false };
var separate = new PersistedFolder { Id = C, Start = 12, End = 16, Name = "Later", IsCollapsed = true };

var source = FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState { TimelineKey = "scene-a", Folders = [outer, nested, separate] },
        new TimelineFolderState
        {
            TimelineKey = "scene-b",
            Folders =
            [
                new PersistedFolder { Id = E, Start = 20, End = 21, Name = "Other", IsCollapsed = false }
            ]
        }
    ]
});

// P4.2 legacy API remains stable for already-frozen callers.
Check("legacy_empty_selection_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [], D).Status == FolderCreationStatus.EmptySelection);
Check("legacy_single_layer_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [20], D).Status == FolderCreationStatus.TooSmall);
Check("legacy_noncontiguous_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [20, 22], D).Status == FolderCreationStatus.NonContiguous);

// S1 reference creation semantics.
var gapped = FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [20, 22], 20, D);
Check("s1_selected_gap_uses_min_max",
    gapped.Allowed && gapped.Start == 20 && gapped.End == 22
    && gapped.RequestedStart == 20 && gapped.RequestedEnd == 22
    && !gapped.NeedsAdditionalLayer);

var outside = FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [20, 22], 21, D);
Check("s1_click_outside_selection_uses_clicked_only",
    outside.Allowed && outside.Start == 21 && outside.End == 21
    && outside.NeedsAdditionalLayer);

var noSelection = FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [], 20, D);
Check("s1_empty_selection_uses_clicked_only",
    noSelection.Allowed && noSelection.Start == 20 && noSelection.End == 20
    && noSelection.NeedsAdditionalLayer);

var headAdjusted = FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [1, 2], 1, D);
Check("s1_folder_head_adjusts_down",
    headAdjusted.Allowed
    && headAdjusted.RequestedStart == 1
    && headAdjusted.RequestedEnd == 2
    && headAdjusted.Start == 2
    && headAdjusted.End == 2
    && headAdjusted.AdjustedForFolderHead
    && headAdjusted.NeedsAdditionalLayer);

var nestedHeadAdjusted = FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [3, 4], 3, D);
Check("s1_nested_folder_head_adjusts_down",
    nestedHeadAdjusted.Allowed
    && nestedHeadAdjusted.Start == 4
    && nestedHeadAdjusted.End == 4
    && nestedHeadAdjusted.AdjustedForFolderHead);

var crossing = FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [7, 10], 7, D);
Check("s1_crossing_range_rejected",
    crossing.Status == FolderCreationStatus.InvalidRange);

Check("s1_negative_clicked_rejected",
    FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [], -1, D).Status
        == FolderCreationStatus.InvalidSelection);
Reject<ArgumentException>("s1_empty_candidate_rejected",
    () => FolderUxCommands.EvaluateCreateFromContext(source, "scene-a", [], 20, Guid.Empty));

var created = FolderUxCommands.CreateFolder(source, "scene-a", gapped, D, "  New Folder  ");
var newFolder = FolderDocumentRules.FindTimeline(created, "scene-a")!.Folders.Single(x => x.Id == D);
Check("s1_create_from_decision_exact_range",
    newFolder.Start == 20 && newFolder.End == 22);
Check("s1_create_from_decision_trims_name",
    newFolder.Name == "New Folder" && !newFolder.IsCollapsed);
Check("s1_create_preserves_source",
    FolderDocumentRules.FindTimeline(source, "scene-a")!.Folders.Count == 3);

// One-row convenience plan: explicit insertion belongs to new folder + ancestor,
// while disjoint folders below are shifted once.
var parentTailSource = FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState
        {
            TimelineKey = "tail",
            Folders =
            [
                new PersistedFolder { Id = A, Start = 1, End = 3, Name = "Parent", IsCollapsed = false },
                new PersistedFolder { Id = C, Start = 6, End = 7, Name = "Later", IsCollapsed = false }
            ]
        }
    ]
});
var tailDecision = FolderUxCommands.EvaluateCreateFromContext(parentTailSource, "tail", [3], 3, D);
Check("s1_tail_single_decision",
    tailDecision.Allowed && tailDecision.Start == 3 && tailDecision.End == 3
    && tailDecision.NeedsAdditionalLayer);

var tailPlanned = FolderUxCommands.CreateFolderWithInsertedLayer(
    parentTailSource, "tail", tailDecision, D, "Child");
var tailFolders = FolderDocumentRules.FindTimeline(tailPlanned, "tail")!.Folders;
var parentAfter = tailFolders.Single(x => x.Id == A);
var childAfter = tailFolders.Single(x => x.Id == D);
var laterAfter = tailFolders.Single(x => x.Id == C);
Check("s1_inserted_layer_expands_new_folder",
    childAfter.Start == 3 && childAfter.End == 4);
Check("s1_inserted_layer_expands_tail_ancestor",
    parentAfter.Start == 1 && parentAfter.End == 4);
Check("s1_inserted_layer_shifts_later_folder_once",
    laterAfter.Start == 7 && laterAfter.End == 8);

Reject<InvalidOperationException>("s1_insert_plan_requires_single_row",
    () => FolderUxCommands.CreateFolderWithInsertedLayer(source, "scene-a", gapped, D, "Nope"));

// Bulk collapse is one immutable document operation and is timeline-scoped.
var allOpen = FolderUxCommands.SetAllCollapsed(source, "scene-a", false);
Check("s1_expand_all",
    FolderDocumentRules.FindTimeline(allOpen, "scene-a")!.Folders.All(x => !x.IsCollapsed));
Check("s1_expand_all_preserves_other_timeline",
    !FolderDocumentRules.FindTimeline(allOpen, "scene-b")!.Folders.Single().IsCollapsed);

var allClosed = FolderUxCommands.SetAllCollapsed(allOpen, "scene-a", true);
Check("s1_collapse_all",
    FolderDocumentRules.FindTimeline(allClosed, "scene-a")!.Folders.All(x => x.IsCollapsed));
Check("s1_bulk_roundtrip_serializable",
    FolderDocumentCodec.Load(FolderDocumentCodec.Save(allClosed)).Success);

var renamed = FolderUxCommands.RenameFolder(created, "scene-a", D, "  Renamed  ");
Check("rename_trims",
    FolderDocumentRules.FindTimeline(renamed, "scene-a")!.Folders.Single(x => x.Id == D).Name == "Renamed");
Reject<ArgumentException>("blank_rename_rejected",
    () => FolderUxCommands.RenameFolder(renamed, "scene-a", D, "   "));

var toggled = FolderUxCommands.ToggleCollapsed(renamed, "scene-a", D);
Check("toggle_collapsed",
    FolderDocumentRules.FindTimeline(toggled, "scene-a")!.Folders.Single(x => x.Id == D).IsCollapsed);

var ungrouped = FolderUxCommands.Ungroup(source, "scene-a", A);
Check("ungroup_removes_only_target",
    FolderDocumentRules.FindTimeline(ungrouped, "scene-a")!.Folders.All(x => x.Id != A)
    && FolderDocumentRules.FindTimeline(ungrouped, "scene-a")!.Folders.Any(x => x.Id == B));

Console.WriteLine($"status=PASS_S1_PURE\nassertion_count={count}");
