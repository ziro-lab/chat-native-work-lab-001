using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4TemplateIdentityProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Template Identity Probe";
    public void SetCulture(CultureInfo cultureInfo) => IdentityProof.Schedule();
}

internal static class IdentityProof
{
    private static bool scheduled;
    private static string output = "";
    private static string phase = "";
    private static readonly Guid SharedSceneId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string PersistName = "CNWL Persist Same";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_TEMPLATE_ID_DIR");
        phase = Environment.GetEnvironmentVariable("CNWL_YMM4_TEMPLATE_ID_PHASE") ?? "";
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(phase)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    private static void Start()
    {
        var ticks = 0;
        var projectCreated = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                    if (active == null && !projectCreated)
                    {
                        projectCreated = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        break;
                    }
                    var timeline = active?.GetType().GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if (timeline == null) continue;
                    timer.Stop();
                    if (phase.Equals("write", StringComparison.OrdinalIgnoreCase)) WritePhase(timeline);
                    else if (phase.Equals("read", StringComparison.OrdinalIgnoreCase)) ReadPhase();
                    else PhaseResult("unknown", "FAIL_UNKNOWN_PHASE", ["phase=" + phase]);
                    return;
                }
                if (ticks >= 90) { timer.Stop(); PhaseResult(phase, "FAIL_TIMEOUT", []); }
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output, $"error-{phase}.txt"), ex.ToString(), new UTF8Encoding(false));
                PhaseResult(phase, "FAIL_EXCEPTION", ["message=" + ex.GetBaseException().Message]);
            }
        };
        timer.Start();
    }

    private static void WritePhase(Timeline timeline)
    {
        foreach (var old in ItemSettings.Default.Templates.Where(x => x.Name == PersistName).ToArray()) ItemSettings.Default.Templates.Remove(old);

        var character = new Character { Name = "CNWL_TemplateIdentity" };
        var t1 = new ItemTemplate(ItemTemplateGroup.TachieFaceItem, PersistName,
            new IItem[] { new TachieFaceItem(character) { Frame = 11, Length = 21, Layer = 7 } }, timeline, SharedSceneId);
        var t2 = new ItemTemplate(ItemTemplateGroup.TachieFaceItem, PersistName,
            new IItem[] { new TachieFaceItem(character) { Frame = 12, Length = 22, Layer = 8 } }, timeline, SharedSceneId);
        ItemSettings.Default.Templates.Add(t1);
        ItemSettings.Default.Templates.Add(t2);

        var sb = new StringBuilder();
        DumpObject(sb, "WRITE_TEMPLATE1", t1);
        DumpObject(sb, "WRITE_TEMPLATE2", t2);
        DumpType(sb, "ITEM_SETTINGS", ItemSettings.Default.GetType());
        File.WriteAllText(Path.Combine(output, "surface-write.txt"), sb.ToString(), new UTF8Encoding(false));

        ItemSettings.Default.Save();
        var matches = ItemSettings.Default.Templates.Where(x => x.Name == PersistName).ToArray();
        PhaseResult("write", "PASS_WRITE_SAVED",
        [
            $"write_count={matches.Length}",
            $"same_name={matches.Length == 2 && matches.All(x => x.Name == PersistName)}",
            $"same_scene_id={matches.Length == 2 && matches.All(x => x.SceneId == SharedSceneId)}",
            $"same_path={matches.Length == 2 && matches.Select(PathKey).Distinct().Count() == 1}",
            "paths=" + string.Join("|", matches.Select(PathKey)),
            "lengths=" + string.Join(",", matches.Select(x => x.Items.Single().Length).OrderBy(x => x))
        ]);
    }

    private static void ReadPhase()
    {
        var matches = ItemSettings.Default.Templates.Where(x => x.Name == PersistName).ToArray();
        var sb = new StringBuilder();
        for (var i = 0; i < matches.Length; i++) DumpObject(sb, "READ_TEMPLATE" + (i + 1), matches[i]);
        File.WriteAllText(Path.Combine(output, "surface-read.txt"), sb.ToString(), new UTF8Encoding(false));

        var sameScene = matches.Length == 2 && matches.All(x => x.SceneId == SharedSceneId);
        var samePath = matches.Length == 2 && matches.Select(PathKey).Distinct().Count() == 1;
        var lengths = matches.Select(x => x.Items.Single().Length).OrderBy(x => x).ToArray();
        var contentRecovered = lengths.SequenceEqual(new[] { 21, 22 });
        var onlyGuidPropertyIsSceneId = typeof(ItemTemplate).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.PropertyType == typeof(Guid) && x.CanRead).Select(x => x.Name).SequenceEqual(new[] { "SceneId" });
        var pass = matches.Length == 2 && sameScene && samePath && contentRecovered && onlyGuidPropertyIsSceneId;
        PhaseResult("read", pass ? "PASS_RESTART_AMBIGUITY" : "FAIL_RESTART_ASSERTION",
        [
            $"read_count={matches.Length}",
            $"same_name_after_restart={matches.Length == 2 && matches.All(x => x.Name == PersistName)}",
            $"same_scene_id_after_restart={sameScene}",
            $"same_path_after_restart={samePath}",
            $"content_recovered={contentRecovered}",
            $"only_public_guid_property_is_scene_id={onlyGuidPropertyIsSceneId}",
            "scene_ids=" + string.Join(",", matches.Select(x => x.SceneId)),
            "paths=" + string.Join("|", matches.Select(PathKey)),
            "lengths=" + string.Join(",", lengths)
        ]);
    }

    private static string PathKey(ItemTemplate template) => string.Join("/", template.Path ?? []);

    private static void DumpObject(StringBuilder sb, string title, object instance)
    {
        sb.AppendLine("=== " + title + " " + instance.GetType().FullName + " ===");
        foreach (var p in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
        {
            try { sb.AppendLine($"PROPERTY {p.PropertyType.FullName} {p.Name} = {Safe(p.GetValue(instance))}"); }
            catch (Exception ex) { sb.AppendLine($"PROPERTY {p.Name} = <throw {ex.GetBaseException().GetType().Name}>"); }
        }
    }

    private static void DumpType(StringBuilder sb, string title, Type type)
    {
        sb.AppendLine("=== " + title + " " + type.FullName + " ===");
        foreach (var m in type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                     .Where(m => new[] { "template", "save", "write", "serialize", "store", "path", "file", "id", "guid", "key" }
                         .Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(m => m.MemberType).ThenBy(m => m.Name)) sb.AppendLine(m.ToString());
    }

    private static string Safe(object? v) => v switch
    {
        null => "<null>",
        string s => '"' + s + '"',
        string[] a => "[" + string.Join(",", a) + "]",
        Guid g => g.ToString(),
        System.Collections.ICollection c => $"<{v.GetType().FullName} Count={c.Count}>",
        _ => v.GetType().IsPrimitive || v is Enum ? v.ToString() ?? "" : $"<{v.GetType().FullName}>"
    };

    private static void PhaseResult(string phaseName, string status, IEnumerable<string> details)
        => File.WriteAllLines(Path.Combine(output, $"result-{phaseName}.txt"), new[] { "status=" + status }.Concat(details), new UTF8Encoding(false));
}
