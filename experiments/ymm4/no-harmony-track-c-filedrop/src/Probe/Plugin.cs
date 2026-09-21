using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class TrackCFileDropEntry : ILocalizePlugin
{
    public string Name => "CNWL integrated fold FileDrop";
    public void SetCulture(CultureInfo cultureInfo) => ProbeDrop.Schedule();
}

internal static class DropNative
{
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    internal const uint LeftDown = 2, LeftUp = 4;
}

internal static class ProbeDrop
{
    private static bool scheduled, failed;
    private static string output = "";
    private static readonly List<string> checks = [];

    internal static void Schedule()
    {
        var path = Environment.GetEnvironmentVariable("CNWL_TRACK_C_FILEDROP_DIR");
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
        var ticks = 0;
        var created = false;
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
                    _ = Run(window, vm, timeline);
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

    private static void RecordFixture(Window window)
    {
        var main = window.DataContext;
        var model =
            main.GetType().GetField("model", Host.Flags)?.GetValue(main) ??
            throw new MissingMemberException("MainViewModel.model");

        var manager =
            Host.Get(model, "UndoRedoManager") ??
            throw new MissingMemberException("MainModel.UndoRedoManager");

        var record =
            manager.GetType().GetMethod(
                "Record",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null) ??
            throw new MissingMethodException("UndoRedoManager.Record()");

        record.Invoke(manager, null);
        Log("fixture_recorded");
    }

    private static async Task Reveal(Host host, int top)
    {
        var current =
            (Rect)(Host.Reactive(host.Vm, "Viewport") ??
            throw new InvalidOperationException("Viewport missing."));

        Host.SetReactive(
            host.Vm,
            "Viewport",
            new Rect(
                new Point(current.X, Math.Max(0, top)),
                current.Size));

        await Task.Delay(450);
    }

    private static async Task Run(Window window, object bootstrapVm, Timeline bootstrapTimeline)
    {
        DirectDisplay? display = null;
        IntegratedDropAdapter? drop = null;

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

            var character = new Character { Name = "CNWL_TRACK_C_DROP" };
            VoiceItem Make(string name, int frame, int layer) =>
                new(character)
                {
                    Frame = frame,
                    Layer = layer,
                    Length = 30,
                    Serif = name,
                    Remark = "CNWL_DROP_" + name
                };

            var head = Make("head", 20, 2);
            var hidden = Make("hidden", 80, 3);
            var target = Make("target", 130, 9);
            var tail = Make("tail", 260, 10);
            var fixtures = new IItem[] { head, hidden, target, tail };

            foreach (var item in fixtures)
                if (!timeline.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Fixture add failed: " + item.Remark);

            timeline.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(1000);
            RecordFixture(window);

            display = new DirectDisplay(host, Log);
            var layoutA = new CollapsedSpan[] { new(2, 3), new(6, 8) };
            var layoutB = new CollapsedSpan[] { new(1, 5), new(2, 3), new(6, 8) };

            display.SetSpans(layoutA);
            await Task.Delay(500);
            display.ThrowIfFailed();

            Check("a_geometry", display.GeometryMatches());
            Check("a_extent", Math.Abs(scroll.ExtentHeight - display.ExpectedExtent) < 1.5);

            drop = new IntegratedDropAdapter(host, display, Log);

            var a = await DropAtTarget(window, host, display, drop, target, "a", 9);
            Check("a_enter", drop.DragEnterSeen);
            Check("a_over", drop.DragOverSeen);
            Check("a_drop", drop.DropSeen);
            Check("a_command", a.CommandExecuted);
            Check("a_added_one", a.AddedCount == 1);
            Check("a_logical_layer", a.LogicalLayer == 9);
            Check("a_final_layer", a.AddedCount == 1 && a.AddedItems.All(x => x.Layer == 9));
            if (a.AddedCount != 1)
                throw new InvalidOperationException($"Layout A added {a.AddedCount} items; expected exactly one.");

            await Task.Delay(500);
            display.ThrowIfFailed();
            Check("a_display_geometry", display.GeometryMatches());
            Check("a_display_extent", Math.Abs(scroll.ExtentHeight - display.ExpectedExtent) < 1.5);

            var aItem = a.AddedItems.Single();
            Check(
                "a_visual_row",
                Math.Abs(host.ModelTop(aItem) - display.Layout.VisualRowOfLogical(9) * display.Height) < 0.01);

            await Native.Key(0x5A, true);
            await Task.Delay(700);
            Check("a_undo_removed", !timeline.Items.Any(x =>
                ReferenceEquals(x, aItem) ||
                string.Equals(
                    IntegratedDropAdapter.FilePathOf(x),
                    a.FilePath,
                    StringComparison.OrdinalIgnoreCase)));

            await Native.Key(0x59, true);
            await Task.Delay(700);
            var aRedo = timeline.Items.FirstOrDefault(x =>
                string.Equals(
                    IntegratedDropAdapter.FilePathOf(x),
                    a.FilePath,
                    StringComparison.OrdinalIgnoreCase));

            Check("a_redo_restored", aRedo is not null);
            Check("a_redo_layer", aRedo?.Layer == 9);
            display.ThrowIfFailed();

            await Native.Key(0x5A, true);
            await Task.Delay(600);
            Check("a_cleanup_undo", !timeline.Items.Any(x =>
                string.Equals(
                    IntegratedDropAdapter.FilePathOf(x),
                    a.FilePath,
                    StringComparison.OrdinalIgnoreCase)));

            display.SetSpans(layoutB);
            await Task.Delay(500);
            display.ThrowIfFailed();

            Check("b_geometry", display.GeometryMatches());
            Check("b_nested_owner", display.Layout.OwnerLogical(3) == 1);
            Check("b_target_row", display.Layout.VisualRowOfLogical(9) == 3);

            var b = await DropAtTarget(window, host, display, drop, target, "b", 9);
            Check("b_enter", drop.DragEnterSeen);
            Check("b_over", drop.DragOverSeen);
            Check("b_drop", drop.DropSeen);
            Check("b_command", b.CommandExecuted);
            Check("b_added_one", b.AddedCount == 1);
            Check("b_logical_layer", b.LogicalLayer == 9);
            Check("b_final_layer", b.AddedCount == 1 && b.AddedItems.All(x => x.Layer == 9));
            if (b.AddedCount != 1)
                throw new InvalidOperationException($"Layout B added {b.AddedCount} items; expected exactly one.");

            await Task.Delay(500);
            display.ThrowIfFailed();
            var bItem = b.AddedItems.Single();

            Check(
                "b_visual_row",
                Math.Abs(host.ModelTop(bItem) - display.Layout.VisualRowOfLogical(9) * display.Height) < 0.01);
            Check("b_display_geometry", display.GeometryMatches());

            await Native.Key(0x5A, true);
            await Task.Delay(700);
            Check("b_undo_removed", !timeline.Items.Any(x =>
                ReferenceEquals(x, bItem) ||
                string.Equals(
                    IntegratedDropAdapter.FilePathOf(x),
                    b.FilePath,
                    StringComparison.OrdinalIgnoreCase)));

            await Native.Key(0x59, true);
            await Task.Delay(700);
            var bRedo = timeline.Items.FirstOrDefault(x =>
                string.Equals(
                    IntegratedDropAdapter.FilePathOf(x),
                    b.FilePath,
                    StringComparison.OrdinalIgnoreCase));

            Check("b_redo_restored", bRedo is not null);
            Check("b_redo_layer", bRedo?.Layer == 9);
            await Task.Delay(400);
            display.ThrowIfFailed();

            Check("b_redo_geometry", display.GeometryMatches());
            Check("post_corrections_bounded", drop.PostCorrections <= 2);
            Check("fixtures_live", fixtures.All(x => timeline.Items.Any(y => ReferenceEquals(x, y))));
            Check("hidden_not_realized", !host.ItemViews().Any(x =>
                Host.Item(x.DataContext) is IItem item &&
                display.Layout.IsHidden(item.Layer)));

            var applications = display.Applications;
            await Task.Delay(1200);
            display.ThrowIfFailed();
            Check("drop_idle_stable", display.Applications <= applications + 1);

            drop.Dispose();
            drop = null;

            display.SetSpans();
            await Task.Delay(400);
            Check("identity_geometry", display.GeometryMatches());
            display.Dispose();
            Check("display_detached", display.SubscriptionCount == 0);

            Check(
                "native_geometry_restored",
                vm.Items.All(x =>
                    Math.Abs(
                        Convert.ToDouble(Host.Get(x, "Top")) -
                        x.Item.Layer * display.Height) < 0.01));

            Check("constraint_restored", double.IsPositiveInfinity(source.MaxHeight));
            Check(
                "no_harmony_loaded",
                !AppDomain.CurrentDomain.GetAssemblies()
                    .Any(x => x.GetName().Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));
            Check(
                "no_harmony_reference",
                !typeof(ProbeDrop).Assembly.GetReferencedAssemblies()
                    .Any(x => x.Name?.Contains("Harmony", StringComparison.OrdinalIgnoreCase) == true));

            Log(
                $"host={typeof(Timeline).Assembly.GetName().Version} " +
                $"applications={display.Applications} writes={display.Mutations} " +
                $"drop_post_corrections={drop?.PostCorrections ?? -1}");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            try
            {
                drop?.Dispose();
                display?.Dispose();
                Native.Release();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            Finish();
        }
    }

    private static async Task<IntegratedDropResult> DropAtTarget(
        Window mainWindow,
        Host host,
        DirectDisplay display,
        IntegratedDropAdapter adapter,
        IItem anchor,
        string label,
        int expectedLogicalLayer)
    {
        var h = display.Height;
        var foldedY = display.Layout.VisualRowOfLogical(expectedLogicalLayer) * h;
        await Reveal(host, Math.Max(0, (int)foldedY));

        var anchorBox = Host.ScreenRect(host.ItemView(anchor));
        var scrollBox = Host.ScreenRect(host.Scroll);
        var target = new Point(
            Math.Min(scrollBox.Right - 35, Math.Max(anchorBox.Right + 100, scrollBox.Left + scrollBox.Width * 0.72)),
            anchorBox.Y + anchorBox.Height / 2);

        if (!scrollBox.Contains(target))
            throw new InvalidOperationException($"Drop point outside scroll viewport: {target} vs {scrollBox}.");

        var path = Path.Combine(output, $"{label}-1x1.png");
        File.WriteAllBytes(
            path,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQ1sAAAAASUVORK5CYII="));

        var completion = adapter.Expect(path);
        await RealFileDrop(mainWindow, target, path);

        var winner = await Task.WhenAny(completion, Task.Delay(5000));
        if (!ReferenceEquals(winner, completion))
            throw new TimeoutException("Integrated FileDrop completion timeout.");

        var result = await completion;
        if (result.LogicalLayer != expectedLogicalLayer)
            throw new InvalidOperationException(
                $"Drop mapped to L{result.LogicalLayer}; expected L{expectedLogicalLayer}.");

        return result;
    }

    private static async Task RealFileDrop(Window mainWindow, Point target, string filePath)
    {
        var border = new Border
        {
            Width = 64,
            Height = 64,
            Background = Brushes.Gray
        };

        var sourceWindow = new Window
        {
            Width = 80,
            Height = 80,
            Left = 20,
            Top = 20,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Content = border
        };

        sourceWindow.Show();
        sourceWindow.Activate();
        await Task.Delay(250);

        var start = border.PointToScreen(new Point(border.ActualWidth / 2, border.ActualHeight / 2));
        DropNative.SetCursorPos((int)start.X, (int)start.Y);
        await Task.Delay(100);
        DropNative.mouse_event(DropNative.LeftDown, 0, 0, 0, 0);
        await Task.Delay(80);

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { filePath });

        using var cancel = new CancellationTokenSource();
        var mover = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancel.Token);
                for (var i = 1; i <= 12; i++)
                {
                    DropNative.SetCursorPos(
                        (int)Math.Round(start.X + (target.X - start.X) * i / 12.0),
                        (int)Math.Round(start.Y + (target.Y - start.Y) * i / 12.0));
                    await Task.Delay(75, cancel.Token);
                }

                await Task.Delay(180, cancel.Token);
                DropNative.mouse_event(DropNative.LeftUp, 0, 0, 0, 0);
            }
            catch (OperationCanceledException)
            {
            }
        });

        try
        {
            _ = System.Windows.DragDrop.DoDragDrop(border, data, DragDropEffects.Copy);
        }
        finally
        {
            cancel.Cancel();
            DropNative.mouse_event(DropNative.LeftUp, 0, 0, 0, 0);
            sourceWindow.Close();
            mainWindow.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
        }

        try { await mover; }
        catch (OperationCanceledException) { }

        await Task.Delay(600);
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
                "status=" + (failed ? "FAIL_TRACK_C_FILEDROP" : "PASS_TRACK_C_FILEDROP"),
                "assertion_count=" + checks.Count
            }.Concat(checks));
        File.Move(temp, Path.Combine(output, "result.txt"), true);
    }
}
