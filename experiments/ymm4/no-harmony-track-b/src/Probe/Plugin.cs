using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class TrackBEntry : ILocalizePlugin
{
    public string Name => "CNWL isolated display stability";
    public void SetCulture(CultureInfo cultureInfo) => ProbeB.Schedule();
}
internal static class ProbeB
{
    private static bool scheduled, failed;
    private static string output = "";
    private static readonly List<string> checks = [];
    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_TRACK_B_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path)) return;
        scheduled = true; output = Path.GetFullPath(path); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }
    private static void Log(string text) => File.AppendAllText(Path.Combine(output, "progress.txt"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
    private static void Check(string name, bool ok)
    {
        checks.Add(name + "=" + ok); Log("assert " + checks[^1]); failed |= !ok;
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
                    var vm = Host.Get(main, "ActiveTimelineViewModel");
                    if (vm is null && !created)
                    {
                        created = true; main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null); return;
                    }
                    if (vm is null) continue;
                    var timeline = Host.Get(vm, "Timeline") as Timeline ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline;
                    if (timeline is null) continue;
                    timer.Stop(); _ = Run(window, vm, timeline); return;
                }
            }
            catch (Exception ex) { timer.Stop(); Fail(ex); Finish(); }
        };
        timer.Start();
    }
    private static async Task Sample(Host host, DirectDisplay display, string tag, int delay = 500)
    {
        display.Phase = tag; await Task.Delay(delay); display.ThrowIfFailed();
        Check(tag + "_geometry", display.GeometryMatches());
        Check(tag + "_extent", Math.Abs(host.Scroll.ExtentHeight - display.ExpectedExtent) < 1.5);
        Check(tag + "_no_reentry", display.Reentries == 0);
        Log($"sample={tag} extent={host.Scroll.ExtentHeight} expected={display.ExpectedExtent} viewport={Host.Reactive(host.Vm, "Viewport")} writes={display.Mutations} applications={display.Applications}");
    }
    private static void Jump(Host host, double top)
    {
        var current = (Rect)(Host.Reactive(host.Vm, "Viewport") ?? throw new InvalidOperationException("Viewport"));
        Host.SetReactive(host.Vm, "Viewport", new Rect(new Point(current.X, top), current.Size));
    }
    private static async Task Run(Window window, object vm, Timeline timeline)
    {
        DirectDisplay? display = null;
        var settings = SettingsBase<YMMSettings>.Default;
        var oldHeight = settings.LayerHeight;
        try
        {
            window.WindowState = WindowState.Normal; window.Left = 0; window.Top = 0; window.Width = 1000; window.Height = 700;
            await Task.Delay(1000);
            var view = Host.Elements(window).Where(x => x.GetType().Name == "TimelineView" && x.IsVisible).OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var scroll = Host.Elements(view).OfType<ScrollViewer>().Where(x => x.IsVisible && x.ActualHeight > 50).OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var source = scroll.Content as FrameworkElement ?? throw new InvalidOperationException("Timeline content");
            var host = new Host(window, timeline, vm, view, source, scroll); host.Activate();
            var character = new Character { Name = "CNWL_TRACK_B" };
            // Keep every item horizontally inside the small native viewport. Only vertical
            // realization is under test; the previous L20 at frame 480 was outside X=0..461.
            var fixtures = new[] { 0, 3, 7, 12, 20 }.Select((layer, i) => (IItem)new VoiceItem(character) { Frame = 40 + i * 30, Layer = layer, Length = 80, Serif = "display", Remark = "CNWL_B_" + layer }).ToArray();
            foreach (var item in fixtures) if (!timeline.TryAddItems([item], item.Frame, item.Layer)) throw new InvalidOperationException("Fixture add");
            timeline.SelectedItems = ImmutableList<IItem>.Empty; await Task.Delay(1000);
            var baseline = fixtures.Select(item => (Item: item, item.Layer, item.Frame)).ToArray();
            var low = fixtures[^1];
            Check("native_viewport_requires_virtualization", scroll.ViewportHeight < low.Layer * oldHeight);
            Log($"native extent={scroll.ExtentHeight} viewport={scroll.ViewportHeight} height={oldHeight}");
            var lowVm = ((TimelineViewModel)vm).Items.Single(x => ReferenceEquals(x.Item, low));
            Log($"low_native_rect left={Host.Get(lowVm, "Left")} width={Host.Get(lowVm, "Width")} top={Host.Get(lowVm, "Top")} viewport={Host.Reactive(vm, "Viewport")}");
            display = new DirectDisplay(host, Log);
            var a = new CollapsedSpan[] { new(2, 3), new(6, 8) };
            var b = new CollapsedSpan[] { new(1, 5), new(2, 3), new(6, 8) };
            display.SetSpans(a); await Sample(host, display, "initial");
            var idleWrites = display.Mutations;
            await Sample(host, display, "idle", 3000);
            Check("idle_no_rewrite_loop", display.Mutations == idleWrites);
            display.Phase = "native_scroll"; scroll.ScrollToVerticalOffset(10 * settings.LayerHeight);
            await Sample(host, display, "native_scroll");
            Jump(host, display.Layout.VisualRowOfLogical(low.Layer) * settings.LayerHeight);
            await Sample(host, display, "folded_viewport");
            Check("low_item_realized_visible", Host.ScreenRect(scroll).Contains(host.Center(low)));
            Check("hidden_items_not_realized", !host.ItemViews().Any(x => Host.Item(x.DataContext) is IItem item && display.Layout.IsHidden(item.Layer)));
            display.Phase = "height_change"; settings.LayerHeight = 40;
            await Sample(host, display, "height40");
            Jump(host, display.Layout.VisualRowOfLogical(low.Layer) * settings.LayerHeight);
            await Sample(host, display, "height40_viewport");
            timeline.RefreshTimelineLengthAndMaxLayer(); await Sample(host, display, "native_refresh");
            for (var cycle = 0; cycle < 8; cycle++)
            {
                display.SetSpans(a); await Sample(host, display, "a_" + cycle, 220);
                display.SetSpans(b); await Sample(host, display, "b_" + cycle, 220);
                display.SetSpans(); await Sample(host, display, "identity_" + cycle, 220);
            }
            settings.LayerHeight = oldHeight; display.SetSpans(b);
            await Sample(host, display, "final_fold");
            Jump(host, display.Layout.VisualRowOfLogical(low.Layer) * settings.LayerHeight);
            await Sample(host, display, "final_viewport");
            Check("item_models_unchanged_before_input", baseline.All(x => x.Item.Layer == x.Layer && x.Item.Frame == x.Frame && timeline.Items.Any(y => ReferenceEquals(y, x.Item))));
            if (failed) throw new InvalidOperationException("Display-only gate failed; input smoke is intentionally skipped");
            Log("phase=late_native_click"); await Native.Click(host.Center(low));
            Check("late_native_click", timeline.SelectedItems.Any(x => ReferenceEquals(x, low)));
            Log("phase=late_right_click");
            var point = host.OffsetScreen(host.Center(low), 110, 0);
            await Native.Click(point, true); await Native.Key(0x1B);
            Check("late_right_click_returned", Host.Reactive(vm, "TimelineCursorPositionWhenRightClick") is Point);
            Log("late_right_native_cursor=" + Host.Reactive(vm, "TimelineCursorPositionWhenRightClick"));
            // Right-click remains native/unmapped here; Track A owns coordinate correction.
            await Sample(host, display, "after_late_clicks");
            display.SetSpans(); await Sample(host, display, "before_detach");
            display.Dispose();
            Check("subscriptions_detached", display.SubscriptionCount == 0);
            var stoppedApplications = display.Applications;
            await Task.Delay(500);
            Check("no_update_after_detach", display.Applications == stoppedApplications);
            Check("content_constraint_restored", double.IsPositiveInfinity(source.MaxHeight));
            Jump(host, low.Layer * settings.LayerHeight); await Task.Delay(400);
            Check("native_geometry_restored", ((TimelineViewModel)vm).Items.All(x => Math.Abs(Convert.ToDouble(Host.Get(x, "Top")) - x.Item.Layer * settings.LayerHeight) < 0.01));
            await Native.Click(host.Center(low));
            Check("native_click_after_detach", timeline.SelectedItems.Any(x => ReferenceEquals(x, low)));
            Check("item_models_unchanged", baseline.All(x => x.Item.Layer == x.Layer && x.Item.Frame == x.Frame) && timeline.Items.Count == fixtures.Length);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Check("no_harmony_reference", !typeof(ProbeB).Assembly.GetReferencedAssemblies().Any(x => x.Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Log($"host_version={typeof(Timeline).Assembly.GetName().Version} applications={display.Applications} mutations={display.Mutations} refreshes={display.CanvasRefreshes}");
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            try { display?.Dispose(); settings.LayerHeight = oldHeight; Native.Release(); }
            catch (Exception ex) { Fail(ex); }
            Finish();
        }
    }
    private static void Fail(Exception ex)
    {
        failed = true; File.AppendAllText(Path.Combine(output, "error.txt"), ex + Environment.NewLine); Log("FAIL " + ex.GetBaseException().Message);
    }
    private static void Finish()
    {
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllLines(temp, new[] { "status=" + (failed ? "FAIL_TRACK_B" : "PASS_TRACK_B"), "assertion_count=" + checks.Count }.Concat(checks));
        File.Move(temp, Path.Combine(output, "result.txt"), true);
    }
}
