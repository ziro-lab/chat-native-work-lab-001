using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
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

namespace Ymm4NoHarmonyFullInteractionProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Full Interaction Probe";
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
    private static FrameworkElement? cursorSource;
    private static FrameworkElement? timelineView;
    private static object? timelineVm;
    private static int layerHeight;
    private static readonly List<string> trace = [];

    private static bool marqueeActive;
    private static Point marqueeStart;
    private static Point marqueeEnd;
    private static bool marqueeHandled;
    private static bool customMarqueeSelected;

    private static bool dragActive;
    private static IItem? dragItem;
    private static Point dragStart;
    private static int dragOriginalLayer;
    private static bool dragHandled;
    private static readonly List<int> dragObservedLayers = [];

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_FULL_DIR");
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
                    _ = RunAsync(window, active, t);
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

    private static async Task RunAsync(Window mainWindow, object activeTimelineViewModel, Timeline t)
    {
        try
        {
            mainWindow.WindowState = System.Windows.WindowState.Maximized;
            mainWindow.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
            await Task.Delay(900);

            var character = new Character { Name = "CNWL_FULL_NO_HARMONY" };
            var dragHead = new VoiceItem(character)
            {
                Frame = 10,
                Length = 70,
                Layer = 1,
                Serif = "drag head",
                Remark = "CNWL_FULL_DRAG_HEAD"
            };
            var transformedTarget = new VoiceItem(character)
            {
                Frame = 120,
                Length = 70,
                Layer = 3,
                Serif = "transformed target",
                Remark = "CNWL_FULL_TRANSFORMED_TARGET"
            };
            var marqueeTarget = new VoiceItem(character)
            {
                Frame = 220,
                Length = 50,
                Layer = 3,
                Serif = "marquee target",
                Remark = "CNWL_FULL_MARQUEE_TARGET"
            };

            foreach (var item in new IItem[] { dragHead, transformedTarget, marqueeTarget })
            {
                if (!t.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("Could not insert fixture " + item.Remark);
            }

            t.CurrentFrame = 0;
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(1400);

            timelineView = FindLargest(mainWindow, fe =>
                fe.GetType().Name.Equals("TimelineView", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("TimelineView not found.");

            var dragView = FindTimelineItemView(timelineView, dragHead)
                ?? throw new InvalidOperationException("Drag fixture view not found.");
            var transformedView = FindTimelineItemView(timelineView, transformedTarget)
                ?? throw new InvalidOperationException("Transformed fixture view not found.");
            var marqueeView = FindTimelineItemView(timelineView, marqueeTarget)
                ?? throw new InvalidOperationException("Marquee fixture view not found.");

            var scrollViewer = FindAncestor<ScrollViewer>(dragView)
                ?? throw new InvalidOperationException("Timeline ScrollViewer not found.");
            cursorSource = scrollViewer.Content as FrameworkElement
                ?? throw new InvalidOperationException("Timeline cursor source not found.");

            layerHeight = SettingsBase<YMMSettings>.Default.LayerHeight;
            if (layerHeight <= 0)
                throw new InvalidOperationException("LayerHeight is not positive.");

            ApplyVisualFold();
            await Task.Delay(450);

            dragView = FindTimelineItemView(timelineView, dragHead) ?? dragView;
            transformedView = FindTimelineItemView(timelineView, transformedTarget) ?? transformedView;
            marqueeView = FindTimelineItemView(timelineView, marqueeTarget) ?? marqueeView;

            var dragBox = Box(dragView);
            var transformedBox = Box(transformedView);
            var marqueeBox = Box(marqueeView);
            if (!dragBox.Valid || !transformedBox.Valid || !marqueeBox.Valid)
                throw new InvalidOperationException("Fixture screen geometry invalid.");

            InstallInteractionAdapter();

            // 1) Custom marquee replacement: Shift-drag around visually shifted L3.
            t.SelectedItems = ImmutableList<IItem>.Empty;
            var frameBeforeMarquee = t.CurrentFrame;
            await ShiftDragAround(marqueeBox);
            await Task.Delay(650);
            customMarqueeSelected = t.SelectedItems.Any(x => ReferenceEquals(x, marqueeTarget));
            var marqueeSuppressedNativeSeek = t.CurrentFrame == frameBeforeMarquee;

            // 2) Custom item drag replacement: move displayed L1 down one displayed row.
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(200);
            dragView = FindTimelineItemView(timelineView, dragHead) ?? dragView;
            dragBox = Box(dragView);
            await Drag(dragBox.Center, new Point(dragBox.Center.X, dragBox.Center.Y + layerHeight));
            await Task.Delay(750);
            var customDragLayer = dragHead.Layer;
            var customDragMatchesFold = customDragLayer == 3;
            var customDragAvoidedNativeL2 = !dragObservedLayers.Contains(2);

            // 3) Right-click correction using the host cursor property, LayerPinning-style.
            transformedView = FindTimelineItemView(timelineView, transformedTarget) ?? transformedView;
            transformedBox = Box(transformedView);
            var tvBox = Box(timelineView);
            var rightClickPoint = new Point(
                Math.Min(tvBox.Right - 40, Math.Max(transformedBox.Right + 70, transformedBox.Center.X + 110)),
                transformedBox.Center.Y);

            await RightClick(rightClickPoint);
            await Task.Delay(350);

            var correctedRightClick = ReadReactivePoint(activeTimelineViewModel, "TimelineCursorPositionWhenRightClick");
            var correctedRightClickLayer = correctedRightClick is { } cr ? (int)(cr.Y / layerHeight) : -1;
            var converter = ObserveAddPositionConverter(correctedRightClick, layerHeight);
            var rightClickMatchesFold = correctedRightClickLayer == 3 && converter.Layer == 3;

            File.WriteAllLines(
                Path.Combine(output, "trace.txt"),
                trace,
                new UTF8Encoding(false));

            WriteResult(
                "PASS_NO_HARMONY_FULL_INTERACTION_OBSERVATION",
                [
                    "harmony_reference_present=False",
                    "visual_fold_applied=True",
                    $"marquee_adapter_handled={marqueeHandled}",
                    $"marquee_transformed_target_selected={customMarqueeSelected}",
                    $"marquee_native_seek_suppressed={marqueeSuppressedNativeSeek}",
                    $"drag_adapter_handled={dragHandled}",
                    $"drag_final_layer={customDragLayer}",
                    $"drag_matches_fold_semantics={customDragMatchesFold}",
                    $"drag_observed_layers={string.Join(",", dragObservedLayers)}",
                    $"drag_avoided_native_layer2={customDragAvoidedNativeL2}",
                    $"right_click_cursor_observed={correctedRightClick is not null}",
                    $"right_click_corrected_layer={correctedRightClickLayer}",
                    $"add_position_converter_layer={converter.Layer}",
                    $"right_click_and_add_match_fold={rightClickMatchesFold}"
                ]);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void InstallInteractionAdapter()
    {
        if (cursorSource is null)
            throw new InvalidOperationException("cursorSource missing.");

        cursorSource.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), true);
        cursorSource.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnPreviewMouseMove), true);
        cursorSource.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp), true);
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (cursorSource is null || timeline is null)
            return;

        var p = Mouse.GetPosition(cursorSource);

        if (e.ChangedButton == MouseButton.Right)
        {
            var mapped = MapDisplayPointToLogical(p);
            if (SetReactivePoint(timelineVm, "TimelineCursorPositionWhenRightClick", mapped))
            {
                trace.Add($"right_click_map raw={Fmt(p)} mapped={Fmt(mapped)}");
            }
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && FindAncestorByName(e.OriginalSource as DependencyObject, "TimelineItemView") is null)
        {
            marqueeActive = true;
            marqueeHandled = true;
            marqueeStart = p;
            marqueeEnd = p;
            e.Handled = true;
            Mouse.Capture(cursorSource, CaptureMode.SubTree);
            trace.Add($"marquee_down {Fmt(p)}");
            return;
        }

        var itemView = FindAncestorByName(e.OriginalSource as DependencyObject, "TimelineItemView") as FrameworkElement;
        var item = itemView is null ? null : ItemOf(itemView.DataContext);
        if (item is null)
            return;

        dragActive = true;
        dragHandled = true;
        dragItem = item;
        dragStart = p;
        dragOriginalLayer = item.Layer;
        dragObservedLayers.Clear();
        dragObservedLayers.Add(item.Layer);
        timeline.SelectItems([item]);
        e.Handled = true;
        Mouse.Capture(cursorSource, CaptureMode.SubTree);
        trace.Add($"drag_down item={item.Remark} layer={item.Layer} p={Fmt(p)}");
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (cursorSource is null)
            return;

        if (marqueeActive)
        {
            marqueeEnd = Mouse.GetPosition(cursorSource);
            e.Handled = true;
            return;
        }

        if (!dragActive || dragItem is null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var p = Mouse.GetPosition(cursorSource);
        var logicalLayer = MapDisplayYToLogicalLayer(p.Y);
        logicalLayer = Math.Max(0, logicalLayer);

        if (dragItem.Layer != logicalLayer)
        {
            dragItem.Layer = logicalLayer;
            dragObservedLayers.Add(logicalLayer);
            ApplyVisualFold();
            trace.Add($"drag_move p={Fmt(p)} logical_layer={logicalLayer}");
        }

        e.Handled = true;
    }

    private static void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (marqueeActive)
        {
            marqueeEnd = cursorSource is null ? marqueeEnd : Mouse.GetPosition(cursorSource);
            marqueeActive = false;
            e.Handled = true;
            Mouse.Capture(null);
            ApplyCustomMarquee();
            trace.Add($"marquee_up {Fmt(marqueeEnd)}");
            return;
        }

        if (dragActive)
        {
            dragActive = false;
            e.Handled = true;
            Mouse.Capture(null);
            ApplyVisualFold();
            trace.Add($"drag_up original={dragOriginalLayer} final={dragItem?.Layer}");
            dragItem = null;
        }
    }

    private static void ApplyCustomMarquee()
    {
        if (cursorSource is null || timeline is null || timelineView is null)
            return;

        var a = cursorSource.PointToScreen(marqueeStart);
        var b = cursorSource.PointToScreen(marqueeEnd);
        var rect = new ScreenBox(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));

        var items = Elements(timelineView)
            .Where(fe => fe.IsVisible && fe.GetType().Name == "TimelineItemView")
            .Select(fe => (View: fe, Item: ItemOf(fe.DataContext)))
            .Where(x => x.Item is not null && Box(x.View).Intersects(rect))
            .Select(x => x.Item!)
            .Distinct(ReferenceEqualityComparer.Instance)
            .ToArray();

        timeline.SelectItems(items);
        trace.Add($"marquee_select count={items.Length} items={string.Join("|", items.Select(i => i.Remark + "@L" + i.Layer))}");
    }

    private static void ApplyVisualFold()
    {
        if (timelineView is null)
            return;

        foreach (var fe in Elements(timelineView).Where(x => x.GetType().Name == "TimelineItemView"))
        {
            var item = ItemOf(fe.DataContext);
            if (item is null)
                continue;

            fe.RenderTransform = item.Layer >= 3
                ? new TranslateTransform(0, -layerHeight)
                : Transform.Identity;
        }
    }

    // Fold [1..2] into one displayed row: display row >= 2 maps to logical +1.
    private static int MapDisplayYToLogicalLayer(double y)
    {
        if (layerHeight <= 0)
            return 0;
        var displayLayer = (int)Math.Floor(y / layerHeight);
        return displayLayer >= 2 ? displayLayer + 1 : displayLayer;
    }

    private static Point MapDisplayPointToLogical(Point point)
    {
        if (layerHeight <= 0)
            return point;

        var displayLayer = (int)Math.Floor(point.Y / layerHeight);
        var offset = point.Y - displayLayer * layerHeight;
        var logicalLayer = displayLayer >= 2 ? displayLayer + 1 : displayLayer;
        return new Point(point.X, logicalLayer * layerHeight + offset);
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

    private readonly record struct ConverterObservation(int Layer, string[] Details);

    private static ConverterObservation ObserveAddPositionConverter(Point? point, int h)
    {
        var details = new List<string>();
        try
        {
            var assembly = typeof(Timeline).Assembly;
            var baseType = assembly.GetType("YukkuriMovieMaker.Views.Converters.AddItemCommandParameterConverterBase");
            var method = baseType?.GetMethod("GetTimelinePosition", BindingFlags.Static | BindingFlags.NonPublic);
            if (method is null || point is null)
                return new(-1, ["converter_missing"]);

            foreach (var second in new object?[] { 1.0, 1, 100.0, 100, null })
            {
                try
                {
                    var values = new object?[] { point.Value, second, h };
                    var result = method.Invoke(null, [values]);
                    details.Add($"invoke={second?.GetType().Name ?? "null"} result={result}");
                    if (result is ValueTuple<int, int> tuple)
                        return new(tuple.Item2, details.ToArray());
                }
                catch (Exception ex)
                {
                    details.Add(ex.GetBaseException().Message);
                }
            }
        }
        catch (Exception ex)
        {
            details.Add(ex.ToString());
        }
        return new(-1, details.ToArray());
    }

    private static Point? ReadReactivePoint(object instance, string propertyName)
    {
        try
        {
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

    private static async Task ShiftDragAround(ScreenBox box)
    {
        var start = new Point(box.Left - 15, box.Top + 5);
        var end = new Point(box.Right + 15, box.Bottom - 5);
        Native.keybd_event(0x10, 0, 0, 0);
        await Task.Delay(80);
        try { await Drag(start, end); }
        finally { Native.keybd_event(0x10, 0, Native.KEYUP, 0); }
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

    private static async Task Drag(Point a, Point b)
    {
        Native.SetCursorPos((int)Math.Round(a.X), (int)Math.Round(a.Y));
        await Task.Delay(100);
        Native.mouse_event(Native.LD, 0, 0, 0, 0);
        await Task.Delay(120);
        for (var i = 1; i <= 8; i++)
        {
            var x = a.X + (b.X - a.X) * i / 8.0;
            var y = a.Y + (b.Y - a.Y) * i / 8.0;
            Native.SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            await Task.Delay(65);
        }
        Native.mouse_event(Native.LU, 0, 0, 0, 0);
    }

    private static FrameworkElement? FindTimelineItemView(DependencyObject root, IItem item) =>
        Elements(root)
            .Where(fe => fe.IsVisible && fe.GetType().Name == "TimelineItemView" && ReferenceEquals(ItemOf(fe.DataContext), item))
            .OrderByDescending(fe => fe.ActualWidth * fe.ActualHeight)
            .FirstOrDefault();

    private static FrameworkElement? FindLargest(DependencyObject root, Func<FrameworkElement, bool> predicate) =>
        Elements(root)
            .Where(fe => fe.IsVisible && fe.ActualWidth > 5 && fe.ActualHeight > 5 && predicate(fe))
            .OrderByDescending(fe => fe.ActualWidth * fe.ActualHeight)
            .FirstOrDefault();

    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if (root is FrameworkElement fe)
            yield return fe;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(root); }
        catch { yield break; }
        for (var i = 0; i < count; i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i)))
                yield return child;
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

    private static DependencyObject? FindAncestorByName(DependencyObject? origin, string typeName)
    {
        var current = origin;
        while (current is not null)
        {
            if (current.GetType().Name == typeName)
                return current;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { break; }
        }
        return null;
    }

    private static IItem? ItemOf(object? dataContext)
    {
        if (dataContext is IItem direct)
            return direct;
        if (dataContext is null)
            return null;

        foreach (var property in dataContext.GetType()
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead &&
                                 (typeof(IItem).IsAssignableFrom(p.PropertyType) ||
                                  p.Name.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
                                  p.Name is "Model" or "Source" or "Value"))
                     .Take(40))
        {
            try
            {
                var value = property.GetValue(dataContext);
                if (value is IItem item)
                    return item;
                if (value is IEnumerable enumerable && value is not string)
                    foreach (var x in enumerable)
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
            var p = fe.PointToScreen(new Point(0, 0));
            return new ScreenBox(p.X, p.Y, fe.ActualWidth, fe.ActualHeight);
        }
        catch { return default; }
    }

    private static string Fmt(Point p) => $"{p.X:F2},{p.Y:F2}";

    private static void Fail(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
            WriteResult("FAIL_EXCEPTION", ["message=" + ex.GetBaseException().Message]);
        }
        catch { }
    }

    private static void WriteResult(string status, IEnumerable<string> details) =>
        File.WriteAllLines(
            Path.Combine(output, "result.txt"),
            new[] { "status=" + status }.Concat(details),
            new UTF8Encoding(false));
}
