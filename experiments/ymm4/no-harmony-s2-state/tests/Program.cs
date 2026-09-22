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

Console.WriteLine($"status=PASS_S2_STATE_V2\nassertion_count={count}");
