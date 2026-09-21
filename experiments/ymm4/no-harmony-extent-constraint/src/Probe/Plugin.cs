using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyExtentConstraintProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Extent Constraint Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal readonly record struct CandidateResult(
    string Name,
    double Extent,
    double Scrollable,
    bool MatchesExpected,
    bool LowItemRealized,
    bool SurvivedRefresh);

internal static class Probe
{
    static bool scheduled, running;
    static string output = "";
    static TimelineViewModel? vm;
    static Timeline? timeline;
    static FrameworkElement? timelineView;
    static ScrollViewer? scroll;
    static int h;

    const int FoldStart = 1;
    const int FoldEnd = 10;
    const int HiddenCount = FoldEnd - FoldStart;

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_EXTENT_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    static void Bootstrap()
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
                ticks++;
                foreach (Window w in Application.Current.Windows)
                {
                    var main = w.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = main.GetType()
                        .GetProperty("ActiveTimelineViewModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(main);

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        return;
                    }

                    if (active is not TimelineViewModel typed || running)
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
                    vm = typed;
                    timeline = t;
                    _ = RunAsync(w, t, typed);
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

    static async Task RunAsync(Window mainWindow, Timeline t, TimelineViewModel viewModel)
    {
        try
        {
            mainWindow.WindowState = System.Windows.WindowState.Maximized;
            mainWindow.Activate();
            await Task.Delay(900);

            var ch = new Character { Name = "CNWL_EXTENT" };
            var top = new VoiceItem(ch) { Frame = 20, Length = 40, Layer = 1, Serif = "top", Remark = "CNWL_EXTENT_TOP" };
            var hidden = new VoiceItem(ch) { Frame = 100, Length = 40, Layer = 5, Serif = "hidden", Remark = "CNWL_EXTENT_HIDDEN" };
            var low = new VoiceItem(ch) { Frame = 180, Length = 40, Layer = 20, Serif = "low", Remark = "CNWL_EXTENT_LOW" };

            foreach (var item in new IItem[] { top, hidden, low })
                if (!t.TryAddItems([item], item.Frame, item.Layer))
                    throw new InvalidOperationException("fixture insert failed");

            t.RefreshTimelineLengthAndMaxLayer();
            t.CurrentFrame = 0;
            await Task.Delay(1400);

            timelineView = FindLargest(mainWindow, x => x.GetType().Name == "TimelineView")
                ?? throw new InvalidOperationException("TimelineView missing");

            scroll = Elements(timelineView)
                .OfType<ScrollViewer>()
                .Where(x => x.IsVisible && x.ActualHeight > 50)
                .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Timeline ScrollViewer missing");

            h = SettingsBase<YMMSettings>.Default.LayerHeight;
            if (h <= 0)
                throw new InvalidOperationException("LayerHeight invalid");

            var labels = GetList(viewModel, "LayerLabels") ?? throw new MissingMemberException("LayerLabels");
            var lines = GetList(viewModel, "LayerLines") ?? throw new MissingMemberException("LayerLines");

            var expectedRows = Math.Max(1, labels.Count - HiddenCount);
            var expectedHeight = expectedRows * h;
            var nativeExtent = scroll.ExtentHeight;
            var nativeScrollable = scroll.ScrollableHeight;

            ApplyGeometry(viewModel, labels, lines);
            ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(400);

            var content = scroll.Content as FrameworkElement
                ?? throw new InvalidOperationException("ScrollViewer content is not FrameworkElement");

            var fastCanvases = Elements(timelineView)
                .Where(x => x.GetType().Name == "FastCanvasItemsControl")
                .ToArray();

            var tall = Elements(content)
                .Where(x => x.ActualHeight >= nativeExtent - 1.0 || (!double.IsNaN(x.Height) && x.Height >= nativeExtent - 1.0))
                .ToArray();

            var inventory = new List<string>
            {
                $"content_type={content.GetType().FullName}",
                $"content_actual_height={content.ActualHeight:F2}",
                $"content_height={content.Height}",
                $"content_max_height={content.MaxHeight}",
                $"content_height_binding={DescribeBinding(content, FrameworkElement.HeightProperty)}",
                $"fast_canvas_count={fastCanvases.Length}",
                $"tall_element_count={tall.Length}"
            };
            foreach (var fe in tall.Take(30))
                inventory.Add($"tall={fe.GetType().FullName}|actual={fe.ActualHeight:F2}|height={fe.Height}|max={fe.MaxHeight}|binding={DescribeBinding(fe, FrameworkElement.HeightProperty)}");

            var results = new List<CandidateResult>();

            results.Add(await TestCandidate(
                "content_maxheight",
                expectedHeight,
                low,
                () => content.MaxHeight = expectedHeight,
                () => content.ClearValue(FrameworkElement.MaxHeightProperty)));

            var contentHeightBinding = BindingOperations.GetBindingBase(content, FrameworkElement.HeightProperty);
            if (contentHeightBinding is null)
            {
                var oldHeight = content.Height;
                results.Add(await TestCandidate(
                    "content_height",
                    expectedHeight,
                    low,
                    () => content.Height = expectedHeight,
                    () => content.Height = oldHeight));
            }

            results.Add(await TestCandidate(
                "fastcanvas_maxheight",
                expectedHeight,
                low,
                () =>
                {
                    foreach (var fe in fastCanvases)
                        fe.MaxHeight = expectedHeight;
                },
                () =>
                {
                    foreach (var fe in fastCanvases)
                        fe.ClearValue(FrameworkElement.MaxHeightProperty);
                }));

            var boundCanvasHeight = Elements(timelineView)
                .Where(fe =>
                {
                    var binding = BindingOperations.GetBindingBase(fe, FrameworkElement.HeightProperty) as Binding;
                    return binding?.Path?.Path?.Contains("CanvasHeight", StringComparison.OrdinalIgnoreCase) == true;
                })
                .ToArray();

            results.Add(await TestCandidate(
                "canvasheight_bound_maxheight",
                expectedHeight,
                low,
                () =>
                {
                    foreach (var fe in boundCanvasHeight)
                        fe.MaxHeight = expectedHeight;
                },
                () =>
                {
                    foreach (var fe in boundCanvasHeight)
                        fe.ClearValue(FrameworkElement.MaxHeightProperty);
                }));

            var best = results.FirstOrDefault(x => x.MatchesExpected && x.LowItemRealized && x.SurvivedRefresh);

            File.WriteAllLines(
                Path.Combine(output, "inventory.txt"),
                inventory.Concat(results.Select(x =>
                    $"candidate={x.Name}|extent={x.Extent:F2}|scrollable={x.Scrollable:F2}|matches={x.MatchesExpected}|low_realized={x.LowItemRealized}|survived_refresh={x.SurvivedRefresh}")),
                new UTF8Encoding(false));

            WriteResult(
                "PASS_NO_HARMONY_EXTENT_CONSTRAINT_OBSERVATION",
                [
                    "harmony_reference_present=False",
                    $"native_extent={nativeExtent:F2}",
                    $"native_scrollable={nativeScrollable:F2}",
                    $"expected_extent={expectedHeight:F2}",
                    $"content_type={content.GetType().FullName}",
                    $"fast_canvas_count={fastCanvases.Length}",
                    $"canvasheight_bound_element_count={boundCanvasHeight.Length}",
                    $"candidate_count={results.Count}",
                    $"successful_candidate_found={!string.IsNullOrEmpty(best.Name)}",
                    $"successful_candidate={best.Name ?? ""}",
                    $"successful_extent={best.Extent:F2}",
                    $"successful_scrollable={best.Scrollable:F2}",
                    $"successful_low_item_realized={best.LowItemRealized}",
                    $"successful_survived_refresh={best.SurvivedRefresh}"
                ]);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    static async Task<CandidateResult> TestCandidate(
        string name,
        double expectedHeight,
        IItem low,
        Action apply,
        Action restore)
    {
        if (scroll is null || timelineView is null || vm is null || timeline is null)
            throw new InvalidOperationException("probe state incomplete");

        apply();
        timelineView.UpdateLayout();
        ForceFastCanvasUpdateAll(timelineView);
        await Task.Delay(350);

        var extent = scroll.ExtentHeight;
        var scrollable = scroll.ScrollableHeight;
        var matches = Math.Abs(extent - expectedHeight) < 1.5;

        var expectedLowY = RowOf(low.Layer) * h;
        vm.Viewport.Value = new Rect(
            new Point(vm.Viewport.Value.X, expectedLowY),
            vm.Viewport.Value.Size);
        ForceFastCanvasUpdateAll(timelineView);
        await Task.Delay(300);

        var lowView = FindItemView(timelineView, low);
        var lowRealized = lowView is not null && lowView.ActualWidth > 2 && lowView.ActualHeight > 2;

        timeline.RefreshTimelineLengthAndMaxLayer();
        ForceFastCanvasUpdateAll(timelineView);
        await Task.Delay(350);

        var survived = Math.Abs(scroll.ExtentHeight - expectedHeight) < 1.5;

        restore();
        timelineView.UpdateLayout();
        ForceFastCanvasUpdateAll(timelineView);
        await Task.Delay(250);

        return new CandidateResult(name, extent, scrollable, matches, lowRealized, survived);
    }

    static void ApplyGeometry(TimelineViewModel viewModel, IList labels, IList lines)
    {
        foreach (var itemVm in viewModel.Items)
        {
            var item = itemVm.Item;
            SetPrivateProperty(itemVm, "Top", IsHiddenLayer(item.Layer) ? -100000.0 - item.Layer * h : RowOf(item.Layer) * h);
            if (IsHiddenLayer(item.Layer))
                SetPrivateProperty(itemVm, "Height", 6.0);
        }

        ApplyRows(labels);
        ApplyRows(lines);
    }

    static void ApplyRows(IList rows)
    {
        for (var layer = 0; layer < rows.Count; layer++)
        {
            if (rows[layer] is not object row)
                continue;
            SetPrivateProperty(row, "Top", IsHiddenLayer(layer) ? -100000.0 - layer * h : RowOf(layer) * h);
            SetPrivateProperty(row, "Height", IsHiddenLayer(layer) ? 0.0 : (double)h);
        }
    }

    static bool IsHiddenLayer(int layer) => layer > FoldStart && layer <= FoldEnd;

    static int RowOf(int layer)
    {
        if (layer <= FoldStart) return layer;
        if (layer <= FoldEnd) return FoldStart;
        return layer - HiddenCount;
    }

    static string DescribeBinding(FrameworkElement fe, DependencyProperty dp)
    {
        var b = BindingOperations.GetBindingBase(fe, dp);
        return b switch
        {
            Binding binding => binding.Path?.Path ?? "<binding>",
            null => "<none>",
            _ => b.GetType().Name
        };
    }

    static IList? GetList(object instance, string name)
    {
        try
        {
            return instance.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance) as IList;
        }
        catch { return null; }
    }

    static void SetPrivateProperty(object instance, string name, object value)
    {
        var prop = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, name);
        prop.SetValue(instance, value);
    }

    static int ForceFastCanvasUpdateAll(DependencyObject root)
    {
        var count = 0;
        foreach (var fe in Elements(root))
        {
            if (fe.GetType().Name != "FastCanvasItemsControl")
                continue;
            var method = fe.GetType().GetMethod("UpdateAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
                continue;
            method.Invoke(fe, null);
            count++;
        }
        return count;
    }

    static FrameworkElement? FindItemView(DependencyObject root, IItem item) =>
        Elements(root)
            .Where(x => x.IsVisible && x.GetType().Name == "TimelineItemView" && ReferenceEquals(ItemOf(x.DataContext), item))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    static FrameworkElement? FindLargest(DependencyObject root, Func<FrameworkElement, bool> predicate) =>
        Elements(root)
            .Where(x => x.IsVisible && x.ActualWidth > 5 && x.ActualHeight > 5 && predicate(x))
            .OrderByDescending(x => x.ActualWidth * x.ActualHeight)
            .FirstOrDefault();

    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
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

    static IItem? ItemOf(object? dc)
    {
        if (dc is IItem direct) return direct;
        if (dc is null) return null;

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
                var value = p.GetValue(dc);
                if (value is IItem item) return item;
                if (value is IEnumerable en && value is not string)
                    foreach (var x in en)
                        if (x is IItem nested)
                            return nested;
            }
            catch { }
        }
        return null;
    }

    static void WriteResult(string status, IEnumerable<string> details) =>
        File.WriteAllLines(
            Path.Combine(output, "result.txt"),
            new[] { "status=" + status }.Concat(details),
            new UTF8Encoding(false));

    static void Fail(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
            WriteResult("FAIL_EXCEPTION", ["message=" + ex.GetBaseException().Message]);
        }
        catch { }
    }
}
