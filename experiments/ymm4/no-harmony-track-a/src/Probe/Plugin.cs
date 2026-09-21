using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL no-Harmony isolated Track A";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}
internal static class Probe
{
    private static bool scheduled;
    private static string output = "";
    private static readonly List<string> checks = [];
    private static bool failed;
    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_TRACK_A_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true; output = Path.GetFullPath(dir); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }
    private static void Bootstrap()
    {
        var ticks = 0; var created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100) throw new TimeoutException("MainViewModel bootstrap");
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = Host.Get(main, "ActiveTimelineViewModel");
                    if (active is null && !created)
                    {
                        created = true; main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null); return;
                    }
                    if (active is null) continue;
                    var timeline = Host.Get(active, "Timeline") as Timeline ?? active.GetType().GetField("timeline", Host.Flags)?.GetValue(active) as Timeline;
                    if (timeline is null) continue;
                    timer.Stop(); _ = Run(window, active, timeline); return;
                }
            }
            catch (Exception ex) { timer.Stop(); Fail(ex); Finish(); }
        };
        timer.Start();
    }
    private static void Check(string name, bool pass)
    {
        checks.Add(name + "=" + pass); Log("assert " + checks[^1]); failed |= !pass;
    }
    private static void Log(string message) => File.AppendAllText(Path.Combine(output, "progress.txt"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
    private static void Live(string phase, Timeline timeline, IItem[] fixtures)
    {
        var items = timeline.Items.ToArray();
        var valid = items.Length == fixtures.Length && fixtures.All(expected => items.Any(actual => ReferenceEquals(expected, actual)));
        Log(phase + " live=" + string.Join("|", items.Select(x => $"{x.Remark}:L{x.Layer}:F{x.Frame}")));
        Check(phase + "_live_identity", valid);
        if (!valid) throw new InvalidOperationException(phase + ": Undo/Redo changed fixture membership; retained object fields are not evidence");
    }
    private static void RecordFixture(Window window)
    {
        // Harness preparation only. No Record/Clear is injected into gestures or the adapter.
        // MainViewModel.model is a known exact harness bridge; products should obtain
        // the manager from public TimelineToolInfo.UndoRedoManager instead.
        var main = window.DataContext;
        var model = main.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(main)
            ?? throw new MissingMemberException("MainViewModel.model");
        var manager = Host.Get(model, "UndoRedoManager") ?? throw new MissingMemberException("MainModel.UndoRedoManager");
        if (manager.GetType().FullName != "YukkuriMovieMaker.UndoRedo.UndoRedoManager") throw new InvalidOperationException("Unexpected history manager");
        var record = manager.GetType().GetMethod("Record", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException("UndoRedoManager.Record()");
        Log("phase=fixture_record_before");
        record.Invoke(manager, null);
        Log("phase=fixture_record_after undoable=" + Host.Get(manager, "IsUndoable"));
        Check("fixture_record_boundary", true);
    }
    private static async Task Run(Window window, object vm, Timeline t)
    {
        FoldInputAdapter? adapter = null;
        FrameworkElement? root = null; object? oldScale = null;
        try
        {
            window.WindowState = WindowState.Maximized;
            await Task.Delay(900);
            // Fixture accommodation, not a product display change or virtualization proof.
            root = window.Content as FrameworkElement ?? throw new InvalidOperationException("Window content");
            oldScale = root.ReadLocalValue(FrameworkElement.LayoutTransformProperty);
            root.SetCurrentValue(FrameworkElement.LayoutTransformProperty, new ScaleTransform(0.5, 0.5));
            await Task.Delay(700);
            var view = Host.Elements(window).Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var scroll = Host.Elements(view).OfType<ScrollViewer>().Where(x => x.IsVisible && x.ActualHeight > 50)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var source = scroll.Content as FrameworkElement ?? throw new InvalidOperationException("Timeline content");
            var host = new Host(window, t, vm, view, source, scroll); host.Activate();
            var h = SettingsBase<YMMSettings>.Default.LayerHeight;
            Log($"fixture scale=0.5 height={h} viewport={Host.Reactive(vm, "Viewport")} scroll={Host.ScreenRect(scroll)}");
            var a = FolderLayout.Create(10, [new(2, 3), new(6, 8)]);
            var b = FolderLayout.Create(10, [new(1, 5), new(2, 3), new(6, 8)]);
            Check("map_a", a.VisibleCsv == "0,1,2,4,5,6,9,10" && a.DisplayRowToLogical(6) == 9 && a.OwnerLogical(3) == 2 && a.OwnerLogical(7) == 6);
            Check("map_b", b.VisibleCsv == "0,1,6,9,10" && b.OwnerLogical(3) == 1 && b.OwnerLogical(4) == 1 && b.OwnerLogical(7) == 6);
            var character = new Character { Name = "CNWL_TRACK_A" };
            VoiceItem Make(string name, int frame, int layer) => new(character) { Frame = frame, Layer = layer, Length = 70, Remark = "CNWL_A_" + name, Serif = name };
            var drag = Make("drag", 80, 6); var other = Make("other", 420, 6); var target = Make("target", 260, 9);
            var child = Make("child", 30, 2); var hidden = Make("hidden", 120, 3); var tail = Make("tail", 600, 10);
            var fixtures = new IItem[] { drag, other, target, child, hidden, tail };
            foreach (var item in fixtures) if (!t.TryAddItems([item], item.Frame, item.Layer)) throw new InvalidOperationException("Fixture add " + item.Remark);
            t.CurrentFrame = 0; t.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(1400);
            RecordFixture(window);
            Live("fixture", t, fixtures);
            host.Activate();
            var original = (drag.Layer, drag.Frame);
            var originalOther = (other.Layer, other.Frame);
            Log("phase=baseline_drag");
            var start = host.Center(drag);
            await Native.Drag(start, host.OffsetScreen(start, 64, h));
            var nativeDelta = drag.Frame - original.Frame;
            Live("baseline_drag", t, fixtures);
            Check("baseline_native_drag", drag.Layer == 7 && nativeDelta > 0);
            Log("native_frame_delta=" + nativeDelta);
            await Native.Key(0x5A, true);
            Live("baseline_undo", t, fixtures);
            Check("baseline_undo", (drag.Layer, drag.Frame) == original);
            if ((drag.Layer, drag.Frame) != original) throw new InvalidOperationException("Unfolded native control did not undo one gesture");
            t.SelectedItems = ImmutableList<IItem>.Empty;
            Log("phase=install_track_a");
            adapter = new FoldInputAdapter(host, h, a, Log);
            await Task.Delay(400);
            Check("a_native_geometry_untouched", fixtures.All(x => Math.Abs(host.ModelTop(x) - x.Layer * h) < 0.01));
            Check("a_visual_geometry", Math.Abs(host.LocalTop(target) - 6 * h) < 2 && Math.Abs(host.LocalTop(drag) - 5 * h) < 2);
            Check("hidden_not_hittable", !host.ItemView(hidden).IsHitTestVisible);
            Log("phase=normal_click_a");
            var clickState = (target.Layer, target.Frame);
            await Native.Click(host.Center(target));
            Check("a_normal_click", t.SelectedItems.Any(x => ReferenceEquals(x, target)) && (target.Layer, target.Frame) == clickState);
            Check("a_click_no_layer_write", adapter.Corrections == 0);
            Log("phase=right_a"); await Right(host, target, h, "a");
            Log("phase=marquee_a");
            t.SelectedItems = ImmutableList<IItem>.Empty;
            var frameBefore = t.CurrentFrame;
            var bounds = Host.ScreenRect(host.ItemView(target));
            var tl = source.PointFromScreen(bounds.TopLeft); var br = source.PointFromScreen(bounds.BottomRight);
            await Native.Drag(source.PointToScreen(new Point(tl.X - 12, tl.Y + 4)), source.PointToScreen(new Point(br.X + 12, br.Y - 4)), true);
            Check("marquee_exact_target", adapter.MarqueeHandled && t.SelectedItems.Count == 1 && ReferenceEquals(t.SelectedItems[0], target));
            Check("marquee_no_seek", t.CurrentFrame == frameBefore);
            t.SelectedItems = ImmutableList<IItem>.Empty;
            Log("phase=nonuniform_drag");
            start = host.Center(drag);
            await Native.Drag(start, host.OffsetScreen(start, 64, h));
            var final = (drag.Layer, drag.Frame);
            Live("single_drag", t, fixtures);
            Check("single_L6_to_L9", drag.Layer == 9 && adapter.Corrections > 0);
            Check("single_native_frame_preserved", drag.Frame - original.Frame == nativeDelta);
            await Native.Key(0x5A, true); Live("single_undo", t, fixtures); Check("single_undo", (drag.Layer, drag.Frame) == original);
            await Native.Key(0x59, true); Live("single_redo", t, fixtures); Check("single_redo", (drag.Layer, drag.Frame) == final);
            await Native.Key(0x5A, true); Live("single_reset", t, fixtures);
            Check("single_reset", (drag.Layer, drag.Frame) == original);
            Log("phase=multi_drag");
            t.SelectItems([drag, other]); await Task.Delay(150);
            start = host.Center(drag);
            await Native.Drag(start, host.OffsetScreen(start, 64, h));
            var finalOther = (other.Layer, other.Frame); final = (drag.Layer, drag.Frame);
            Live("multi_drag", t, fixtures);
            Check("multi_nonuniform_layers", drag.Layer == 9 && other.Layer == 9);
            Check("multi_native_frames", drag.Frame - original.Frame == nativeDelta && other.Frame - originalOther.Frame == nativeDelta);
            await Native.Key(0x5A, true); Live("multi_undo", t, fixtures);
            Check("multi_undo", (drag.Layer, drag.Frame) == original && (other.Layer, other.Frame) == originalOther);
            await Native.Key(0x59, true); Live("multi_redo", t, fixtures);
            Check("multi_redo", (drag.Layer, drag.Frame) == final && (other.Layer, other.Frame) == finalOther);
            await Native.Key(0x5A, true); Live("multi_reset", t, fixtures);
            Check("multi_reset", (drag.Layer, drag.Frame) == original && (other.Layer, other.Frame) == originalOther);
            Log("phase=parent_collapse");
            adapter.SetLayout(b); await Task.Delay(350);
            Check("b_visual_geometry", Math.Abs(host.LocalTop(target) - 3 * h) < 2);
            Check("b_child_hidden", !host.ItemView(child).IsHitTestVisible && !host.ItemView(hidden).IsHitTestVisible);
            t.SelectedItems = ImmutableList<IItem>.Empty;
            var corrections = adapter.Corrections;
            await Native.Click(host.Center(target));
            Check("b_normal_click", t.SelectedItems.Any(x => ReferenceEquals(x, target)) && (target.Layer, target.Frame) == clickState);
            Check("b_click_no_layer_write", corrections == adapter.Corrections);
            Log("phase=right_b"); await Right(host, target, h, "b");
            Check("right_routes_observed", adapter.RightMaps == 2);
            Check("b_native_geometry_untouched", fixtures.All(x => Math.Abs(host.ModelTop(x) - x.Layer * h) < 0.01));
            Log("phase=detach"); adapter.Dispose(); adapter = null; await Task.Delay(250);
            Check("identity_visual_restored", fixtures.All(x => Math.Abs(host.LocalTop(x) - x.Layer * h) < 2 && host.ItemView(x).IsHitTestVisible));
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Native.Click(host.Center(target));
            Check("native_click_after_detach", t.SelectedItems.Any(x => ReferenceEquals(x, target)));
            Live("teardown", t, fixtures);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Check("no_harmony_loaded", !assemblies.Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Check("no_harmony_reference", !typeof(Probe).Assembly.GetReferencedAssemblies().Any(x => x.Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Log("host_version=" + typeof(Timeline).Assembly.GetName().Version);
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            try
            {
                adapter?.Dispose(); Native.Release();
                if (root is not null && oldScale is not null)
                {
                    if (oldScale == DependencyProperty.UnsetValue) root.ClearValue(FrameworkElement.LayoutTransformProperty);
                    else root.SetValue(FrameworkElement.LayoutTransformProperty, oldScale);
                }
            }
            catch (Exception ex) { Fail(ex); }
            Finish();
        }
    }
    private static async Task Right(Host host, IItem target, int height, string tag)
    {
        var box = Host.ScreenRect(host.ItemView(target));
        var local = host.Source.PointFromScreen(new Point(box.Right, box.Y + box.Height / 2));
        await Native.Click(host.Source.PointToScreen(new Point(local.X + 50, local.Y)), true);
        var raw = Host.Reactive(host.Vm, "TimelineCursorPositionWhenRightClick");
        Check(tag + "_right_cursor", raw is Point point && (int)Math.Floor(point.Y / height) == 9);
        Check(tag + "_add_converter", raw is Point p && Host.ConverterLayer(p, height) == 9);
        await Native.Key(0x1B);
    }
    private static void Fail(Exception ex)
    {
        failed = true;
        File.AppendAllText(Path.Combine(output, "error.txt"), ex + Environment.NewLine);
        Log("FAIL " + ex.GetBaseException().Message);
    }
    private static void Finish()
    {
        var temporary = Path.Combine(output, "result.tmp");
        File.WriteAllLines(temporary, new[] { "status=" + (failed ? "FAIL_TRACK_A" : "PASS_TRACK_A"), "assertion_count=" + checks.Count }.Concat(checks));
        File.Move(temporary, Path.Combine(output, "result.txt"), true);
    }
}
