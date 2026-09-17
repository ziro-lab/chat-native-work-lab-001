using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4TemplatePlacerUxHostSurfaceProbe;

// Detached public-only behavioral probe: no application projects, internal collectors or history clearing.
public sealed class BoundaryProbeEntry : ILocalizePlugin
{
    public string Name => "CNWL — native trial session boundaries";
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
            await ExternalAsync(lines, "TIMELINE", false);
            await ExternalAsync(lines, "ITEM", false);
            await ExternalAsync(lines, "ITEM", true);
            await OpenUndoAsync(lines);
            lines.Add("BOUNDARY_OBSERVATION=COMPLETE");
        }
        catch (Exception ex) { lines.Add("BOUNDARY_OBSERVATION=FAIL"); lines.Add("error=" + ex); }
        File.WriteAllLines(Path.Combine(dir, "boundaries.txt"), lines, new UTF8Encoding(false));
    }
    private static async Task ExternalAsync(List<string> lines, string kind, bool timelineOnly)
    {
        var prefix = kind + (timelineOnly ? "_BUBBLE" : "_DIRECT");
        lines.Add("SECTION " + prefix);
        var t = new Timeline(); var m = new UndoRedoManager();
        var original = new TextItem { Frame = 10, Length = 20, Layer = 2, Text = "original" };
        var a = new TextItem { Frame = 40, Length = 20, Layer = 3, Text = "A" };
        var b = new TextItem { Frame = 40, Length = 20, Layer = 3, Text = "B" };
        var other = new TextItem { Frame = 70, Length = 20, Layer = 4, Text = "external" };
        t.Items = [original]; var initial = t.Items;
        var open = false; var own = false; var closing = false;
        void Trace(string s) => lines.Add(prefix + " " + s);
        EventHandler<UndoRedoEventArgs> route = (_, e) => { Trace("ADD before; open=" + open); m.AddCommand(e.Command); Trace("ADD after"); };
        PropertyChangingEventHandler changing = (sender, e) =>
        {
            Trace("CHANGING " + (ReferenceEquals(sender, t) ? "timeline" : "item") + ":" + e.PropertyName + " open=" + open + " own=" + own);
            if (!open || own || closing) return;
            closing = true; open = false; Trace("BOUNDARY before Record"); m.Record(); Trace("BOUNDARY after Record"); closing = false;
        };
        t.UndoRedoCommandCreated += route;
        t.PropertyChanging += changing;
        if (!timelineOnly) original.PropertyChanging += changing;
        m.Recorded += (_, _) => Trace("RECORDED");
        m.UndoRedoCommandCreated += (_, _) => Trace("MANAGER COMMAND");
        m.HistoryChanged += (_, _) => Trace("HISTORY");
        m.Undoed += (_, _) => Trace("UNDOED");
        m.Redoed += (_, _) => Trace("REDOED");
        try
        {
            m.Record(); open = true; own = true;
            t.Items = initial.Add(a); t.RefreshTimelineLengthAndMaxLayer();
            t.Items = initial.Add(b); t.RefreshTimelineLengthAndMaxLayer(); own = false;
            if (kind == "TIMELINE") t.Items = t.Items.Add(other); else original.Text = "external";
            t.RefreshTimelineLengthAndMaxLayer(); m.Record(); open = false;
            await m.UndoAsync();
            var separate = t.Items.SequenceEqual(initial.Add(b)) && original.Text == "original";
            Trace("RESULT FIRST_UNDO_ONLY_EXTERNAL=" + separate);
            if (m.IsUndoable) await m.UndoAsync(); else Trace("SECOND_UNDO unavailable (negative result, not a probe crash)");
            Trace("RESULT SECOND_UNDO_INITIAL=" + (separate && t.Items.SequenceEqual(initial) && original.Text == "original"));
            if (m.IsRedoable) await m.RedoAsync(); if (m.IsRedoable) await m.RedoAsync();
            Trace("RESULT REDO_EXTERNAL=" + (kind == "TIMELINE" ? t.Items.Contains(other) : original.Text == "external"));
        }
        finally { t.UndoRedoCommandCreated -= route; t.PropertyChanging -= changing; original.PropertyChanging -= changing; }
    }
    private static async Task OpenUndoAsync(List<string> lines)
    {
        lines.Add("SECTION OPEN_NATIVE_UNDO");
        var t = new Timeline(); var m = new UndoRedoManager();
        var initial = t.Items; var a = new TextItem { Frame = 10, Length = 20, Layer = 2, Text = "A" };
        var b = new TextItem { Frame = 10, Length = 20, Layer = 2, Text = "B" };
        var open = false;
        EventHandler<UndoRedoEventArgs> route = (_, e) => { lines.Add("OPEN ADD open=" + open); m.AddCommand(e.Command); };
        t.UndoRedoCommandCreated += route;
        m.Recorded += (_, _) => { lines.Add("OPEN RECORDED open=" + open); open = false; };
        m.Undoed += (_, _) => lines.Add("OPEN UNDOED open=" + open);
        m.Redoed += (_, _) => lines.Add("OPEN REDOED open=" + open);
        t.PropertyChanging += (_, e) => lines.Add("OPEN CHANGING " + e.PropertyName + " open=" + open);
        try
        {
            m.Record(); open = true; t.Items = initial.Add(a); t.RefreshTimelineLengthAndMaxLayer();
            t.Items = initial.Add(b); t.RefreshTimelineLengthAndMaxLayer();
            lines.Add("OPEN BEFORE UndoAsync undoable=" + m.IsUndoable);
            try { await m.UndoAsync(); } catch (InvalidOperationException ex) { lines.Add("OPEN DIRECT_UNDO_REJECTED=" + ex.Message); }
            lines.Add("OPEN RESULT UNDO_INITIAL=" + t.Items.SequenceEqual(initial) + " open=" + open);
            if (m.IsRedoable) await m.RedoAsync();
            lines.Add("OPEN RESULT REDO_FINAL=" + t.Items.SequenceEqual(initial.Add(b)) + " open=" + open);
        }
        finally { t.UndoRedoCommandCreated -= route; }
    }
}
