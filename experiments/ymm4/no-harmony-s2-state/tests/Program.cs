using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyState;

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

var folderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
var otherId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

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
                    Id = folderId,
                    Start = 1,
                    End = 4,
                    Name = "A",
                    IsCollapsed = false
                },
                new PersistedFolder
                {
                    Id = otherId,
                    Start = 8,
                    End = 10,
                    Name = "B",
                    IsCollapsed = true
                }
            ]
        }
    ]
});

var empty = FolderSessionDocumentCodec.Load(null);
Check("empty_load",
    empty.Success
    && empty.Status == FolderSessionLoadStatus.Empty
    && empty.Document is not null
    && empty.Document.Core.Timelines.Count == 0);

var legacyRaw = FolderDocumentCodec.Save(core);
var legacy = FolderSessionDocumentCodec.Load(legacyRaw);
Check("v1_migrates",
    legacy.Success
    && legacy.Status == FolderSessionLoadStatus.LoadedV1
    && legacy.Document is not null
    && FolderDocumentCodec.Save(legacy.Document.Core) == legacyRaw
    && legacy.Document.FolderOptions.Count == 0
    && legacy.Document.VisibilityRestore.Count == 0);

var session = FolderSessionDocumentRules.NormalizeAndValidate(
    new FolderSessionDocument
    {
        Core = core,
        FolderOptions =
        [
            new FolderOptionState
            {
                TimelineKey = "scene-a",
                FolderId = folderId,
                Color = "#ffe05a5a",
                Hidden = true
            },
            // Default option is intentionally normalized away.
            new FolderOptionState
            {
                TimelineKey = "scene-a",
                FolderId = otherId,
                Color = null,
                Hidden = false
            }
        ],
        VisibilityRestore =
        [
            new VisibilityRestoreState
            {
                TimelineKey = "scene-a",
                Layer = 1,
                RestoreVisible = true,
                UserOverride = false
            },
            new VisibilityRestoreState
            {
                TimelineKey = "scene-a",
                Layer = 2,
                RestoreVisible = false,
                UserOverride = true
            }
        ]
    });

Check("color_canonicalized",
    session.FolderOptions.Single().Color == "#FFE05A5A");
Check("default_option_removed",
    session.FolderOptions.Count == 1);
Check("hidden_preserved",
    session.FolderOptions.Single().Hidden);
Check("restore_preserved",
    session.VisibilityRestore.Count == 2
    && session.VisibilityRestore.Single(x => x.Layer == 1).RestoreVisible
    && session.VisibilityRestore.Single(x => x.Layer == 2).UserOverride);

var raw = FolderSessionDocumentCodec.Save(session);
var loaded = FolderSessionDocumentCodec.Load(raw);
Check("v2_roundtrip",
    loaded.Success
    && loaded.Status == FolderSessionLoadStatus.LoadedV2
    && loaded.Document is not null
    && FolderSessionDocumentCodec.Save(loaded.Document) == raw);
Check("v2_contains_core_v1",
    loaded.Document!.Core.SchemaVersion == FolderDocument.CurrentSchemaVersion);
Check("v2_is_rejected_by_old_v1_codec",
    FolderDocumentCodec.Load(raw).Status == FolderDocumentLoadStatus.UnsupportedVersion);

var setDefault = FolderSessionDocumentRules.SetFolderOption(
    session,
    "scene-a",
    folderId,
    color: null,
    hidden: false);
Check("set_default_removes_option",
    setDefault.FolderOptions.Count == 0);

var setColor = FolderSessionDocumentRules.SetFolderOption(
    setDefault,
    "scene-a",
    otherId,
    "#FF4F7FE0",
    hidden: false);
Check("set_color_adds_option",
    setColor.FolderOptions.Single().FolderId == otherId
    && setColor.FolderOptions.Single().Color == "#FF4F7FE0");

var reducedCore = FolderDocumentRules.ReplaceTimeline(
    core,
    "scene-a",
    [core.Timelines.Single().Folders.Single(x => x.Id == folderId)]);
var reduced = FolderSessionDocumentRules.ReplaceCore(session, reducedCore);
Check("replace_core_prunes_deleted_folder_options",
    reduced.FolderOptions.Single().FolderId == folderId);
Check("replace_core_keeps_timeline_restore",
    reduced.VisibilityRestore.Count == 2);

Reject<ArgumentException>("bad_color_rejected",
    () => FolderSessionDocumentRules.SetFolderOption(
        session, "scene-a", folderId, "red", false));

Reject<ArgumentException>("dangling_option_rejected",
    () => FolderSessionDocumentRules.NormalizeAndValidate(
        session with
        {
            FolderOptions =
            [
                new FolderOptionState
                {
                    TimelineKey = "scene-a",
                    FolderId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    Color = "#FFFFFFFF",
                    Hidden = false
                }
            ]
        }));

Reject<ArgumentException>("duplicate_option_rejected",
    () => FolderSessionDocumentRules.NormalizeAndValidate(
        session with
        {
            FolderOptions =
            [
                new FolderOptionState
                {
                    TimelineKey = "scene-a",
                    FolderId = folderId,
                    Color = "#FFFFFFFF",
                    Hidden = false
                },
                new FolderOptionState
                {
                    TimelineKey = "scene-a",
                    FolderId = folderId,
                    Color = "#FF000000",
                    Hidden = true
                }
            ]
        }));

Reject<ArgumentException>("negative_restore_layer_rejected",
    () => FolderSessionDocumentRules.NormalizeAndValidate(
        session with
        {
            VisibilityRestore =
            [
                new VisibilityRestoreState
                {
                    TimelineKey = "scene-a",
                    Layer = -1,
                    RestoreVisible = true
                }
            ]
        }));

Reject<ArgumentException>("unknown_restore_timeline_rejected",
    () => FolderSessionDocumentRules.NormalizeAndValidate(
        session with
        {
            VisibilityRestore =
            [
                new VisibilityRestoreState
                {
                    TimelineKey = "missing",
                    Layer = 1,
                    RestoreVisible = true
                }
            ]
        }));

var malformed = FolderSessionDocumentCodec.Load("{");
Check("malformed_rejected",
    malformed.Status == FolderSessionLoadStatus.Malformed
    && !malformed.Success);

var future = FolderSessionDocumentCodec.Load(
    "{\"schemaVersion\":3,\"core\":{}}");
Check("future_rejected",
    future.Status == FolderSessionLoadStatus.UnsupportedVersion
    && !future.Success);

var invalid = FolderSessionDocumentCodec.Load(
    raw.Replace("#FFE05A5A", "invalid", StringComparison.Ordinal));
Check("invalid_v2_rejected",
    invalid.Status == FolderSessionLoadStatus.Invalid
    && !invalid.Success);

Check("deterministic_save",
    FolderSessionDocumentCodec.Save(session)
        == FolderSessionDocumentCodec.Save(
            session with
            {
                FolderOptions = session.FolderOptions.Reverse().ToArray(),
                VisibilityRestore = session.VisibilityRestore.Reverse().ToArray()
            }));

// Runtime P3 raw-quarantine policy remains exact after switching to outer v2.
var runtime = new Ymm4NoHarmonyFolderLayoutProbe.FolderPersistenceSession();
var runtimeLegacy = runtime.Load(legacyRaw);
Check("runtime_v1_loads",
    runtimeLegacy.Success
    && runtimeLegacy.Status == FolderSessionLoadStatus.LoadedV1
    && !runtime.IsRecoveryBlocked
    && FolderDocumentCodec.Save(runtime.Document) == legacyRaw);

var promotedRaw = runtime.Save();
Check("runtime_v1_promotes_to_v2_on_save",
    promotedRaw is not null
    && FolderSessionDocumentCodec.Load(promotedRaw).Status
        == FolderSessionLoadStatus.LoadedV2
    && FolderDocumentCodec.Load(promotedRaw).Status
        == FolderDocumentLoadStatus.UnsupportedVersion);

var futureRaw = "{\"schemaVersion\":99,\"sentinel\":\"keep-me-exactly\"}";
var futureRuntime = new Ymm4NoHarmonyFolderLayoutProbe.FolderPersistenceSession();
var futureResult = futureRuntime.Load(futureRaw);
Check("runtime_future_quarantined",
    !futureResult.Success
    && futureRuntime.IsRecoveryBlocked
    && futureRuntime.Save() == futureRaw);

var malformedRaw = "{ definitely not json";
var malformedRuntime = new Ymm4NoHarmonyFolderLayoutProbe.FolderPersistenceSession();
var malformedResult = malformedRuntime.Load(malformedRaw);
Check("runtime_malformed_quarantined",
    !malformedResult.Success
    && malformedRuntime.IsRecoveryBlocked
    && malformedRuntime.Save() == malformedRaw);

futureRuntime.ReplaceDocument(core);
Check("runtime_replace_clears_quarantine",
    !futureRuntime.IsRecoveryBlocked
    && futureRuntime.Document.Timelines.Count == 1
    && FolderSessionDocumentCodec.Load(futureRuntime.Save()).Success);

malformedRuntime.Reset();
Check("runtime_reset_clears_quarantine",
    !malformedRuntime.IsRecoveryBlocked
    && malformedRuntime.Save() is null);

// Visibility policy: one restore entry per layer, overlapping hidden folders
// never overwrite the original state, and final restore waits for the last
// hidden-folder reason.
var visibilityCore = FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState
        {
            TimelineKey = "vis",
            Folders =
            [
                new PersistedFolder
                {
                    Id = folderId,
                    Start = 1,
                    End = 5,
                    Name = "Parent",
                    IsCollapsed = false
                },
                new PersistedFolder
                {
                    Id = otherId,
                    Start = 3,
                    End = 4,
                    Name = "Child",
                    IsCollapsed = false
                }
            ]
        }
    ]
});

var visibilityState = new FolderSessionDocument { Core = visibilityCore };
var hostVisibility = new Dictionary<int, bool>
{
    [1] = true,
    [2] = false,
    [3] = true,
    [4] = true,
    [5] = true
};

var hideChild = FolderVisibilityRules.SetFolderHidden(
    visibilityState,
    "vis",
    otherId,
    true,
    hostVisibility);
Check("visibility_hide_child_writes_exact_range",
    hideChild.Writes.Select(x => x.Layer).SequenceEqual(new[] { 3, 4 })
    && hideChild.Writes.All(x => !x.Visible));
Check("visibility_hide_child_captures_original_once",
    hideChild.State.VisibilityRestore.Count == 2
    && hideChild.State.VisibilityRestore.All(x => x.RestoreVisible));

var hostAfterChild = hostVisibility.ToDictionary(x => x.Key, x => x.Value);
foreach (var write in hideChild.Writes)
    hostAfterChild[write.Layer] = write.Visible;

var hideParent = FolderVisibilityRules.SetFolderHidden(
    hideChild.State,
    "vis",
    folderId,
    true,
    hostAfterChild);
Check("visibility_hide_parent_writes_parent_range",
    hideParent.Writes.Select(x => x.Layer)
        .SequenceEqual(new[] { 1, 2, 3, 4, 5 })
    && hideParent.Writes.All(x => !x.Visible));
Check("visibility_overlap_does_not_overwrite_restore",
    hideParent.State.VisibilityRestore.Single(x => x.Layer == 3).RestoreVisible
    && hideParent.State.VisibilityRestore.Single(x => x.Layer == 4).RestoreVisible
    && !hideParent.State.VisibilityRestore.Single(x => x.Layer == 2).RestoreVisible);

var userShow = FolderVisibilityRules.ObserveUserVisibility(
    hideParent.State,
    "vis",
    3,
    true);
Check("visibility_user_override_recorded",
    userShow.VisibilityRestore.Single(x => x.Layer == 3)
        is { RestoreVisible: true, UserOverride: true });

var unhideParent = FolderVisibilityRules.SetFolderHidden(
    userShow,
    "vis",
    folderId,
    false,
    hostAfterChild);
Check("visibility_unhide_parent_keeps_child_covered",
    !unhideParent.Writes.Any(x => x.Layer is 3 or 4)
    && unhideParent.Writes.Single(x => x.Layer == 1).Visible
    && !unhideParent.Writes.Single(x => x.Layer == 2).Visible
    && unhideParent.Writes.Single(x => x.Layer == 5).Visible);
Check("visibility_child_restore_entries_remain",
    unhideParent.State.VisibilityRestore.Any(x => x.Layer == 3)
    && unhideParent.State.VisibilityRestore.Any(x => x.Layer == 4));

var unhideChild = FolderVisibilityRules.SetFolderHidden(
    unhideParent.State,
    "vis",
    otherId,
    false,
    hostAfterChild);
Check("visibility_last_reason_restores_child",
    unhideChild.Writes.Single(x => x.Layer == 3).Visible
    && unhideChild.Writes.Single(x => x.Layer == 4).Visible
    && unhideChild.State.VisibilityRestore.Count == 0);
Check("visibility_originally_hidden_restored_hidden",
    unhideParent.Writes.Single(x => x.Layer == 2).Visible == false);

var hideAgainBase = FolderVisibilityRules.SetFolderHidden(
    visibilityState,
    "vis",
    folderId,
    true,
    hostVisibility);
var overrideAgain = FolderVisibilityRules.ObserveUserVisibility(
    hideAgainBase.State,
    "vis",
    3,
    true);
var explicitRehideChild = FolderVisibilityRules.SetFolderHidden(
    overrideAgain,
    "vis",
    otherId,
    true,
    hostVisibility);
Check("visibility_explicit_rehide_resets_override_but_keeps_restore_value",
    explicitRehideChild.Writes.Single(x => x.Layer == 3).Visible == false
    && explicitRehideChild.State.VisibilityRestore.Single(x => x.Layer == 3)
        is { RestoreVisible: true, UserOverride: false });

var outsideUserChange = FolderVisibilityRules.ObserveUserVisibility(
    visibilityState,
    "vis",
    1,
    false);
Check("visibility_user_change_outside_hidden_is_noop",
    FolderSessionDocumentCodec.Save(outsideUserChange)
        == FolderSessionDocumentCodec.Save(
            FolderSessionDocumentRules.NormalizeAndValidate(visibilityState)));

Console.WriteLine($"status=PASS_S2_STATE_V2\nassertion_count={count}");
