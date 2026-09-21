using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class TrackCEntry : ILocalizePlugin
{
    public string Name => "CNWL integrated fold interaction";
    public void SetCulture(CultureInfo cultureInfo) => ProbeC.Schedule();
}
internal static class ProbeC
{
    private static bool scheduled, failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_TRACK_C_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path)) return;
        scheduled = true; output = Path.GetFullPath(path); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }
    private static void Log(string text) => File.AppendAllText(Path.Combine(output, "progress.txt"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
    private static void Check(string name, bool ok) { checks.Add(name + "=" + ok); failed |= !ok; Log("assert " + checks[^1]); }
    private static void Bootstrap()
    {
        var ticks = 0; var created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100) throw new TimeoutException("Bootstrap");
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var vm = Host.Get(main, "ActiveTimelineViewModel");
                    if (vm is null && !created) { created = true; main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null); return; }
                    if (vm is null) continue;
                    var timeline = Host.Get(vm, "Timeline") as Timeline ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline;
                    if (timeline is null) continue;
                    timer.Stop(); _ = Run(window, vm, timeline); return;
                }
            }
            catch (Exception ex) { timer.Stop(); Fail(ex); Finish(); }
        }; timer.Start();
    }
    private static void Live(string name, Timeline t, IItem[] fixtures)
    {
        var valid = t.Items.Count == fixtures.Length && fixtures.All(x => t.Items.Any(y => ReferenceEquals(x, y)));
        Check(name + "_live", valid);
        if (!valid) throw new InvalidOperationException(name + " changed fixture identity/count");
    }
    private static async Task Reveal(Host host, double top)
    {
        if (!ReferenceEquals(host.Vm, host.View.DataContext)) throw new InvalidOperationException("Visible Timeline context changed during probe");
        var current = (Rect)(Host.Reactive(host.Vm, "Viewport") ?? throw new InvalidOperationException("Viewport"));
        Host.SetReactive(host.Vm, "Viewport", new Rect(new Point(current.X, top), current.Size));
        await Task.Delay(450);
    }
    private static async Task Sample(Host host, DirectDisplay display, string name)
    {
        display.Phase = name; await Task.Delay(350); display.ThrowIfFailed();
        Check(name + "_geometry", display.GeometryMatches());
        Check(name + "_extent", Math.Abs(host.Scroll.ExtentHeight - display.ExpectedExtent) < 1.5);
        Check(name + "_no_reentry", display.Reentries == 0);
    }
    private static async Task Right(Host host, IItem item, int expected, string name, int height)
    {
        Log("phase=" + name);
        var p = host.OffsetScreen(host.Center(item), 65, 0);
        await Native.Click(p, true);
        var position = Host.Reactive(host.Vm, "TimelineCursorPositionWhenRightClick");
        Check(name + "_cursor", position is Point point && (int)Math.Floor(point.Y / height) == expected);
        Check(name + "_converter", position is Point mapped && Host.ConverterLayer(mapped, height) == expected);
        await Native.Key(0x1B);
    }
    private static async Task Run(Window window, object vm, Timeline t)
    {
        DirectDisplay? display = null; InputMapAdapter? input = null;
        try
        {
            window.WindowState = WindowState.Normal; window.Left = 0; window.Top = 0; window.Width = 1000; window.Height = 700;
            await Task.Delay(1200);
            var view = Host.Elements(window).Where(x => x.GetType().Name == "TimelineView" && x.IsVisible).OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            // A layout/startup replacement can leave Bootstrap's ActiveTimelineViewModel
            // stale even while the actual displayed view uses the same Timeline model.
            // Bind the exact visible context BEFORE creating or recording any fixtures.
            Log("bootstrap_vm_same_as_visible=" + ReferenceEquals(vm, view.DataContext));
            vm = view.DataContext is TimelineViewModel visible ? visible : throw new InvalidOperationException("Visible Timeline VM missing");
            t = Host.Get(vm, "Timeline") as Timeline ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline ?? throw new InvalidOperationException("Visible Timeline model missing");
            var scroll = Host.Elements(view).OfType<ScrollViewer>().Where(x => x.IsVisible && x.ActualHeight > 50).OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var host = new Host(window, t, vm, view, scroll.Content as FrameworkElement ?? throw new InvalidOperationException("Content"), scroll);
            host.Activate(); Check("visible_context_bound", ReferenceEquals(vm, view.DataContext));
            var character = new Character { Name = "CNWL_TRACK_C" };
            VoiceItem Make(string name, int frame, int layer) => new(character) { Frame = frame, Layer = layer, Length = 30, Serif = name, Remark = "CNWL_C_" + name };
            var drag = Make("drag", 40, 6); var other = Make("other", 220, 6); var target = Make("target", 140, 9);
            var child = Make("child", 10, 2); var hidden = Make("hidden", 80, 3); var tail = Make("tail", 280, 10); var low = Make("low", 160, 20);
            var fixtures = new IItem[] { drag, other, target, child, hidden, tail, low };
            foreach (var item in fixtures) if (!t.TryAddItems([item], item.Frame, item.Layer)) throw new InvalidOperationException("Fixture add");
            t.SelectedItems = ImmutableList<IItem>.Empty; await Task.Delay(1000);
            var model = window.DataContext.GetType().GetField("model", Host.Flags)?.GetValue(window.DataContext) ?? throw new MissingMemberException("MainViewModel.model");
            var history = Host.Get(model, "UndoRedoManager") ?? throw new MissingMemberException("UndoRedoManager");
            history.GetType().GetMethod("Record", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)!.Invoke(history, null);
            var h = YukkuriMovieMaker.Settings.YMMSettings.Default.LayerHeight;
            Check("unscaled_virtualization", scroll.ViewportHeight < low.Layer * h);
            var original = (drag.Layer, drag.Frame); var originalOther = (other.Layer, other.Frame);
            await Reveal(host, 6 * h); Log("phase=native_control");
            var start = host.Center(drag); await Native.Drag(start, host.OffsetScreen(start, 32, h));
            var delta = drag.Frame - original.Frame;
            Live("control", t, fixtures); Check("native_control", drag.Layer == 7 && delta > 0);
            await Native.Key(0x5A, true); Live("control_undo", t, fixtures);
            Check("native_control_undo", (drag.Layer, drag.Frame) == original);
            if ((drag.Layer, drag.Frame) != original) throw new InvalidOperationException("Native control failed");
            Log("native_frame_delta=" + delta);
            display = new DirectDisplay(host, Log);
            var a = new CollapsedSpan[] { new(2, 3), new(6, 8) };
            var b = new CollapsedSpan[] { new(1, 5), new(2, 3), new(6, 8) };
            display.SetSpans(a); await Sample(host, display, "attach_a");
            input = new InputMapAdapter(host, display, Log);
            await Reveal(host, display.Layout.VisualRowOfLogical(6) * h);
            var targetState = (target.Layer, target.Frame);
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Native.Click(host.Center(target));
            Check("native_click_a", t.SelectedItems.Any(x => ReferenceEquals(x, target)));
            Check("click_no_layer_write", input.Corrections == 0 && (target.Layer, target.Frame) == targetState);
            await Right(host, target, 9, "right_before_drag", h);
            await Reveal(host, display.Layout.VisualRowOfLogical(6) * h);
            t.SelectedItems = ImmutableList<IItem>.Empty;
            var beforeFrame = t.CurrentFrame; var box = Host.ScreenRect(host.ItemView(target));
            var top = host.Source.PointFromScreen(box.TopLeft); var bottom = host.Source.PointFromScreen(box.BottomRight);
            Log("phase=marquee");
            await Native.Drag(host.Source.PointToScreen(new Point(top.X - 10, top.Y + 3)), host.Source.PointToScreen(new Point(bottom.X + 10, bottom.Y - 3)), true);
            Check("marquee_exact_target", input.Marquees == 1 && t.SelectedItems.Count == 1 && ReferenceEquals(t.SelectedItems[0], target));
            Check("marquee_no_seek", beforeFrame == t.CurrentFrame);
            await Reveal(host, display.Layout.VisualRowOfLogical(6) * h);
            t.SelectedItems = ImmutableList<IItem>.Empty;
            Log("phase=integrated_single_drag"); start = host.Center(drag);
            await Native.Drag(start, host.OffsetScreen(start, 32, h)); await Sample(host, display, "single");
            var final = (drag.Layer, drag.Frame);
            Live("single", t, fixtures); Check("single_L6_to_L9", drag.Layer == 9 && input.Corrections > 0);
            Check("single_native_frame", drag.Frame - original.Frame == delta);
            await Native.Key(0x5A, true); Live("single_undo", t, fixtures); Check("single_undo", (drag.Layer, drag.Frame) == original);
            await Native.Key(0x59, true); Live("single_redo", t, fixtures); Check("single_redo", (drag.Layer, drag.Frame) == final);
            await Native.Key(0x5A, true); Live("single_reset", t, fixtures); Check("single_reset", (drag.Layer, drag.Frame) == original);
            await Sample(host, display, "single_history");
            await Reveal(host, display.Layout.VisualRowOfLogical(6) * h);
            t.SelectItems([drag, other]); await Task.Delay(200);
            Log("phase=integrated_multi_drag"); start = host.Center(drag);
            await Native.Drag(start, host.OffsetScreen(start, 32, h)); await Sample(host, display, "multi");
            final = (drag.Layer, drag.Frame); var otherFinal = (other.Layer, other.Frame);
            Live("multi", t, fixtures); Check("multi_L6_to_L9", drag.Layer == 9 && other.Layer == 9);
            Check("multi_native_frames", drag.Frame - original.Frame == delta && other.Frame - originalOther.Frame == delta);
            await Native.Key(0x5A, true); Live("multi_undo", t, fixtures); Check("multi_undo", (drag.Layer, drag.Frame) == original && (other.Layer, other.Frame) == originalOther);
            await Native.Key(0x59, true); Live("multi_redo", t, fixtures); Check("multi_redo", (drag.Layer, drag.Frame) == final && (other.Layer, other.Frame) == otherFinal);
            await Native.Key(0x5A, true); Live("multi_reset", t, fixtures); Check("multi_reset", (drag.Layer, drag.Frame) == original && (other.Layer, other.Frame) == originalOther);
            await Sample(host, display, "multi_history");
            await Reveal(host, display.Layout.VisualRowOfLogical(6) * h);
            await Right(host, target, 9, "right_after_drag", h);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                Check("gesture_closed_" + cycle, !input.GestureActive);
                display.SetSpans(b); await Sample(host, display, "nested_" + cycle);
                await Reveal(host, display.Layout.VisualRowOfLogical(6) * h);
                t.SelectedItems = ImmutableList<IItem>.Empty;
                await Native.Click(host.Center(target)); Check("nested_click_" + cycle, t.SelectedItems.Any(x => ReferenceEquals(x, target)));
                await Right(host, target, 9, "nested_right_" + cycle, h);
                display.SetSpans(a); await Sample(host, display, "reopen_" + cycle);
            }
            await Reveal(host, display.Layout.VisualRowOfLogical(20) * h);
            Check("low_realized_at_folded_row", Host.ScreenRect(scroll).Contains(host.Center(low)));
            await Native.Click(host.Center(low)); Check("low_native_click", t.SelectedItems.Any(x => ReferenceEquals(x, low)));
            Check("no_hidden_view", !host.ItemViews().Any(x => Host.Item(x.DataContext) is IItem item && display.Layout.IsHidden(item.Layer)));
            Check("gesture_preview_sampled", display.GestureSamples > 0);
            Check("gesture_previews_remained_visible", display.MissingGestureViews == 0);
            var idleWrites = display.Mutations; await Task.Delay(1500); display.ThrowIfFailed();
            Check("integrated_idle_stable", display.Mutations == idleWrites);
            Live("before_detach", t, fixtures);
            input.Dispose(); input = null;
            display.SetSpans(); await Sample(host, display, "identity");
            display.Dispose(); Check("display_subscriptions_detached", display.SubscriptionCount == 0);
            await Reveal(host, 6 * h);
            t.SelectedItems = ImmutableList<IItem>.Empty;
            start = host.Center(drag); await Native.Drag(start, host.OffsetScreen(start, 32, h));
            Check("native_drag_after_detach", drag.Layer == 7 && drag.Frame - original.Frame == delta);
            await Native.Key(0x5A, true); Live("after_detach_undo", t, fixtures);
            Check("native_undo_after_detach", (drag.Layer, drag.Frame) == original);
            Check("native_geometry_restored", ((TimelineViewModel)vm).Items.All(x => Math.Abs(Convert.ToDouble(Host.Get(x, "Top")) - x.Item.Layer * h) < 0.01));
            Check("constraint_restored", double.IsPositiveInfinity(host.Source.MaxHeight));
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Check("no_harmony_reference", !typeof(ProbeC).Assembly.GetReferencedAssemblies().Any(x => x.Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Log($"host={typeof(Timeline).Assembly.GetName().Version} apps={display.Applications} writes={display.Mutations}");
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            try { input?.Dispose(); display?.Dispose(); Native.Release(); } catch (Exception ex) { Fail(ex); }
            Finish();
        }
    }
    private static void Fail(Exception ex) { failed = true; File.AppendAllText(Path.Combine(output, "error.txt"), ex + Environment.NewLine); Log("FAIL " + ex.GetBaseException().Message); }
    private static void Finish()
    {
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllLines(temp, new[] { "status=" + (failed ? "FAIL_TRACK_C_INPUT" : "PASS_TRACK_C_INPUT"), "assertion_count=" + checks.Count }.Concat(checks));
        File.Move(temp, Path.Combine(output, "result.txt"), true);
    }
}
