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
            await ProbeUndoCollectorAsync(lines);
            await ProbeTimelineRecordCollectorAsync(lines);
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
            "YukkuriMovieMaker.Plugin.Community.ExplorerToolPlugin",
            "YukkuriMovieMaker.Plugin.Community.NotepadToolPlugin"
        })
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName, false)).FirstOrDefault(t => t != null);
            if (type == null) { lines.Add("TOOL_GROUP_MISSING " + typeName); continue; }
            object? instance = null;
            try { instance = Activator.CreateInstance(type); } catch (Exception ex) { lines.Add("TOOL_GROUP_CREATE_FAIL " + typeName + " " + ex.GetType().Name); }
            if (instance is IToolPlugin tool)
                lines.Add($"TOOL_GROUP_VALUE type={type.FullName} name={tool.Name} group={tool.DefaultGroupName} group2={tool.DefaultGroupName2}");
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

    private static async Task ProbeUndoCollectorAsync(ICollection<string> lines)
    {
        lines.Add("SECTION UNDO_COLLECTOR_BEHAVIOR");
        var parent = new UndoRedoManager();
        var collector = new UndoRedoCommandCollector();
        var child = new UndoRedoManager(collector);
        var state = new Box();
        state.Value = 2;
        child.AddCommand(new BoxCommand(state, 0, 1));
        child.AddCommand(new BoxCommand(state, 1, 2));
        lines.Add($"UNDO_CHILD collector_empty_after_two={collector.IsEmpty} parent_undoable_before={parent.IsUndoable}");
        parent.AddCommand(collector);
        lines.Add($"UNDO_PARENT undoable_after_add={parent.IsUndoable} redoable={parent.IsRedoable} state={state.Value}");
        await parent.UndoAsync();
        lines.Add($"UNDO_PARENT after_undo state={state.Value} undoable={parent.IsUndoable} redoable={parent.IsRedoable}");
        Check(state.Value == 0, "child manager commands collected into one parent undo step", lines);
        await parent.RedoAsync();
        lines.Add($"UNDO_PARENT after_redo state={state.Value} undoable={parent.IsUndoable} redoable={parent.IsRedoable}");
        Check(state.Value == 2, "one parent redo restores both collected commands", lines);
    }

    private static async Task ProbeTimelineRecordCollectorAsync(ICollection<string> lines)
    {
        lines.Add("SECTION TIMELINE_RECORD_COLLECTOR");
        var timeline = new Timeline();
        var collector = new UndoRedoCommandCollector();
        var child = new UndoRedoManager(collector);
        var parent = new UndoRedoManager();
        child.Subscribe(timeline);
        var a = new TextItem { Frame = 10, Length = 20, Layer = 2, Text = "A" };
        var b = new TextItem { Frame = 40, Length = 20, Layer = 3, Text = "B" };
        child.Record();
        timeline.Items = timeline.Items.Add(a);
        timeline.RefreshTimelineLengthAndMaxLayer();
        child.Record();
        child.Record();
        timeline.Items = timeline.Items.Remove(a).Add(b);
        timeline.RefreshTimelineLengthAndMaxLayer();
        child.Record();
        lines.Add($"TIMELINE_COLLECTOR empty={collector.IsEmpty} count_now={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)}");
        parent.AddCommand(collector);
        await parent.UndoAsync();
        lines.Add($"TIMELINE_COLLECTOR after_undo count={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)}");
        Check(timeline.Items.Count == 0, "multiple native Record pairs collected into one parent undo step", lines);
        await parent.RedoAsync();
        lines.Add($"TIMELINE_COLLECTOR after_redo count={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)}");
        Check(timeline.Items.Count == 1 && !timeline.Items.Contains(a) && timeline.Items.Contains(b), "one parent redo restores final Timeline trial state", lines);
        child.Unsubscribe(timeline);
    }

    private static void Check(bool value, string message, ICollection<string> lines)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        lines.Add("ASSERT PASS: " + message);
    }

    private sealed class Box { public int Value { get; set; } }
    private sealed class BoxCommand(Box box, int before, int after) : IUndoRedoCommand
    {
        public ValueTask UndoAsync() { box.Value = before; return ValueTask.CompletedTask; }
        public ValueTask RedoAsync() { box.Value = after; return ValueTask.CompletedTask; }
    }
}
