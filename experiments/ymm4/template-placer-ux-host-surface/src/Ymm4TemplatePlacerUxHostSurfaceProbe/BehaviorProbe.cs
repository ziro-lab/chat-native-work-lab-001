using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4TemplatePlacerUxHostSurfaceProbe;

public sealed class BehaviorProbeEntry : ILocalizePlugin
{
    public string Name => "CNWL — Template Placer UX behavior";
    private static int scheduled;
    public void SetCulture(CultureInfo cultureInfo)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_TP_UX_SURFACE_DIR");
        if (Interlocked.Exchange(ref scheduled, 1) != 0 || string.IsNullOrWhiteSpace(dir)) return;
        Application.Current.Dispatcher.BeginInvoke(new Action(async () => await RunAsync(dir)), DispatcherPriority.ApplicationIdle);
    }

    private static async Task RunAsync(string dir)
    {
        var lines = new List<string>();
        try
        {
            lines.Add("status=PASS_TEMPLATE_PLACER_UX_BEHAVIOR");
            ProbeToolGroups(lines);
            ProbeItemLabels(lines);
            await ProbePropertyCommandConnectionAsync(lines);
            File.WriteAllLines(Path.Combine(dir, "behavior.txt"), lines, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            lines.Add("error=" + ex);
            File.WriteAllLines(Path.Combine(dir, "behavior.txt"), lines, new UTF8Encoding(false));
        }
    }

    private static void ProbeToolGroups(ICollection<string> lines)
    {
        lines.Add("SECTION TOOL_GROUP_VALUES");
        foreach (var typeName in new[] {
            "YukkuriMovieMaker.Plugin.Community.Tool.Explorer.ExplorerToolPlugin",
            "YukkuriMovieMaker.Plugin.Community.Tool.Notepad.NotepadToolPlugin"
        })
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName, false)).FirstOrDefault(t => t != null);
            if (type == null) { lines.Add("TOOL_GROUP_MISSING " + typeName); continue; }
            object? instance = null;
            try { instance = Activator.CreateInstance(type); } catch (Exception ex) { lines.Add("TOOL_GROUP_CREATE_FAIL " + typeName + " " + ex.GetType().Name); }
            if (instance is IToolPlugin tool)
                lines.Add($"TOOL_GROUP_VALUE type={type.FullName} name={tool.Name} group={tool.DefaultGroupName} order={tool.DefaultOrder}");
        }
    }

    private static void ProbeItemLabels(ICollection<string> lines)
    {
        lines.Add("SECTION ITEM_LABEL_VALUES");
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { oldUi, CultureInfo.GetCultureInfo("ja-JP") })
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                foreach (var type in new[] { typeof(TransitionItem), typeof(FrameBufferItem), typeof(VoiceItem), typeof(VideoItem), typeof(ImageItem), typeof(AudioItem), typeof(TextItem), typeof(TachieItem), typeof(ShapeItem) })
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IItem item)
                            lines.Add($"ITEM_LABEL culture={culture.Name} type={type.FullName} label={item.Label}");
                        else lines.Add($"ITEM_LABEL_NO_INSTANCE culture={culture.Name} type={type.FullName}");
                    }
                    catch (Exception ex) { lines.Add($"ITEM_LABEL_CREATE_FAIL culture={culture.Name} type={type.FullName} error={ex.GetType().Name}"); }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    private static async Task ProbePropertyCommandConnectionAsync(ICollection<string> lines)
    {
        lines.Add("SECTION UNDO_PUBLIC_CONNECTION_BEHAVIOR");
        var timeline = new Timeline();
        var manager = new UndoRedoManager();
        var initial = timeline.Items;
        var a = new TextItem { Frame = 10, Length = 20, Layer = 2, Text = "A" };
        var b = new TextItem { Frame = 40, Length = 20, Layer = 3, Text = "B" };
        var stateA = initial.Add(a);
        var stateB = initial.Add(b);

        timeline.Items = stateA;
        timeline.RefreshTimelineLengthAndMaxLayer();
        manager.AddCommand(new UndoRedoPropertyChangedCommand<Timeline, ImmutableList<IItem>>(timeline, nameof(Timeline.Items), initial, stateA));
        timeline.Items = stateB;
        timeline.RefreshTimelineLengthAndMaxLayer();
        manager.AddCommand(new UndoRedoPropertyChangedCommand<Timeline, ImmutableList<IItem>>(timeline, nameof(Timeline.Items), stateA, stateB));
        lines.Add($"UNDO_CONNECT before undoable={manager.IsUndoable} current_count={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)}");

        await manager.UndoAsync();
        var connected = timeline.Items.SequenceEqual(initial);
        var oneUndoState = connected ? "initial" : timeline.Items.SequenceEqual(stateA) ? "first_trial" : "other";
        lines.Add($"UNDO_CONNECT after_one_undo state={oneUndoState} connected={connected} count={timeline.Items.Count}");

        if (connected)
        {
            await manager.RedoAsync();
            var oneRedoFinal = timeline.Items.SequenceEqual(stateB);
            lines.Add($"UNDO_CONNECT after_one_redo final={oneRedoFinal} count={timeline.Items.Count}");
            Check(oneRedoFinal, "connected public property commands redo directly to final trial state", lines);
        }
        else
        {
            Check(timeline.Items.SequenceEqual(stateA), "without connection one undo reaches the immediately previous trial state", lines);
            await manager.UndoAsync();
            Check(timeline.Items.SequenceEqual(initial), "without connection a second undo reaches the initial state", lines);
            await manager.RedoAsync();
            await manager.RedoAsync();
            Check(timeline.Items.SequenceEqual(stateB), "unconnected public commands still restore the final state after two redo steps", lines);
        }

        lines.Add("UNDO_CONNECT_RESULT=" + (connected ? "CONNECTED_ONE_STEP" : "NOT_CONNECTED"));
    }

    private static void Check(bool value, string message, ICollection<string> lines)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        lines.Add("ASSERT PASS: " + message);
    }
}
