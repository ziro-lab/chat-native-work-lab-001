using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;

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
            TimelineKey = "scene-a",
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
                }
            ]
        },
        new TimelineFolderState
        {
            TimelineKey = "scene-b",
            Folders =
            [
                new PersistedFolder
                {
                    Id = C,
                    Start = 10,
                    End = 12,
                    Name = "Other",
                    IsCollapsed = false
                }
            ]
        }
    ]
});

var v1Text = FolderDocumentCodec.Save(core);
var migrated = FolderProductStateCodec.Load(v1Text);
Check("v1_load_success", migrated.Success);
Check("v1_marked_migrated", migrated.MigratedFromV1);
Check("v1_core_preserved",
    FolderDocumentCodec.Save(migrated.State!.Core) == v1Text);
Check("v1_defaults_empty",
    migrated.State.FolderOptions.Count == 0
    && migrated.State.VisibilityRestore.Count == 0);

var v2FromV1 = FolderProductStateCodec.Save(migrated.State);
Check("save_after_v1_is_outer_v2",
    v2FromV1.Contains("\"schemaVersion\":2", StringComparison.Ordinal));
Check("old_v1_codec_rejects_outer_v2",
    FolderDocumentCodec.Load(v2FromV1).Status
        == FolderDocumentLoadStatus.UnsupportedVersion);

var state = FolderProductStateRules.ReplaceCore(
    FolderProductState.Empty,
    core);
state = FolderProductStateRules.SetFolderColor(
    state,
    "scene-a",
    A,
    "#FF4C7FE0");
state = FolderProductStateRules.SetFolderHidden(
    state,
    "scene-a",
    A,
    true);
state = FolderProductStateRules.SetFolderHidden(
    state,
    "scene-a",
    B,
    true);
state = FolderProductStateRules.ReplaceRestoreMap(
    state,
    "scene-a",
    new Dictionary<int, bool>
    {
        [1] = true,
        [2] = false,
        [3] = true,
        [4] = true,
        [5] = false,
        [6] = true
    });

var optionA = FolderProductStateRules.FindOption(state, "scene-a", A);
Check("color_roundtrip_state",
    optionA?.Color == "#FF4C7FE0");
Check("hidden_roundtrip_state",
    optionA?.Hidden == true);
Check("hidden_union_outer_inner",
    FolderProductStateRules.HiddenLayers(state, "scene-a")
        .SetEquals(Enumerable.Range(1, 6)));
Check("restore_map_exact",
    FolderProductStateRules.RestoreMap(state, "scene-a")
        .OrderBy(x => x.Key)
        .SequenceEqual(new Dictionary<int, bool>
        {
            [1] = true,
            [2] = false,
            [3] = true,
            [4] = true,
            [5] = false,
            [6] = true
        }.OrderBy(x => x.Key)));

var v2Text = FolderProductStateCodec.Save(state);
var loadedV2 = FolderProductStateCodec.Load(v2Text);
Check("v2_load_success", loadedV2.Success && !loadedV2.MigratedFromV1);
Check("v2_roundtrip_exact",
    FolderProductStateCodec.Save(loadedV2.State!) == v2Text);

var clearColor = FolderProductStateRules.SetFolderColor(
    state,
    "scene-a",
    A,
    null);
Check("clear_color_preserves_hidden",
    FolderProductStateRules.FindOption(clearColor, "scene-a", A) is
        { Color: null, Hidden: true });

var unhideA = FolderProductStateRules.SetFolderHidden(
    clearColor,
    "scene-a",
    A,
    false);
Check("unhide_outer_keeps_inner_option",
    FolderProductStateRules.FindOption(unhideA, "scene-a", A) is null
    && FolderProductStateRules.FindOption(unhideA, "scene-a", B)?.Hidden == true);
Check("nested_hidden_union_after_outer_open",
    FolderProductStateRules.HiddenLayers(unhideA, "scene-a")
        .SetEquals(new[] { 3, 4 }));

var unhideB = FolderProductStateRules.SetFolderHidden(
    unhideA,
    "scene-a",
    B,
    false);
Check("all_default_options_pruned",
    unhideB.FolderOptions.Count == 0);
Check("no_hidden_layers_after_all_open",
    FolderProductStateRules.HiddenLayers(unhideB, "scene-a").Count == 0);

var coreWithoutA = FolderDocumentRules.ReplaceTimeline(
    core,
    "scene-a",
    FolderDocumentRules.FindTimeline(core, "scene-a")!.Folders
        .Where(x => x.Id != A));
var pruned = FolderProductStateRules.ReplaceCore(
    state,
    coreWithoutA);
Check("replace_core_prunes_orphan_option",
    FolderProductStateRules.FindOption(pruned, "scene-a", A) is null);
Check("replace_core_keeps_live_option",
    FolderProductStateRules.FindOption(pruned, "scene-a", B)?.Hidden == true);

var unknown = FolderProductStateCodec.Load(
    "{\"schemaVersion\":999,\"core\":{}}");
Check("unknown_v2plus_rejected",
    unknown.Status == FolderDocumentLoadStatus.UnsupportedVersion);

var malformed = FolderProductStateCodec.Load("{oops");
Check("malformed_rejected",
    malformed.Status == FolderDocumentLoadStatus.Malformed);

Reject<ArgumentException>("orphan_option_rejected",
    () => FolderProductStateRules.NormalizeAndValidate(new FolderProductState
    {
        Core = core,
        FolderOptions =
        [
            new FolderOptionState
            {
                TimelineKey = "scene-a",
                FolderId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Hidden = true
            }
        ]
    }));

Reject<ArgumentException>("duplicate_restore_layer_rejected",
    () => FolderProductStateRules.NormalizeAndValidate(new FolderProductState
    {
        Core = core,
        VisibilityRestore =
        [
            new TimelineVisibilityRestore
            {
                TimelineKey = "scene-a",
                Layers =
                [
                    new LayerVisibilityRestore { Layer = 1, Visible = true },
                    new LayerVisibilityRestore { Layer = 1, Visible = false }
                ]
            }
        ]
    }));

Console.WriteLine($"status=PASS_S2_EXTENDED_STATE\nassertion_count={count}");
