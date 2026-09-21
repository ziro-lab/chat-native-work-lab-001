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

namespace Ymm4NoHarmonyFoldProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Fold Probe";
    public void SetCulture(CultureInfo cultureInfo) => FoldProbe.Schedule();
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    internal const uint LD = 0x0002;
    internal const uint LU = 0x0004;
}

internal readonly record struct ScreenBox(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public Point Center => new(Left + Width / 2, Top + Height / 2);
    public bool Valid => Width > 2 && Height > 2;
}

internal static class FoldProbe
{
    private static bool scheduled;
    private static bool running;
    private static string output = "";
    private static readonly List<string> Events = [];
    private static Timeline? timeline;
    private static int seq;
    private static string action = "setup";

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_FOLD_DIR");
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

            var character = new Character { Name = "CNWL_NO_HARMONY" };
            var head = new VoiceItem(character)
            {
                Frame = 10,
                Length = 80,
                Layer = 1,
                Serif = "fold head",
                Remark = "CNWL_FOLD_HEAD"
            };
            var target = new VoiceItem(character)
            {
                Frame = 10,
                Length = 80,
                Layer = 3,
                Serif = "fold target",
                Remark = "CNWL_FOLD_TARGET"
            };

            if (!t.TryAddItems([head], head.Frame, head.Layer))
                throw new InvalidOperationException("Could not insert Layer 1 fixture.");
            if (!t.TryAddItems([target], target.Frame, target.Layer))
                throw new InvalidOperationException("Could not insert Layer 3 fixture.");

            t.CurrentFrame = 0;
            t.SelectedItems = ImmutableList<IItem>.Empty;
            await Task.Delay(1500);

            var timelineView = FindLargest(mainWindow, fe =>
                fe.GetType().Name.Equals("TimelineView", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("TimelineView not found.");

            var headView = FindTimelineItemView(timelineView, head)
                ?? throw new InvalidOperationException("Layer 1 TimelineItemView not found.");
            var targetView = FindTimelineItemView(timelineView, target)
                ?? throw new InvalidOperationException("Layer 3 TimelineItemView not found.");

            var layerHeight = SettingsBase<YMMSettings>.Default.LayerHeight;
            if (layerHeight <= 0)
                throw new InvalidOperationException("LayerHeight is not positive.");

            AttachTrace(mainWindow, t);

            var headBefore = Box(headView);
            var targetBefore = Box(targetView);
            if (!headBefore.Valid || !targetBefore.Valid)
                throw new InvalidOperationException("Fixture geometry is invalid.");

            // Model a collapsed logical block [1..2]:
            // Layer 1 remains the visible head; Layer 2 disappears; Layer 3 moves upward by 1 row.
            // This is deliberately visual-only: no Harmony and no host ViewModel patching.
            var shift = -1.0 * layerHeight;
            var group = new TransformGroup();
            if (targetView.RenderTransform is { } existing && !ReferenceEquals(existing, Transform.Identity))
                group.Children.Add(existing.CloneCurrentValue());
            group.Children.Add(new TranslateTransform(0, shift));
            targetView.RenderTransform = group;

            await Task.Delay(500);
            var targetAfter = Box(targetView);
            var visualGap = targetAfter.Top - headBefore.Top;
            var visualGapMatchesOneRow = Math.Abs(visualGap - layerHeight) <= Math.Max(3.0, layerHeight * 0.25);

            File.WriteAllLines(
                Path.Combine(output, "geometry.txt"),
                [
                    $"layer_height={layerHeight}",
                    $"head_before={Format(headBefore)}",
                    $"target_before={Format(targetBefore)}",
                    $"target_after={Format(targetAfter)}",
                    $"target_shift={shift:F2}",
                    $"visual_gap_after={visualGap:F2}",
                    $"expected_visual_gap={layerHeight:F2}"
                ],
                new UTF8Encoding(false));

            action = "click-transformed-target";
            await Click(targetAfter.Center);
            await Task.Delay(650);
            var transformedTargetSelected =
                ReferenceEquals(t.SelectedItem, target) ||
                (t.SelectedItems.Count == 1 && ReferenceEquals(t.SelectedItems[0], target));

            // Keep context-menu input out of the decisive drag observation.
            Point? rightClickCursor = null;

            // Re-select the fold head with real input, then drag by exactly one displayed row,
            // i.e. to the visual row occupied by logical Layer 3 after the fold.
            action = "click-fold-head";
            await Click(headBefore.Center);
            await Task.Delay(500);

            var headSelectedBeforeDrag =
                ReferenceEquals(t.SelectedItem, head) ||
                t.SelectedItems.Any(x => ReferenceEquals(x, head));

            action = "drag-one-displayed-row";
            await Drag(headBefore.Center, new Point(headBefore.Center.X, headBefore.Center.Y + layerHeight));
            await Task.Delay(900);

            var actualLayer = head.Layer;
            var dragMatchesFoldSemantics = actualLayer == 3;
            var dragMatchesNativeOneRow = actualLayer == 2;

            // Capture right-click cursor state only after the drag observation so an open
            // context menu cannot steal the click/drag sequence.
            action = "right-click-transformed-target";
            await RightClick(targetAfter.Center);
            await Task.Delay(350);
            rightClickCursor = ReadReactivePoint(activeTimelineViewModel, "TimelineCursorPositionWhenRightClick");

            lock (Events)
                File.WriteAllLines(Path.Combine(output, "events.txt"), Events, new UTF8Encoding(false));

            WriteResult(
                "PASS_NO_HARMONY_FOLD_OBSERVATION",
                [
                    "harmony_reference_present=False",
                    "visual_transform_applied=True",
                    $"visual_gap_matches_one_row={visualGapMatchesOneRow}",
                    $"transformed_target_selected={transformedTargetSelected}",
                    $"head_selected_before_drag={headSelectedBeforeDrag}",
                    $"drag_actual_layer={actualLayer}",
                    $"drag_matches_fold_semantics={dragMatchesFoldSemantics}",
                    $"drag_matches_native_one_row={dragMatchesNativeOneRow}",
                    $"right_click_cursor_observed={rightClickCursor is not null}",
                    $"right_click_cursor_y={(rightClickCursor?.Y.ToString("F2", CultureInfo.InvariantCulture) ?? "<none>")}"
                ]);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void AttachTrace(Window main, Timeline t)
    {
        main.AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler((_, e) => Log("PreviewMouseDown source=" + Describe(e.OriginalSource))),
            true);
        main.AddHandler(
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler((_, e) => Log("PreviewMouseUp source=" + Describe(e.OriginalSource))),
            true);
        t.PropertyChanged += (_, e) => Log("Timeline.PropertyChanged " + e.PropertyName);
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

    private static Point? ReadReactivePoint(object instance, string propertyName)
    {
        try
        {
            var holder = instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
            if (holder is null)
                return null;
            var value = holder.GetType()
                .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(holder);
            return value is Point p ? p : null;
        }
        catch
        {
            return null;
        }
    }

    private static FrameworkElement? FindTimelineItemView(DependencyObject root, IItem item) =>
        Elements(root)
            .Where(fe => fe.IsVisible &&
                         fe.GetType().Name.Equals("TimelineItemView", StringComparison.OrdinalIgnoreCase) &&
                         ReferencesItem(fe.DataContext, item))
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

    private static bool ReferencesItem(object? dataContext, IItem item)
    {
        if (dataContext is null)
            return false;
        if (ReferenceEquals(dataContext, item))
            return true;

        var type = dataContext.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead &&
                                 (typeof(IItem).IsAssignableFrom(p.PropertyType) ||
                                  p.Name.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
                                  p.Name is "Model" or "Source" or "Value"))
                     .Take(40))
        {
            try
            {
                var value = property.GetValue(dataContext);
                if (ReferenceEquals(value, item))
                    return true;
                if (value is IEnumerable enumerable && value is not string)
                    foreach (var x in enumerable)
                        if (ReferenceEquals(x, item))
                            return true;
            }
            catch
            {
                // Probe-only discovery path.
            }
        }
        return false;
    }

    private static ScreenBox Box(FrameworkElement fe)
    {
        try
        {
            var p = fe.PointToScreen(new Point(0, 0));
            return new ScreenBox(p.X, p.Y, fe.ActualWidth, fe.ActualHeight);
        }
        catch
        {
            return default;
        }
    }

    private static string Format(ScreenBox b) =>
        $"{b.Left:F2},{b.Top:F2},{b.Width:F2},{b.Height:F2}";

    private static string Describe(object? value)
    {
        if (value is not DependencyObject d)
            return value?.GetType().FullName ?? "<null>";

        var parts = new List<string>();
        for (var i = 0; i < 6 && d is not null; i++)
        {
            var text = d.GetType().Name;
            if (d is FrameworkElement fe && fe.DataContext is { } dc)
                text += "[dc=" + dc.GetType().Name + "]";
            parts.Add(text);
            try { d = VisualTreeHelper.GetParent(d); }
            catch { break; }
        }
        return string.Join("<-", parts);
    }

    private static void Log(string text)
    {
        lock (Events)
            Events.Add($"{++seq:D5} action={action} frame={timeline?.CurrentFrame ?? -1} {text}");
    }

    private static void Fail(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
            WriteResult("FAIL_EXCEPTION", ["message=" + ex.GetBaseException().Message]);
        }
        catch
        {
        }
    }

    private static void WriteResult(string status, IEnumerable<string> details) =>
        File.WriteAllLines(
            Path.Combine(output, "result.txt"),
            new[] { "status=" + status }.Concat(details),
            new UTF8Encoding(false));
}
