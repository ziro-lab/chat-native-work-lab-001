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
            ProbeLocalizedResourceNames(lines);
            await ProbeSingleRecordTrialSessionAsync(lines);
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
        foreach (var type in new[] { typeof(TransitionItem), typeof(FrameBufferItem), typeof(VoiceItem), typeof(VideoItem), typeof(ImageItem), typeof(AudioItem), typeof(TextItem), typeof(TachieItem), typeof(ShapeItem) })
        {
            try
            {
                if (Activator.CreateInstance(type) is IItem item)
                    lines.Add($"ITEM_LABEL type={type.FullName} label={item.Label}");
                else lines.Add($"ITEM_LABEL_NO_INSTANCE type={type.FullName}");
            }
            catch (Exception ex) { lines.Add($"ITEM_LABEL_CREATE_FAIL type={type.FullName} error={ex.GetType().Name}"); }
        }
    }

    private static void ProbeLocalizedResourceNames(ICollection<string> lines)
    {
        lines.Add("SECTION LOCALIZED_ITEM_RESOURCE_VALUES");
        var texts = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("YukkuriMovieMaker.Resources.Localization.Texts", false))
            .FirstOrDefault(t => t != null);
        if (texts == null) { lines.Add("RESOURCE_TEXTS_MISSING"); return; }

        var cultureProperty = texts.GetProperty("Culture", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var oldCulture = cultureProperty?.CanRead == true ? cultureProperty.GetValue(null) : null;
        try
        {
            if (cultureProperty?.CanWrite == true) cultureProperty.SetValue(null, CultureInfo.GetCultureInfo("ja-JP"));
            foreach (var name in new[] { "TransitionItemName", "FrameBufferItemName", "EffectItemName", "VoiceItemName", "VideoItemName", "ImageItemName", "AudioItemName", "TextItemName", "TachieItemName", "ShapeItemName" })
            {
                var p = texts.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                lines.Add($"RESOURCE_ITEM_NAME property={name} value={p?.GetValue(null) ?? "<missing>"}");
            }
        }
        finally
        {
            if (cultureProperty?.CanWrite == true) cultureProperty.SetValue(null, oldCulture);
        }
    }

    private static async Task ProbeSingleRecordTrialSessionAsync(ICollection<string> lines)
    {
        lines.Add("SECTION UNDO_SINGLE_RECORD_TRIAL_SESSION");
        var timeline = new Timeline();
        var manager = new UndoRedoManager();
        EventHandler<UndoRedoEventArgs> handler = (_, e) => manager.AddCommand(e.Command);
        timeline.UndoRedoCommandCreated += handler;
        try
        {
            var initial = timeline.Items;
            var a = new TextItem { Frame = 10, Length = 20, Layer = 2, Text = "A" };
            var b = new TextItem { Frame = 40, Length = 20, Layer = 3, Text = "B" };

            manager.Record();
            timeline.Items = initial.Add(a);
            timeline.RefreshTimelineLengthAndMaxLayer();
            timeline.Items = initial.Add(b);
            timeline.RefreshTimelineLengthAndMaxLayer();
            manager.Record();

            lines.Add($"TRIAL_RECORD after_close undoable={manager.IsUndoable} redoable={manager.IsRedoable} count={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)}");
            Check(manager.IsUndoable, "closing one native Record session creates one undoable history entry", lines);
            await manager.UndoAsync();
            lines.Add($"TRIAL_RECORD after_undo count={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)} redoable={manager.IsRedoable}");
            Check(timeline.Items.SequenceEqual(initial), "one native Undo returns the whole trial session to its initial state", lines);
            await manager.RedoAsync();
            lines.Add($"TRIAL_RECORD after_redo count={timeline.Items.Count} hasA={timeline.Items.Contains(a)} hasB={timeline.Items.Contains(b)} undoable={manager.IsUndoable}");
            Check(timeline.Items.Count == 1 && !timeline.Items.Contains(a) && timeline.Items.Contains(b), "one native Redo restores only the final trial state", lines);
            lines.Add("UNDO_TRIAL_SESSION_RESULT=ONE_RECORD_INITIAL_TO_FINAL");
        }
        finally
        {
            timeline.UndoRedoCommandCreated -= handler;
        }
    }

    private static void Check(bool value, string message, ICollection<string> lines)
    {
        if (!value) throw new InvalidOperationException("ASSERT FAIL: " + message);
        lines.Add("ASSERT PASS: " + message);
    }
}
