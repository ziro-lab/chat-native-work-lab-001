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

Check("empty_selection_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [], D).Status == FolderCreationStatus.EmptySelection);
Check("single_layer_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [20], D).Status == FolderCreationStatus.TooSmall);
Check("negative_layer_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [-1, 0], D).Status == FolderCreationStatus.InvalidSelection);
Check("noncontiguous_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [20, 22], D).Status == FolderCreationStatus.NonContiguous);
Check("duplicate_selection_normalized",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [20, 20, 21], D).Allowed);
Check("disjoint_range_allowed",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [20, 21], D).Allowed);
Check("nested_range_allowed",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [6, 7], D).Allowed);
Check("crossing_range_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [7, 8, 9, 10], D).Status == FolderCreationStatus.InvalidRange);
Check("duplicate_head_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [1, 2], D).Status == FolderCreationStatus.InvalidRange);
Check("existing_nested_head_rejected",
    FolderUxCommands.EvaluateCreate(source, "scene-a", [3, 4], D).Status == FolderCreationStatus.InvalidRange);

var created = FolderUxCommands.CreateFolder(source, "scene-a", [20, 21], D, "  New Folder  ");
var newFolder = FolderDocumentRules.FindTimeline(created, "scene-a")!.Folders.Single(x => x.Id == D);
Check("create_exact_range", newFolder.Start == 20 && newFolder.End == 21);
Check("create_trims_name_and_starts_expanded", newFolder.Name == "New Folder" && !newFolder.IsCollapsed);
Check("create_preserves_existing_folders",
    FolderDocumentRules.FindTimeline(created, "scene-a")!.Folders.Count == 4);
Check("create_preserves_other_timeline",
    FolderDocumentRules.FindTimeline(created, "scene-b")!.Folders.Single().Id == E);
Check("create_does_not_mutate_source",
    FolderDocumentRules.FindTimeline(source, "scene-a")!.Folders.Count == 3);

var renamed = FolderUxCommands.RenameFolder(created, "scene-a", D, "  Renamed  ");
Check("rename_trims", FolderDocumentRules.FindTimeline(renamed, "scene-a")!.Folders.Single(x => x.Id == D).Name == "Renamed");
Reject<ArgumentException>("blank_rename_rejected",
    () => FolderUxCommands.RenameFolder(renamed, "scene-a", D, "   "));

var toggled = FolderUxCommands.ToggleCollapsed(renamed, "scene-a", D);
Check("toggle_collapsed_on",
    FolderDocumentRules.FindTimeline(toggled, "scene-a")!.Folders.Single(x => x.Id == D).IsCollapsed);
var toggledBack = FolderUxCommands.ToggleCollapsed(toggled, "scene-a", D);
Check("toggle_collapsed_roundtrip",
    !FolderDocumentRules.FindTimeline(toggledBack, "scene-a")!.Folders.Single(x => x.Id == D).IsCollapsed);

var ungroupedNested = FolderUxCommands.Ungroup(source, "scene-a", A);
var remainingA = FolderDocumentRules.FindTimeline(ungroupedNested, "scene-a")!.Folders;
Check("ungroup_removes_metadata_only_target", remainingA.All(x => x.Id != A));
Check("ungroup_keeps_nested_folder_metadata", remainingA.Any(x => x.Id == B));
Check("ungroup_keeps_disjoint_folder_metadata", remainingA.Any(x => x.Id == C));
Check("ungroup_preserves_other_timeline",
    FolderDocumentRules.FindTimeline(ungroupedNested, "scene-b")!.Folders.Single().Id == E);

Reject<KeyNotFoundException>("missing_folder_rename_rejected",
    () => FolderUxCommands.RenameFolder(source, "scene-a", D, "Nope"));
Reject<KeyNotFoundException>("missing_timeline_ungroup_rejected",
    () => FolderUxCommands.Ungroup(source, "missing", A));
Reject<ArgumentException>("empty_candidate_id_rejected",
    () => FolderUxCommands.EvaluateCreate(source, "scene-a", [20, 21], Guid.Empty));

Check("commands_remain_serializable",
    FolderDocumentCodec.Load(FolderDocumentCodec.Save(toggledBack)).Success);

Console.WriteLine($"status=PASS_P4_UX_POLICY\nassertion_count={count}");
