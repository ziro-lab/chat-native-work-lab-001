using Ymm4NoHarmonyPersistence;

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
Guid G(string value) => Guid.Parse(value);

var outer = new PersistedFolder
{
    Id = G("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
    Start = 1, End = 8, Name = "Outer", IsCollapsed = true
};
var nested = new PersistedFolder
{
    Id = G("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
    Start = 3, End = 5, Name = "Nested", IsCollapsed = false
};
var separate = new PersistedFolder
{
    Id = G("cccccccc-cccc-cccc-cccc-cccccccccccc"),
    Start = 12, End = 16, Name = "Later", IsCollapsed = true
};

var source = new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState { TimelineKey = "scene-b", Folders = [separate] },
        new TimelineFolderState { TimelineKey = " scene-a ", Folders = [nested, outer] }
    ]
};

var normalized = FolderDocumentRules.NormalizeAndValidate(source);
Check("schema_v1", normalized.SchemaVersion == 1);
Check("timeline_order_canonical", normalized.Timelines.Select(x => x.TimelineKey).SequenceEqual(["scene-a", "scene-b"]));
Check("folder_order_canonical", normalized.Timelines[0].Folders.Select(x => x.Id).SequenceEqual([outer.Id, nested.Id]));
Check("metadata_preserved", normalized.Timelines[0].Folders[0].Name == "Outer" && normalized.Timelines[0].Folders[0].IsCollapsed);
Check("source_not_mutated", source.Timelines[1].TimelineKey == " scene-a " && source.Timelines[1].Folders[0].Id == nested.Id);

var json = FolderDocumentCodec.Save(source);
var loaded = FolderDocumentCodec.Load(json);
Check("roundtrip_loaded", loaded.Status == FolderDocumentLoadStatus.Loaded && loaded.Document is not null);
Check("roundtrip_semantics", loaded.Document!.Timelines[0].Folders[1].Name == "Nested" && !loaded.Document.Timelines[0].Folders[1].IsCollapsed);
Check("roundtrip_canonical_stable", FolderDocumentCodec.Save(loaded.Document) == json);

var sameDifferentOrder = new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState { TimelineKey = "scene-a", Folders = [outer, nested] },
        new TimelineFolderState { TimelineKey = "scene-b", Folders = [separate] }
    ]
};
Check("serialization_deterministic", FolderDocumentCodec.Save(sameDifferentOrder) == json);

var empty = FolderDocumentCodec.Load("   ");
Check("empty_text_safe", empty.Status == FolderDocumentLoadStatus.Empty && empty.Document == FolderDocument.Empty);
Check("malformed_json_safe", FolderDocumentCodec.Load("{no").Status == FolderDocumentLoadStatus.Malformed);
Check("missing_schema_safe", FolderDocumentCodec.Load("{\"timelines\":[]}").Status == FolderDocumentLoadStatus.Malformed);
Check("future_schema_safe", FolderDocumentCodec.Load("{\"schemaVersion\":99,\"timelines\":[]}").Status == FolderDocumentLoadStatus.UnsupportedVersion);
Check("wrong_shape_safe", FolderDocumentCodec.Load("{\"schemaVersion\":1,\"timelines\":null}").Status == FolderDocumentLoadStatus.Invalid);

var nullNameJson = "{\"schemaVersion\":1,\"timelines\":[{\"timelineKey\":\"scene-a\",\"folders\":[{\"id\":\"dddddddd-dddd-dddd-dddd-dddddddddddd\",\"start\":2,\"end\":3,\"name\":null,\"isCollapsed\":true}]}]}";
var nullName = FolderDocumentCodec.Load(nullNameJson);
Check("null_name_normalized", nullName.Status == FolderDocumentLoadStatus.Loaded && nullName.Document!.Timelines[0].Folders[0].Name == string.Empty);

var replaced = FolderDocumentRules.ReplaceTimeline(
    normalized,
    "scene-a",
    [outer with { Name = "Renamed", IsCollapsed = false }]);
Check("replace_updates_target", FolderDocumentRules.FindTimeline(replaced, "scene-a")!.Folders.Single().Name == "Renamed");
Check("replace_preserves_other_timeline", FolderDocumentRules.FindTimeline(replaced, "scene-b")!.Folders.Single().Id == separate.Id);
Check("replace_does_not_mutate_source", FolderDocumentRules.FindTimeline(normalized, "scene-a")!.Folders.Count == 2);

var removed = FolderDocumentRules.RemoveTimeline(replaced, "scene-a");
Check("remove_only_target", FolderDocumentRules.FindTimeline(removed, "scene-a") is null && FolderDocumentRules.FindTimeline(removed, "scene-b") is not null);
Check("stale_unknown_timeline_can_be_retained", FolderDocumentCodec.Load(FolderDocumentCodec.Save(normalized)).Document!.Timelines.Count == 2);

Reject<ArgumentException>("duplicate_timeline_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines =
    [
        new TimelineFolderState { TimelineKey = "same" },
        new TimelineFolderState { TimelineKey = "same" }
    ]
}));
Reject<ArgumentException>("blank_timeline_key_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines = [new TimelineFolderState { TimelineKey = "  " }]
}));
Reject<ArgumentException>("duplicate_folder_id_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines = [new TimelineFolderState { TimelineKey = "x", Folders = [outer, outer with { Start = 20, End = 21 }] }]
}));
Reject<ArgumentException>("empty_folder_id_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines = [new TimelineFolderState { TimelineKey = "x", Folders = [outer with { Id = Guid.Empty }] }]
}));
Reject<ArgumentException>("duplicate_head_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines = [new TimelineFolderState { TimelineKey = "x", Folders = [outer, separate with { Start = outer.Start }] }]
}));
Reject<ArgumentException>("crossing_ranges_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines = [new TimelineFolderState { TimelineKey = "x", Folders = [outer, separate with { Start = 5, End = 10 }] }]
}));
Reject<ArgumentException>("negative_range_rejected", () => FolderDocumentRules.NormalizeAndValidate(new FolderDocument
{
    Timelines = [new TimelineFolderState { TimelineKey = "x", Folders = [outer with { Start = -1 }] }]
}));
Reject<ArgumentException>("save_invalid_rejected", () => FolderDocumentCodec.Save(new FolderDocument { SchemaVersion = 7 }));
Check("load_invalid_range_safe", FolderDocumentCodec.Load(
    "{\"schemaVersion\":1,\"timelines\":[{\"timelineKey\":\"x\",\"folders\":[{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"start\":5,\"end\":2,\"name\":\"bad\",\"isCollapsed\":true}]}]}")
    .Status == FolderDocumentLoadStatus.Invalid);

// Recovery session: unreadable project state must survive an unrelated save
// byte-for-byte instead of being silently replaced by an empty document.
var session = new FolderPersistenceSession();
var sessionValid = session.Load(json);
Check("session_valid_loaded", sessionValid.Success && !session.IsRecoveryBlocked);
Check("session_valid_save_canonical", session.Save() == json);

const string malformedRaw = "{ definitely-not-json";
var sessionMalformed = session.Load(malformedRaw);
Check("session_malformed_runtime_empty", sessionMalformed.Status == FolderDocumentLoadStatus.Malformed && session.Document == FolderDocument.Empty);
Check("session_malformed_quarantined", session.IsRecoveryBlocked && session.PreservedUnreadableState == malformedRaw);
Check("session_malformed_save_preserves_raw", session.Save() == malformedRaw);

const string futureRaw = "{\"schemaVersion\":99,\"timelines\":[{\"timelineKey\":\"future\",\"folders\":[]}]}";
var sessionFuture = session.Load(futureRaw);
Check("session_future_quarantined", sessionFuture.Status == FolderDocumentLoadStatus.UnsupportedVersion && session.IsRecoveryBlocked);
Check("session_future_save_preserves_raw", session.Save() == futureRaw);

const string invalidRaw = "{\"schemaVersion\":1,\"timelines\":[{\"timelineKey\":\"x\",\"folders\":[{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"start\":8,\"end\":2,\"name\":\"broken\",\"isCollapsed\":true}]}]}";
var sessionInvalid = session.Load(invalidRaw);
Check("session_invalid_quarantined", sessionInvalid.Status == FolderDocumentLoadStatus.Invalid && session.IsRecoveryBlocked);
Check("session_invalid_save_preserves_raw", session.Save() == invalidRaw);

session.Replace(normalized);
Check("session_explicit_replace_clears_quarantine", !session.IsRecoveryBlocked && session.Save() == json);
session.Load(malformedRaw);
session.Reset();
Check("session_explicit_reset_clears_quarantine", !session.IsRecoveryBlocked && session.Save() is null && session.Document == FolderDocument.Empty);

Console.WriteLine($"status=PASS_P3_FOLDER_DOCUMENT\nassertion_count={count}");
