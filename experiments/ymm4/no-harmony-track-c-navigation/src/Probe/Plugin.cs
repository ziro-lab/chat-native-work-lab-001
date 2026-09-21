using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class TrackCNavigationEntry : ILocalizePlugin
{
    public string Name => "CNWL integrated folded navigation";
    public void SetCulture(CultureInfo cultureInfo) => NavigationProbe.Schedule();
}

internal static class NavigationProbe
{
    private static bool scheduled, failed;
    private static string output = "";
    private static readonly List<string> checks = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_TRACK_C_NAV_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(path)) return;
        scheduled = true;
        output = Path.GetFullPath(path);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static void Log(string text) =>
        File.AppendAllText(
            Path.Combine(output, "progress.txt"),
            DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);

    private static void Check(string name, bool pass)
    {
        checks.Add(name + "=" + pass);
        failed |= !pass;
        Log("assert " + checks[^1]);
    }

    private static void Bootstrap()
    {
        var created = false;
        var ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100)
                    throw new TimeoutException("Bootstrap timeout.");

                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var vm = Host.Get(main, "ActiveTimelineViewModel");
                    if (vm is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        return;
                    }

                    if (vm is null) continue;

                    var timeline =
                        Host.Get(vm, "Timeline") as Timeline ??
                        vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline;

                    if (timeline is null) continue;

                    timer.Stop();
                    _ = Run(window);
                    return;
                }
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

    private static double VmTop(TimelineViewModel vm, IItem item)
    {
        var itemVm = vm.Items.Single(x => ReferenceEquals(x.Item, item));
        return Convert.ToDouble(Host.Get(itemVm, "Top"));
    }

    private static bool Visible(Host host, IItem item)
    {
        var view = host.ItemViews().FirstOrDefault(x => ReferenceEquals(Host.Item(x.DataContext), item));
        return view is not null &&
               view.ActualWidth > 2 &&
               view.ActualHeight > 2 &&
               Host.ScreenRect(host.Scroll).IntersectsWith(Host.ScreenRect(view));
    }

    private static bool ViewportContains(TimelineViewModel vm, double top, double height)
    {
        var vp = vm.Viewport.Value;
        return top + height > vp.Top - 1 && top < vp.Bottom + 1;
    }

    private static async Task ResetViewport(TimelineViewModel vm)
    {
        var current = vm.Viewport.Value;
        vm.Viewport.Value = new Rect(new Point(current.X, 0), current.Size);
        await Task.Delay(350);
    }

    private static async Task Navigate(
        Host host,
        DirectDisplay display,
        TimelineViewModel vm,
        IItem target,
        string tag)
    {
        display.ThrowIfFailed();
        var expectedTop = display.Layout.VisualRowOfLogical(target.Layer) * (double)display.Height;
        var modelTop = VmTop(vm, target);

        Check(tag + "_model_top", Math.Abs(modelTop - expectedTop) < 0.01);

        vm.ScrollToItem(target);
        await Task.Delay(650);
        display.ThrowIfFailed();

        var actualTop = VmTop(vm, target);
        var vp = vm.Viewport.Value;
        Log($"{tag} viewport={vp} target_top={actualTop} display_row={display.Layout.VisualRowOfLogical(target.Layer)}");

        Check(tag + "_visible", Visible(host, target));
        Check(tag + "_viewport_contains", ViewportContains(vm, actualTop, display.Height));
        Check(tag + "_geometry", display.GeometryMatches());
        Check(tag + "_no_reentry", display.Reentries == 0);
    }

    private static async Task Run(Window window)
    {
        DirectDisplay? display = null;
        var settings = SettingsBase<YMMSettings>.Default;
        var oldHeight = settings.LayerHeight;

        try
        {
            window.WindowState = WindowState.Normal;
            window.Left = 0;
            window.Top = 0;
            window.Width = 1000;
            window.Height = 700;
            await Task.Delay(1200);

            var view = Host.Elements(window)
                .Where(x => x.GetType().Name == "TimelineView" && x.IsVisible)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();

            var vm =
                view.DataContext as TimelineViewModel ??
                throw new InvalidOperationException("Visible TimelineViewModel missing.");

            var timeline =
                Host.Get(vm, "Timeline") as Timeline ??
                vm.GetType().GetField("timeline", Host.Flags)?.GetValue(vm) as Timeline ??
                throw new InvalidOperationException("Visible Timeline model missing.");

            var scroll = Host.Elements(view)
                .OfType<ScrollViewer>()
                .Where(x => x.IsVisible && x.ActualHeight > 50)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .First();

            var source =
                scroll.Content as FrameworkElement ??
                throw new InvalidOperationException("Timeline content missing.");

            var host = new Host(window, timeline, vm, view, source, scroll);
            host.Activate();
            Check("visible_context_bound", ReferenceEquals(vm, view.DataContext));

            var character = new Character { Name = "CNWL_TRACK_C_NAV" };
            VoiceItem Make(string name, int frame, int layer) =>
                new(character)
                {
                    Frame = frame,
                    Layer = layer,
                    Length = 35,
                    Serif = name,
                    Remark = "CNWL_NAV_" + name
                };

            var top = Make("top", 20, 1);
            var owner = Make("owner", 60, 2);
            var hidden = Make("hidden", 100, 3);
            var target9 = Make("target9", 140, 9);
            var target20 = Make("target20", 180, 20);

            var fixtures = new IItem[] { top, owner, hidden, target9, target20 };
            foreach (var item in fixtures)
                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add failed: " + item.Remark);

            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(1000);

            var h = settings.LayerHeight;
            Check("native_requires_vertical_navigation", scroll.ViewportHeight < target20.Layer * h);

            await ResetViewport(vm);
            vm.ScrollToItem(target20);
            await Task.Delay(650);
            var nativeViewport = vm.Viewport.Value;
            Log("native_scroll_target20=" + nativeViewport);
            Check("native_scroll_returned", nativeViewport.Y > 0);

            await ResetViewport(vm);

            display = new DirectDisplay(host, Log);
            var layoutA = new CollapsedSpan[] { new(2, 3), new(6, 8) };
            var layoutB = new CollapsedSpan[] { new(1, 5), new(2, 3), new(6, 8) };

            display.SetSpans(layoutA);
            await Task.Delay(500);
            display.ThrowIfFailed();

            Check("a_geometry", display.GeometryMatches());
            Check("a_extent", Math.Abs(scroll.ExtentHeight - display.ExpectedExtent) < 1.5);

            await ResetViewport(vm);
            await Navigate(host, display, vm, target20, "a_target20");
            Check("a_scroll_compressed", vm.Viewport.Value.Y < nativeViewport.Y);

            await ResetViewport(vm);
            await Navigate(host, display, vm, target9, "a_target9");

            display.SetSpans(layoutB);
            await Task.Delay(500);
            display.ThrowIfFailed();

            Check("b_geometry", display.GeometryMatches());
            Check("b_owner3", display.Layout.OwnerLogical(3) == 1);

            await ResetViewport(vm);
            await Navigate(host, display, vm, target20, "b_target20");

            await ResetViewport(vm);
            await Navigate(host, display, vm, target9, "b_target9");

            settings.LayerHeight = 40;
            await Task.Delay(550);
            display.ThrowIfFailed();
            Check("height40_geometry", display.GeometryMatches());

            await ResetViewport(vm);
            await Navigate(host, display, vm, target20, "height40_target20");

            // Hidden target behavior is observed but not yet promoted to Full semantics.
            await ResetViewport(vm);
            vm.ScrollToItem(hidden);
            await Task.Delay(500);
            display.ThrowIfFailed();
            Log(
                $"hidden_navigation_observation viewport={vm.Viewport.Value} " +
                $"hidden_top={VmTop(vm, hidden)} owner_top={VmTop(vm, owner)} owner_visible={Visible(host, owner)} hidden_visible={Visible(host, hidden)}");
            Check("hidden_navigation_returned", true);
            Check("hidden_remains_hidden", !Visible(host, hidden));

            settings.LayerHeight = oldHeight;
            display.SetSpans();
            await Task.Delay(500);
            display.ThrowIfFailed();
            Check("identity_geometry", display.GeometryMatches());

            display.Dispose();
            Check("display_detached", display.SubscriptionCount == 0);
            display = null;

            await ResetViewport(vm);
            vm.ScrollToItem(target20);
            await Task.Delay(650);
            Check("native_navigation_after_detach", Visible(host, target20));
            Check(
                "native_geometry_restored",
                vm.Items.All(x =>
                    Math.Abs(
                        Convert.ToDouble(Host.Get(x, "Top")) -
                        x.Item.Layer * settings.LayerHeight) < 0.01));
            Check("constraint_restored", double.IsPositiveInfinity(source.MaxHeight));

            Check(
                "no_harmony_loaded",
                !AppDomain.CurrentDomain.GetAssemblies()
                    .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Check(
                "no_harmony_reference",
                !typeof(NavigationProbe).Assembly.GetReferencedAssemblies()
                    .Any(x => x.Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                display?.Dispose();
                settings.LayerHeight = oldHeight;
                Native.Release();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            Finish();
        }
    }

    private static void Fail(Exception ex)
    {
        failed = true;
        File.AppendAllText(Path.Combine(output, "error.txt"), ex + Environment.NewLine);
        Log("FAIL " + ex.GetBaseException().Message);
    }

    private static void Finish()
    {
        var temp = Path.Combine(output, "result.tmp");
        File.WriteAllLines(
            temp,
            new[]
            {
                "status=" + (failed ? "FAIL_TRACK_C_NAVIGATION" : "PASS_TRACK_C_NAVIGATION"),
                "assertion_count=" + checks.Count
            }.Concat(checks));
        File.Move(temp, Path.Combine(output, "result.txt"), true);
    }
}
