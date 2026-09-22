using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class P4MetadataHistoryEntry : ILocalizePlugin
{
    public string Name => "CNWL P4 metadata history";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_METADATA_HISTORY_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static object? PublicProperty(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    private static void Bootstrap()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        var ticks = 0;
        var created = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (++ticks > 120) throw new TimeoutException("Bootstrap");
                var window = Application.Current.Windows.Cast<Window>()
                    .FirstOrDefault(x => x.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (window?.DataContext is not { } root) return;

                var active = PublicProperty(root, "ActiveTimelineViewModel");
                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        root.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(root, null);
                    }
                    return;
                }

                timer.Stop();
                await Run(window, root);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Fail(ex);
                Finish();
            }
        };
        timer.Start();
    }

    private static void Check(string name, bool pass)
    {
        checks[name] = pass;
        failed |= !pass;
    }

    private static void Fact(string name, object? value) =>
        facts[name] = value?.ToString() ?? "<null>";

    private static void Fail(Exception ex)
    {
        failed = true;
        facts["error"] = ex.ToString();
    }

    private static async Task Run(Window window, object root)
    {
        try
        {
            var active = PublicProperty(root, "ActiveTimelineViewModel")
                ?? throw new InvalidOperationException("ActiveTimelineViewModel missing.");
            var timeline = PublicProperty(active, "Timeline") as Timeline
                ?? active.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline
                ?? throw new InvalidOperationException("Timeline missing.");

            var model = root.GetType().GetField(
                    "model",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(root)
                ?? throw new MissingMemberException(root.GetType().FullName, "model");
            var manager = model.GetType().GetProperty(
                    "UndoRedoManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(model) as UndoRedoManager
                ?? throw new MissingMemberException(model.GetType().FullName, "UndoRedoManager");

            var save = root.GetType().GetMethod(
                    "SaveProject",
                    BindingFlags.Instance | BindingFlags.Public,
                    [typeof(string)])
                ?? throw new MissingMethodException(root.GetType().FullName, "SaveProject");
            var baselinePath = Path.Combine(output, "p4-metadata-baseline.ymmp");
            save.Invoke(root, [baselinePath]);
            await Task.Delay(700);
            Check("baseline_saved", File.Exists(baselinePath));
            Check("baseline_reports_saved", PublicProperty(root, "IsSaved") is true);

            var folderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            FolderDocument current = FolderDocumentRules.NormalizeAndValidate(new FolderDocument
            {
                Timelines =
                [
                    new TimelineFolderState
                    {
                        TimelineKey = timeline.ID.ToString("D"),
                        Folders =
                        [
                            new PersistedFolder
                            {
                                Id = folderId,
                                Start = 2,
                                End = 5,
                                Name = "History",
                                IsCollapsed = false
                            }
                        ]
                    }
                ]
            });

            var before = current;
            var after = FolderUxCommands.ToggleCollapsed(before, timeline.ID.ToString("D"), folderId);
            Check("pure_change_is_metadata_only",
                FolderDocumentRules.FindTimeline(before, timeline.ID.ToString("D"))!.Folders.Single().IsCollapsed == false
                && FolderDocumentRules.FindTimeline(after, timeline.ID.ToString("D"))!.Folders.Single().IsCollapsed == true);

            var recorded = 0;
            var undone = 0;
            var redone = 0;
            EventHandler recordedHandler = (_, _) => recorded++;
            EventHandler undoHandler = (_, _) => undone++;
            EventHandler redoHandler = (_, _) => redone++;
            manager.Recorded += recordedHandler;
            manager.Undoed += undoHandler;
            manager.Redoed += redoHandler;

            try
            {
                current = after;
                manager.AddCommand(new UndoRedoActionCommand(
                    () => current = before,
                    () => current = after));
                manager.Record();
                await Task.Delay(500);

                Check("metadata_recorded_once", recorded == 1);
                Check("metadata_marks_project_unsaved", PublicProperty(root, "IsSaved") is false);
                Check("metadata_forward_state", FolderDocumentCodec.Save(current) == FolderDocumentCodec.Save(after));

                window.Activate();
                Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                await Native.Key(0x5A, ctrl: true); // Ctrl+Z
                Check("metadata_undo_callback_once", undone == 1);
                Check("metadata_undo_restores_document", FolderDocumentCodec.Save(current) == FolderDocumentCodec.Save(before));

                await Native.Key(0x59, ctrl: true); // Ctrl+Y
                Check("metadata_redo_callback_once", redone == 1);
                Check("metadata_redo_restores_document", FolderDocumentCodec.Save(current) == FolderDocumentCodec.Save(after));

                var savedPath = Path.Combine(output, "p4-metadata-after.ymmp");
                save.Invoke(root, [savedPath]);
                await Task.Delay(700);
                Check("save_after_metadata_succeeds", File.Exists(savedPath));
                Check("save_after_metadata_reports_saved", PublicProperty(root, "IsSaved") is true);
            }
            finally
            {
                manager.Recorded -= recordedHandler;
                manager.Undoed -= undoHandler;
                manager.Redoed -= redoHandler;
            }

            Check("timeline_identity_unchanged", timeline.ID != Guid.Empty);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));

            Fact("recorded", recorded);
            Fact("undone", undone);
            Fact("redone", redone);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }

        Finish();
    }

    private static void Finish()
    {
        var result = new
        {
            status = failed ? "FAIL_P4_METADATA_HISTORY" : "PASS_P4_METADATA_HISTORY",
            hostVersion = typeof(Timeline).Assembly.GetName().Version?.ToString(),
            checks,
            facts
        };
        var tmp = Path.Combine(output, "result.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, Path.Combine(output, "result.json"), true);
    }
}
