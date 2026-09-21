using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4NoHarmonyFolderLayoutProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony FolderLayout Generalization Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    internal const uint LD = 0x0002;
    internal const uint LU = 0x0004;
    internal const uint KEYUP = 0x0002;
}

internal readonly record struct ScreenBox(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public Point Center => new(Left + Width / 2, Top + Height / 2);
    public bool Valid => Width > 2 && Height > 2;
    public bool Intersects(ScreenBox other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

internal static class Probe
{
    private static bool scheduled;
    private static bool running;
    private static string output = "";
    private static Timeline? timeline;
    private static object? timelineVm;
    private static FrameworkElement? timelineView;
    private static FrameworkElement? cursorSource;
    private static int h;
    private static FolderLayout? layout;

    private static IItem? activeDrag;
    private static int activeDragOriginalLayer;
    private static bool bubbleMoveSeen;
    private static readonly List<string> trace = [];

    private static bool marqueeActive;
    private static Point marqueeStart;
    private static Point marqueeEnd;
    private static bool marqueeHandled;

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_FOLDER_LAYOUT_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    private static void Bootstrap()
    {
        var ticks = 0;
        var projectCreated = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = main.GetType()
                        .GetProperty("ActiveTimelineViewModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(main);

                    if (active is null && !projectCreated)
                    {
                        projectCreated = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        return;
                    }

                    if (active is null || running)
                        continue;

                    var t = active.GetType()
                        .GetProperty("Timeline", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(active) as Timeline
                        ?? active.GetType()
                            .GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.GetValue(active) as Timeline;

                    if (t is null)
                        continue;

                    running = true;
                    timer.Stop();
                    timeline = t;
                    timelineVm = active;
                    _ = RunAsync(window, t);
                    return;
                }

                if (ticks > 100)
                {
                    timer.Stop();
                    WriteResult("FAIL_BOOTSTRAP_TIMEOUT", []);
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Fail(ex);
            }
        };
        timer.Start();
    }

    private static async Task RunAsync(Window mainWindow, Timeline t)
    {
        try
        {
            mainWindow.WindowState = System.Windows.WindowState.Maximized;
            mainWindow.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
            await Task.Delay(900);

            var layoutA = FolderLayout.Create(
                10,
                [new CollapsedSpan(2, 3), new CollapsedSpan(6, 8)]);
            var layoutB = FolderLayout.Create(
                10,
                [new CollapsedSpan(1, 5), new CollapsedSpan(2, 3), new CollapsedSpan(6, 8)]);

            AssertEqual("layoutA.visible", "0,1,2,4,5,6,9,10", layoutA.VisibleCsv);
            AssertEqual("layoutA.row6", 9, layoutA.DisplayRowToLogical(6));
            AssertEqual("layoutA.owner3", 2, layoutA.OwnerLogical(3));
            AssertEqual("layoutA.owner7", 6, layoutA.OwnerLogical(7));
            AssertEqual("layoutB.visible", "0,1,6,9,10", layoutB.VisibleCsv);
            AssertEqual("layoutB.row3", 9, layoutB.DisplayRowToLogical(3));
            AssertEqual("layoutB.owner3", 1, layoutB.OwnerLogical(3));
            AssertEqual("layoutB.owner4", 1, layoutB.OwnerLogical(4));
            AssertEqual("layoutB.owner7", 6, layoutB.OwnerLogical(7));

            var ch = new Character { Name = "CNWL_GENERAL_LAYOUT" };
            var childHead = new VoiceItem(ch) { Frame = 30, Length = 50, Layer = 2, Serif = "child", Remark = "CNWL_LAYOUT_CHILD_HEAD" };
            var hiddenChild = new VoiceItem(ch) { Frame = 100, Length = 50, Layer = 3, Serif = "hidden", Remark = "CNWL_LAYOUT_HIDDEN_CHILD" };
            var dragSource = new VoiceItem(ch) { Frame = 80, Length = 70, Layer = 6, Serif = "drag", Remark = "CNWL_LAYOUT_DRAG_SOURCE" };
            var target = new VoiceItem(ch) { Frame = 220, Length = 70, Layer = 9, Serif = "target", Remark = "CNWL_LAYOUT_TARGET" };
            var tail = new VoiceItem(ch) { Frame = 330, Length = 40, Layer = 10, Serif = "tail", Remark = "CNWL_LAYOUT_TAIL" };

            foreach (var item in new IItem[] { childHead, hiddenChild, dragSource, target, tail })
                if (!t.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Could not insert fixture " + item.Remark);

            t.CurrentFrame = 0;
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(1400);

            timelineView = FindLargest(mainWindow, x => x.GetType().Name == "TimelineView")
                ?? throw new InvalidOperationException("TimelineView not found.");

            h = SettingsBase<YMMSettings>.Default.LayerHeight;
            if (h <= 0)
                throw new InvalidOperationException("LayerHeight invalid.");

            var scroll = Elements(timelineView)
                .OfType<ScrollViewer>()
                .Where(x => x.IsVisible && x.ActualHeight > 50)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Timeline ScrollViewer not found.");
            cursorSource = scroll.Content as FrameworkElement
                ?? throw new InvalidOperationException("Timeline cursor source not found.");

            // L9 is outside the startup viewport on some host/layout combinations.
            // Move the native viewport so logical L6-L9 are realized before applying the visual fold.
            var viewport = ReadReactiveRect(timelineVm, "Viewport");
            if (viewport is { } vp)
            {
                SetReactiveRect(
                    timelineVm,
                    "Viewport",
                    new Rect(new Point(vp.X, 6 * h), vp.Size));
                await Task.Delay(500);
            }

            var targetView = FindItemView(timelineView, target)
                ?? throw new InvalidOperationException("Target view not found after viewport realization.");
            var dragView = FindItemView(timelineView, dragSource)
                ?? throw new InvalidOperationException("Drag view not found after viewport realization.");

            var targetTopBefore = GetItemVmTop(target);
            var dragTopBefore = GetItemVmTop(dragSource);

            Checkpoint("before_layout_a");
            layout = layoutA;
            ApplyLayout();
            Checkpoint("after_layout_a");
            await Task.Delay(450);

            // Use the compressed viewport coordinates now that item VM Top values are folded.
            var foldedViewport = ReadReactiveRect(timelineVm, "Viewport");
            if (foldedViewport is { } fvp)
            {
                SetReactiveRect(
                    timelineVm,
                    "Viewport",
                    new Rect(new Point(fvp.X, 4 * h), fvp.Size));
                ForceFastCanvasUpdateAll(timelineView);
                await Task.Delay(450);
            }

            targetView = FindItemView(timelineView, target)
                ?? throw new InvalidOperationException("Target view not realized at folded viewport.");
            dragView = FindItemView(timelineView, dragSource)
                ?? throw new InvalidOperationException("Drag view not realized at folded viewport.");
            var targetA = Box(targetView);
            var dragA = Box(dragView);
            if (!targetA.Valid || !dragA.Valid)
                throw new InvalidOperationException("Folded fixture geometry invalid.");

            var targetTopA = GetItemVmTop(target);
            var dragTopA = GetItemVmTop(dragSource);
            var targetShiftA = targetTopA - targetTopBefore;
            var dragShiftA = dragTopA - dragTopBefore;
            var geometryAMatches =
                Near(targetTopA, layoutA.VisualRowOfLogical(9) * h) &&
                Near(dragTopA, layoutA.VisualRowOfLogical(6) * h) &&
                Near(targetTopA - dragTopA, h);

            Checkpoint("before_install_adapters");
            InstallAdapters();
            Checkpoint("after_install_adapters");

            // Shift-marquee around the visually shifted L9 item.
            t.SelectedItems = ImmutableList<IItem>.Empty;
            var frameBeforeMarquee = t.CurrentFrame;
            await ShiftDragAround(targetA);
            await Task.Delay(650);
            var marqueeSelected = t.SelectedItems.Any(x => ReferenceEquals(x, target));
            var marqueeSuppressedSeek = t.CurrentFrame == frameBeforeMarquee;

            // Standard YMM4 diagonal drag owns frame behavior; generic FolderLayout corrects only logical layer.
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(150);
            dragView = FindItemView(timelineView, dragSource) ?? dragView;
            dragA = Box(dragView);
            var dragOriginal = (Layer: dragSource.Layer, Frame: dragSource.Frame);
            await Drag(dragA.Center, new Point(dragA.Center.X + 64, dragA.Center.Y + h));
            await Task.Delay(850);
            var dragFinal = (Layer: dragSource.Layer, Frame: dragSource.Frame);
            var dragMatchesNonUniformJump = dragFinal.Layer == 9;
            var nativeFramePreserved = dragFinal.Frame != dragOriginal.Frame;

            mainWindow.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
            await Shortcut(0x5A);
            await Task.Delay(650);
            var undoRestored = dragSource.Layer == dragOriginal.Layer && dragSource.Frame == dragOriginal.Frame;
            await Shortcut(0x59);
            await Task.Delay(650);
            var redoRestored = dragSource.Layer == dragFinal.Layer && dragSource.Frame == dragFinal.Frame;
            await Shortcut(0x5A);
            await Task.Delay(500);

            // Right-click/add mapping under layout A. Visible row 6 must resolve to logical L9.
            targetView = FindItemView(timelineView, target) ?? targetView;
            targetA = Box(targetView);
            var tvBox = Box(timelineView);
            var rightA = new Point(Math.Min(tvBox.Right - 40, targetA.Right + 90), targetA.Center.Y);
            await RightClick(rightA);
            await Task.Delay(300);
            var rightAPoint = ReadReactivePoint(timelineVm, "TimelineCursorPositionWhenRightClick");
            var rightALayer = rightAPoint is null ? -1 : (int)Math.Floor(rightAPoint.Value.Y / h);
            var converterA = ObserveAddPositionConverter(rightAPoint, h);
            await Escape();

            // Dynamically collapse the parent too. Nested child head must now disappear into L1.
            Checkpoint("before_layout_b");
            layout = layoutB;
            ApplyLayout();
            Checkpoint("after_layout_b");
            var parentFoldViewport = ReadReactiveRect(timelineVm, "Viewport");
            if (parentFoldViewport is { } pvp)
            {
                SetReactiveRect(
                    timelineVm,
                    "Viewport",
                    new Rect(new Point(pvp.X, 2 * h), pvp.Size));
                ForceFastCanvasUpdateAll(timelineView);
                await Task.Delay(450);
            }

            targetView = FindItemView(timelineView, target)
                ?? throw new InvalidOperationException("Target view not realized after parent collapse.");
            var targetB = Box(targetView);
            var targetTopB = GetItemVmTop(target);
            var targetShiftB = targetTopB - targetTopBefore;
            var geometryBMatches = Near(targetTopB, layoutB.VisualRowOfLogical(9) * h);

            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Click(targetB.Center);
            await Task.Delay(350);
            var clickBSelected = t.SelectedItems.Any(x => ReferenceEquals(x, target));

            var rightB = new Point(Math.Min(tvBox.Right - 40, targetB.Right + 90), targetB.Center.Y);
            await RightClick(rightB);
            await Task.Delay(300);
            var rightBPoint = ReadReactivePoint(timelineVm, "TimelineCursorPositionWhenRightClick");
            var rightBLayer = rightBPoint is null ? -1 : (int)Math.Floor(rightBPoint.Value.Y / h);
            var converterB = ObserveAddPositionConverter(rightBPoint, h);
            await Escape();

            var criticalPass =
                geometryAMatches &&
                marqueeSelected &&
                marqueeSuppressedSeek &&
                bubbleMoveSeen &&
                dragMatchesNonUniformJump &&
                nativeFramePreserved &&
                undoRestored &&
                redoRestored &&
                rightALayer == 9 &&
                converterA == 9 &&
                geometryBMatches &&
                clickBSelected &&
                rightBLayer == 9 &&
                converterB == 9;

            File.WriteAllLines(Path.Combine(output, "trace.txt"), trace, new UTF8Encoding(false));
            WriteResult(
                criticalPass
                    ? "PASS_NO_HARMONY_FOLDER_LAYOUT_GENERALIZATION"
                    : "FAIL_NO_HARMONY_FOLDER_LAYOUT_GENERALIZATION",
                [
                    "harmony_reference_present=False",
                    $"layout_a_visible={layoutA.VisibleCsv}",
                    $"layout_a_owner_3={layoutA.OwnerLogical(3)}",
                    $"layout_a_owner_7={layoutA.OwnerLogical(7)}",
                    $"layout_a_row6_logical={layoutA.DisplayRowToLogical(6)}",
                    $"layout_b_visible={layoutB.VisibleCsv}",
                    $"layout_b_owner_3={layoutB.OwnerLogical(3)}",
                    $"layout_b_owner_4={layoutB.OwnerLogical(4)}",
                    $"layout_b_owner_7={layoutB.OwnerLogical(7)}",
                    $"layout_b_row3_logical={layoutB.DisplayRowToLogical(3)}",
                    $"geometry_a_matches={geometryAMatches}",
                    $"target_shift_a={targetShiftA:F2}",
                    $"drag_shift_a={dragShiftA:F2}",
                    $"marquee_handled={marqueeHandled}",
                    $"marquee_target_selected={marqueeSelected}",
                    $"marquee_native_seek_suppressed={marqueeSuppressedSeek}",
                    $"bubble_move_seen={bubbleMoveSeen}",
                    $"drag_original=L{dragOriginal.Layer}:F{dragOriginal.Frame}",
                    $"drag_final=L{dragFinal.Layer}:F{dragFinal.Frame}",
                    $"drag_matches_nonuniform_jump={dragMatchesNonUniformJump}",
                    $"drag_native_frame_changed={nativeFramePreserved}",
                    $"drag_undo_restored={undoRestored}",
                    $"drag_redo_restored={redoRestored}",
                    $"right_a_layer={rightALayer}",
                    $"right_a_converter_layer={converterA}",
                    $"right_a_matches={rightALayer == 9 && converterA == 9}",
                    $"geometry_b_matches={geometryBMatches}",
                    $"target_shift_b={targetShiftB:F2}",
                    $"layout_b_click_target_selected={clickBSelected}",
                    $"right_b_layer={rightBLayer}",
                    $"right_b_converter_layer={converterB}",
                    $"right_b_matches={rightBLayer == 9 && converterB == 9}"
                ]);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void InstallAdapters()
    {
        if (cursorSource is null)
            throw new InvalidOperationException("cursorSource missing.");

        cursorSource.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewDown), true);
        cursorSource.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnPreviewMove), true);
        cursorSource.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewUp), true);
        cursorSource.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(OnBubbleMove), true);
        cursorSource.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(OnBubbleUp), true);
    }

    private static void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (cursorSource is null || layout is null)
            return;

        var raw = Mouse.GetPosition(cursorSource);

        if (e.ChangedButton == MouseButton.Right)
        {
            var mapped = layout.MapDisplayPointToLogical(raw, h);
            if (SetReactivePoint(timelineVm, "TimelineCursorPositionWhenRightClick", mapped))
                trace.Add($"right_map raw={Fmt(raw)} mapped={Fmt(mapped)}");
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        var itemView = FindAncestorByName(e.OriginalSource as DependencyObject, "TimelineItemView") as FrameworkElement;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && itemView is null)
        {
            marqueeActive = true;
            marqueeHandled = true;
            marqueeStart = raw;
            marqueeEnd = raw;
            e.Handled = true;
            Mouse.Capture(cursorSource, CaptureMode.SubTree);
            trace.Add($"marquee_down {Fmt(raw)}");
            return;
        }

        var item = itemView is null ? null : ItemOf(itemView.DataContext);
        if (item is null)
            return;

        activeDrag = item;
        activeDragOriginalLayer = item.Layer;
        trace.Add($"drag_down L{item.Layer}:F{item.Frame} raw={Fmt(raw)}");
        // Do not handle: native YMM4 keeps frame/snap behavior.
    }

    private static void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (!marqueeActive || cursorSource is null)
            return;

        marqueeEnd = Mouse.GetPosition(cursorSource);
        e.Handled = true;
    }

    private static void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !marqueeActive || cursorSource is null)
            return;

        marqueeEnd = Mouse.GetPosition(cursorSource);
        marqueeActive = false;
        e.Handled = true;
        Mouse.Capture(null);
        ApplyMarquee();
        trace.Add($"marquee_up {Fmt(marqueeEnd)}");
    }

    private static void OnBubbleMove(object sender, MouseEventArgs e)
    {
        if (activeDrag is null || cursorSource is null || layout is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        bubbleMoveSeen = true;
        var raw = Mouse.GetPosition(cursorSource);
        var desired = layout.DisplayYToLogical(raw.Y, h);
        var nativeLayer = activeDrag.Layer;

        if (activeDrag.Layer != desired)
        {
            activeDrag.Layer = desired;
            ApplyLayout();
        }

        trace.Add($"drag_move native={nativeLayer} desired={desired} final={activeDrag.Layer} frame={activeDrag.Frame} raw={Fmt(raw)}");
    }

    private static void OnBubbleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || activeDrag is null)
            return;

        trace.Add($"drag_up original={activeDragOriginalLayer} final={activeDrag.Layer} frame={activeDrag.Frame}");
        activeDrag = null;
        ApplyLayout();
    }

    private static void ApplyMarquee()
    {
        if (timeline is null || timelineView is null || cursorSource is null)
            return;

        var a = cursorSource.PointToScreen(marqueeStart);
        var b = cursorSource.PointToScreen(marqueeEnd);
        var rect = new ScreenBox(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));

        var selected = Elements(timelineView)
            .Where(x => x.IsVisible && x.GetType().Name == "TimelineItemView")
            .Select(x => (View: x, Item: ItemOf(x.DataContext)))
            .Where(x => x.Item is not null && Box(x.View).Intersects(rect))
            .Select(x => x.Item!)
            .Distinct<IItem>(ReferenceEqualityComparer.Instance)
            .ToArray();

        timeline.SelectItems(selected);
        trace.Add($"marquee_select={string.Join("|", selected.Select(x => x.Remark + "@L" + x.Layer))}");
    }

    private static void ApplyLayout()
    {
        if (timelineView is null || layout is null || timelineVm is null)
            return;

        var itemsHolder = timelineVm.GetType()
            .GetProperty("Items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(timelineVm);

        if (itemsHolder is IEnumerable itemVms)
        {
            foreach (var itemVm in itemVms)
            {
                if (itemVm is null)
                    continue;

                var item = ItemOf(itemVm.GetType()
                    .GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(itemVm));

                if (item is null || item.Layer > layout.MaxLayer)
                    continue;

                var hidden = layout.IsHidden(item.Layer);
                var top = hidden
                    ? -100000.0 - item.Layer * h
                    : layout.VisualRowOfLogical(item.Layer) * (double)h;

                SetPrivateProperty(itemVm, "Top", top);
                if (hidden)
                    SetPrivateProperty(itemVm, "Height", 6.0);
            }
        }

        ForceFastCanvasUpdateAll(timelineView);
    }

    private static double GetItemVmTop(IItem item)
    {
        if (timelineVm is null)
            throw new InvalidOperationException("timelineVm missing.");

        var itemsHolder = timelineVm.GetType()
            .GetProperty("Items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(timelineVm);

        if (itemsHolder is not IEnumerable itemVms)
            throw new MissingMemberException("TimelineViewModel.Items");

        foreach (var itemVm in itemVms)
        {
            if (itemVm is null)
                continue;

            var model = itemVm.GetType()
                .GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(itemVm) as IItem;

            if (!ReferenceEquals(model, item))
                continue;

            var top = itemVm.GetType()
                .GetProperty("Top", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(itemVm);

            return Convert.ToDouble(top, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException("Item VM not found for " + item.Remark);
    }

    private static void SetPrivateProperty(object instance, string name, object value)
    {
        var property = instance.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, name);
        property.SetValue(instance, value);
    }

    private static int ForceFastCanvasUpdateAll(DependencyObject root)
    {
        var count = 0;
        foreach (var fe in Elements(root))
        {
            if (fe.GetType().Name != "FastCanvasItemsControl")
                continue;
            var method = fe.GetType()
                .GetMethod("UpdateAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
                continue;
            method.Invoke(fe, null);
            count++;
        }
        return count;
    }

    private static bool SetReactivePoint(object? instance, string propertyName, Point value)
    {
        try
        {
            if (instance is null)
                return false;
            var holder = instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            var p = holder?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p is null || !p.CanWrite)
                return false;
            p.SetValue(holder, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Rect? ReadReactiveRect(object? instance, string propertyName)
    {
        try
        {
            if (instance is null)
                return null;
            var holder = instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            var value = holder?.GetType()
                .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(holder);
            return value is Rect rect ? rect : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SetReactiveRect(object? instance, string propertyName, Rect value)
    {
        try
        {
            if (instance is null)
                return false;
            var holder = instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            var p = holder?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p is null || !p.CanWrite)
                return false;
            p.SetValue(holder, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Point? ReadReactivePoint(object? instance, string propertyName)
    {
        try
        {
            if (instance is null)
                return null;
            var holder = instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            var value = holder?.GetType()
                .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(holder);
            return value is Point p ? p : null;
        }
        catch
        {
            return null;
        }
    }

    private static int ObserveAddPositionConverter(Point? point, int layerHeight)
    {
        if (point is null)
            return -1;

        try
        {
            var baseType = typeof(Timeline).Assembly.GetType("YukkuriMovieMaker.Views.Converters.AddItemCommandParameterConverterBase");
            var method = baseType?.GetMethod("GetTimelinePosition", BindingFlags.Static | BindingFlags.NonPublic);
            if (method is null)
                return -1;

            foreach (var second in new object?[] { 1.0, 1, 100.0, 100, null })
            {
                try
                {
                    var values = new object?[] { point.Value, second, layerHeight };
                    var result = method.Invoke(null, [values]);
                    if (result is ValueTuple<int, int> tuple)
                        return tuple.Item2;
                }
                catch { }
            }
        }
        catch { }

        return -1;
    }

    private static async Task ShiftDragAround(ScreenBox box)
    {
        var start = new Point(box.Left - 15, box.Top + 5);
        var end = new Point(box.Right + 15, box.Bottom - 5);
        Native.keybd_event(0x10, 0, 0, 0);
        await Task.Delay(80);
        try { await Drag(start, end); }
        finally { Native.keybd_event(0x10, 0, Native.KEYUP, 0); }
    }

    private static async Task Drag(Point a, Point b)
    {
        Native.SetCursorPos((int)Math.Round(a.X), (int)Math.Round(a.Y));
        await Task.Delay(100);
        Native.mouse_event(Native.LD, 0, 0, 0, 0);
        await Task.Delay(120);
        for (var i = 1; i <= 10; i++)
        {
            Native.SetCursorPos(
                (int)Math.Round(a.X + (b.X - a.X) * i / 10.0),
                (int)Math.Round(a.Y + (b.Y - a.Y) * i / 10.0));
            await Task.Delay(70);
        }
        Native.mouse_event(Native.LU, 0, 0, 0, 0);
    }

    private static async Task Click(Point p)
    {
        Native.SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
        await Task.Delay(100);
        Native.mouse_event(Native.LD, 0, 0, 0, 0);
        await Task.Delay(70);
        Native.mouse_event(Native.LU, 0, 0, 0, 0);
    }

    private static async Task RightClick(Point p)
    {
        const uint rd = 0x0008, ru = 0x0010;
        Native.SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
        await Task.Delay(100);
        Native.mouse_event(rd, 0, 0, 0, 0);
        await Task.Delay(70);
        Native.mouse_event(ru, 0, 0, 0, 0);
    }

    private static async Task Escape()
    {
        Native.keybd_event(0x1B, 0, 0, 0);
        await Task.Delay(50);
        Native.keybd_event(0x1B, 0, Native.KEYUP, 0);
        await Task.Delay(100);
    }

    private static async Task Shortcut(byte key)
    {
        const byte ctrl = 0x11;
        Native.keybd_event(ctrl, 0, 0, 0);
        await Task.Delay(60);
        Native.keybd_event(key, 0, 0, 0);
        await Task.Delay(60);
        Native.keybd_event(key, 0, Native.KEYUP, 0);
        Native.keybd_event(ctrl, 0, Native.KEYUP, 0);
    }

    private static FrameworkElement? FindItemView(DependencyObject root, IItem item) =>
        Elements(root)
            .Where(x => x.IsVisible && x.GetType().Name == "TimelineItemView" && ReferenceEquals(ItemOf(x.DataContext), item))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private static FrameworkElement? FindLargest(DependencyObject root, Func<FrameworkElement, bool> predicate) =>
        Elements(root)
            .Where(x => x.IsVisible && x.ActualWidth > 5 && x.ActualHeight > 5 && predicate(x))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement fe)
            yield return fe;

        int count;
        try { count = VisualTreeHelper.GetChildrenCount(root); }
        catch { yield break; }

        for (var i = 0; i < count; i++)
            foreach (var x in Elements(VisualTreeHelper.GetChild(root, i)))
                yield return x;
    }

    private static T? FindAncestor<T>(DependencyObject? origin) where T : DependencyObject
    {
        var current = origin;
        while (current is not null)
        {
            if (current is T found)
                return found;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { break; }
        }
        return null;
    }

    private static DependencyObject? FindAncestorByName(DependencyObject? origin, string name)
    {
        var current = origin;
        while (current is not null)
        {
            if (current.GetType().Name == name)
                return current;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { break; }
        }
        return null;
    }

    private static IItem? ItemOf(object? dc)
    {
        if (dc is IItem direct)
            return direct;
        if (dc is null)
            return null;

        foreach (var p in dc.GetType()
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead &&
                                 (typeof(IItem).IsAssignableFrom(p.PropertyType) ||
                                  p.Name.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
                                  p.Name is "Model" or "Source" or "Value"))
                     .Take(40))
        {
            try
            {
                var v = p.GetValue(dc);
                if (v is IItem item)
                    return item;
                if (v is IEnumerable en && v is not string)
                    foreach (var x in en)
                        if (x is IItem nested)
                            return nested;
            }
            catch { }
        }
        return null;
    }

    private static ScreenBox Box(FrameworkElement fe)
    {
        try
        {
            var p = fe.PointToScreen(new Point());
            return new ScreenBox(p.X, p.Y, fe.ActualWidth, fe.ActualHeight);
        }
        catch
        {
            return default;
        }
    }

    private static bool Near(double actual, double expected) =>
        Math.Abs(actual - expected) <= Math.Max(3.0, h * 0.2);

    private static void AssertEqual<T>(string name, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}");
    }

    private static string Fmt(Point p) => $"{p.X:F2},{p.Y:F2}";

    private static void Checkpoint(string value)
    {
        try
        {
            File.AppendAllLines(
                Path.Combine(output, "progress.txt"),
                [DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + value],
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static void WriteResult(string status, IEnumerable<string> details) =>
        File.WriteAllLines(
            Path.Combine(output, "result.txt"),
            new[] { "status=" + status }.Concat(details),
            new UTF8Encoding(false));

    private static void Fail(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
            WriteResult("FAIL_EXCEPTION", ["message=" + ex.GetBaseException().Message]);
        }
        catch { }
    }
}
