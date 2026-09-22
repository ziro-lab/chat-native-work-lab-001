using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ymm4NoHarmonyFolderRanges;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class P2NavigationEntry : ILocalizePlugin
{
    public string Name => "CNWL P2 selection navigation";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    private static readonly FolderRange[] Baseline =
    [
        new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, 10),
        new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 2, 5),
        new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 16, 20)
    ];
    private static bool scheduled;
    private static bool failed;
    private static string output = "";
    private static readonly Dictionary<string, bool> checks = [];
    private static readonly Dictionary<string, string> facts = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_P2_NAVIGATION_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path)) return;
        scheduled = true;
        output = Path.GetFullPath(path);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static void Bootstrap()
    {
        var ticks = 0;
        var created = false;
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
                    if (vm is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        return;
                    }
                    if (vm is null) continue;
                    timer.Stop();
                    _ = Run(window);
                    return;
                }
            }
            catch (Exception ex) { timer.Stop(); Fail(ex); Finish(); }
        };
        timer.Start();
    }

    private static void Log(string text) => File.AppendAllText(Path.Combine(output, "progress.txt"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
    private static void Check(string name, bool pass)
    {
        checks.Add(name, pass);
        failed |= !pass;
        Log($"assert {name}={pass}");
    }
    private static void Fact(string name, object? value) => facts[name] = value?.ToString() ?? "<null>";
    private static void Fail(Exception ex)
    {
        failed = true;
        File.AppendAllText(Path.Combine(output, "error.txt"), ex + Environment.NewLine);
        Log("failure=" + ex);
    }
    private static void Finish()
    {
        var result = new { status = failed ? "FAIL_P2_NAVIGATION" : "PASS_P2_NAVIGATION", checks, facts };
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path.Combine(output, "result.json"));
    }
    private static async Task Wait(string name, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > 5000) throw new TimeoutException(name);
            await Task.Delay(100);
        }
        await Task.Delay(350);
    }
    private static bool Visible(Host host, IItem item)
    {
        var view = host.ItemViews().FirstOrDefault(x => ReferenceEquals(Host.Item(x.DataContext), item));
        if (view is null) return false;
        var box = Host.ScreenRect(view);
        return box.Width > 2 && box.Height > 2 && Host.ScreenRect(host.Scroll).Contains(new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
    }
    private static string State(Timeline timeline) => string.Join("|", timeline.Items.OrderBy(x => x.Remark).Select(x => $"{x.Remark}:L{x.Layer}:F{x.Frame}:N{x.Length}"));

    private static async Task Run(Window window)
    {
        DirectDisplay? display = null;
        SelectionNavigationBridge? navigation = null;
        try
        {
            window.WindowState = WindowState.Normal;
            window.Left = 0; window.Top = 0; window.Width = 1000; window.Height = 700;
            await Task.Delay(1000);
            var view = Host.Elements(window).Where(x => x.GetType().Name == "TimelineView" && x.IsVisible).OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var vm = view.DataContext as TimelineViewModel ?? throw new InvalidOperationException("TimelineViewModel missing");
            var timeline = Host.Get(vm, "Timeline") as Timeline ?? vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline ?? throw new InvalidOperationException("Timeline missing");
            var scroll = Host.Elements(view).OfType<ScrollViewer>().Where(x => x.IsVisible && x.ActualHeight > 50).OrderByDescending(x => x.ActualWidth * x.ActualHeight).First();
            var host = new Host(window, timeline, vm, view, scroll.Content as FrameworkElement ?? throw new InvalidOperationException("Timeline content missing"), scroll);
            host.Activate();
            Native.Release();
            Check("visible_context_bound", ReferenceEquals(vm, view.DataContext));
            Fact("host_version", typeof(Timeline).Assembly.GetName().Version);
            Fact("public_navigation_surface", string.Join(" | ", vm.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(x => x.Name.Contains("Scroll", StringComparison.Ordinal) || x.Name.Contains("Viewport", StringComparison.Ordinal)).Select(x => x.ToString())));

            var character = new Character { Name = "CNWL_P2_NAV" };
            var items = new Dictionary<int, IItem>();
            foreach (var layer in new[] { 0, 1, 2, 4, 12, 16, 20, 45, 60 })
            {
                var item = new VoiceItem(character) { Frame = 20, Layer = layer, Length = 60, Serif = "L" + layer, Remark = "CNWL_P2_NAV_L" + layer };
                if (!timeline.TryAddItems([item], item.Frame, item.Layer)) throw new InvalidOperationException("Fixture add " + layer);
                items.Add(layer, item);
            }
            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            timeline.CurrentFrame = 20;
            await Task.Delay(700);
            IReadOnlyList<FolderRange> collapsed = Baseline;
            display = new DirectDisplay(host, Log);
            var activeDisplay = display;
            async Task Reset()
            {
                timeline.SelectedItems = ImmutableList<IItem>.Empty;
                collapsed = Baseline;
                activeDisplay.SetSpans(Baseline.Select(x => new CollapsedSpan(x.Start, x.End)).ToArray());
                scroll.ScrollToVerticalOffset(0);
                await Task.Delay(650);
                activeDisplay.ThrowIfFailed();
            }
            await Reset();
            var original = State(timeline);
            Check("fixture_hidden_nested_target", display.Layout.IsHidden(4));
            timeline.SelectedItems = ImmutableList.Create(items[4]);
            await Task.Delay(650);
            Check("raw_selection_keeps_target_folded", display.Layout.IsHidden(4));
            Fact("raw_selection_target_visible", Visible(host, items[4]));
            await Reset();

            navigation = new SelectionNavigationBridge(host, display, () => collapsed, x => collapsed = x, Log);
            var activeNavigation = navigation;
            var horizontalBefore = scroll.HorizontalOffset;
            timeline.SelectedItems = ImmutableList.Create(items[4]);
            await Wait("nested selection reveal", () => activeNavigation.Failure is not null || (!activeDisplay.Layout.IsHidden(4) && Visible(host, items[4])));
            Check("selection_event_observed", navigation.SelectionSignals > 0);
            Check("nested_selection_revealed", !display.Layout.IsHidden(4) && Visible(host, items[4]));
            Check("unrelated_folder_stays_collapsed", collapsed.SequenceEqual(new[] { Baseline[2] }));
            Check("navigation_preserves_selection_seek", timeline.SelectedItems.Count == 1 && ReferenceEquals(timeline.SelectedItems[0], items[4]) && timeline.CurrentFrame == 20);
            Check("navigation_preserves_horizontal", Math.Abs(scroll.HorizontalOffset - horizontalBefore) < 1);

            await Reset();
            timeline.SelectedItems = ImmutableList.Create(items[2]);
            await Wait("nested owner reveal", () => activeNavigation.Failure is not null || (!activeDisplay.Layout.IsHidden(2) && Visible(host, items[2])));
            Check("nested_owner_keeps_own_collapse", collapsed.SequenceEqual(new[] { Baseline[1], Baseline[2] }));
            Check("nested_owner_visible", Visible(host, items[2]));

            await Reset();
            Check("far_target_initially_offscreen", !Visible(host, items[45]));
            timeline.SelectedItems = ImmutableList.Create(items[45]);
            await Wait("visible offscreen selection", () => activeNavigation.Failure is not null || Visible(host, items[45]));
            Check("offscreen_selection_followed", Visible(host, items[45]));
            Check("visible_target_preserves_collapses", collapsed.SequenceEqual(Baseline));

            await Reset();
            var appliedBefore = navigation.Applied;
            timeline.SelectedItems = ImmutableList.Create(items[4]);
            timeline.SelectedItems = ImmutableList.Create(items[12]);
            await Wait("rapid selection", () => activeNavigation.Failure is not null || activeNavigation.Applied > appliedBefore);
            Check("latest_selection_wins", ReferenceEquals(timeline.SelectedItems.Single(), items[12]) && Visible(host, items[12]));
            Check("stale_hidden_request_does_not_expand", collapsed.SequenceEqual(Baseline));

            await Reset();
            appliedBefore = navigation.Applied;
            timeline.SelectedItems = ImmutableList.Create(items[4], items[45]);
            await Task.Delay(650);
            Check("ambiguous_multi_selection_not_guessed", navigation.Applied == appliedBefore && collapsed.SequenceEqual(Baseline));

            await Reset();
            timeline.SelectedItems = ImmutableList.Create(items[4]);
            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(650);
            Check("cleared_selection_cancels_pending", collapsed.SequenceEqual(Baseline));

            timeline.SelectedItems = ImmutableList.Create(items[4]);
            navigation.Dispose();
            var signalsAfterDetach = navigation.SelectionSignals;
            timeline.SelectedItems = ImmutableList.Create(items[2]);
            await Task.Delay(650);
            Check("detach_cancels_pending", collapsed.SequenceEqual(Baseline));
            Check("detach_unsubscribes", navigation.SelectionSignals == signalsAfterDetach);
            Check("item_geometry_unchanged", State(timeline) == original);
            Check("no_navigation_failure", navigation.Failure is null);
            Check("no_display_failure_or_reentry", display.Failure is null && display.Reentries == 0);
            Check("no_harmony_loaded", !AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Fact("navigation_applied", navigation.Applied);
            Fact("selection_signals", navigation.SelectionSignals);
            display.Dispose();
            Check("display_subscriptions_released", display.SubscriptionCount == 0);
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            try { navigation?.Dispose(); display?.Dispose(); Native.Release(); }
            catch (Exception ex) { Fail(ex); }
            Finish();
        }
    }
}
