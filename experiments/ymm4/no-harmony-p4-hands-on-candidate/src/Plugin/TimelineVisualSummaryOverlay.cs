using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Ymm4NoHarmonyVisualSummary;
using YukkuriMovieMaker.Project.Items;
using YmmGroupItem = YukkuriMovieMaker.Project.Items.GroupItem;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed class TimelineVisualSummaryOverlay : IDisposable
{
    private readonly Host host;
    private readonly DirectDisplay display;
    private readonly Action<string> log;
    private readonly AdornerLayer layer;
    private readonly SummaryAdorner adorner;
    private bool disposed;

    internal int TimingBandCount { get; private set; }
    internal int GroupSegmentCount { get; private set; }

    private TimelineVisualSummaryOverlay(
        Host host,
        DirectDisplay display,
        Action<string> log,
        AdornerLayer layer)
    {
        this.host = host;
        this.display = display;
        this.log = log;
        this.layer = layer;

        adorner = new SummaryAdorner(host.Source);
        layer.Add(adorner);

        display.Applied += Display_Applied;
        Refresh();
    }

    internal static TimelineVisualSummaryOverlay? TryCreate(
        Host host,
        DirectDisplay display,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(log);

        var layer = AdornerLayer.GetAdornerLayer(host.Source);
        if (layer is null)
        {
            log(
                "visual_summary_unavailable reason=no_adorner_layer " +
                $"source={host.Source.GetType().FullName}");
            return null;
        }

        return new TimelineVisualSummaryOverlay(
            host,
            display,
            log,
            layer);
    }

    internal void Refresh()
    {
        if (disposed)
            return;

        try
        {
            display.ThrowIfFailed();

            var geometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(
                    host.Vm);

            var itemByKey =
                new Dictionary<string, TimelineItemGeometry>(
                    StringComparer.Ordinal);
            var visualItems =
                new List<VisualItemSpan>(geometry.Count);

            for (var i = 0; i < geometry.Count; i++)
            {
                var current = geometry[i];
                var key = i.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

                itemByKey[key] = current;
                visualItems.Add(new VisualItemSpan(
                    key,
                    current.Item.Layer,
                    current.Item.Frame,
                    current.Item.Length));
            }

            var timing =
                VisualSummaryRules.BuildTimingBands(
                    visualItems,
                    display.Layout.IsHidden,
                    display.Layout.OwnerLogical,
                    display.Layout.VisualRowOfLogical);

            var timingDraw = BuildTimingDraw(
                timing,
                itemByKey,
                display.Height);

            var groupDraw = new List<GroupDraw>();

            foreach (var current in geometry)
            {
                if (current.Item is not YmmGroupItem group
                    || group.GroupRange < 1)
                {
                    continue;
                }

                var segments =
                    VisualSummaryRules.BuildGroupSegments(
                        group.Layer,
                        group.GroupRange,
                        display.Layout.OwnerLogical,
                        display.Layout.VisualRowOfLogical);

                foreach (var segment in segments)
                {
                    groupDraw.Add(new GroupDraw(
                        new Rect(
                            current.Left,
                            segment.VisualStartRow
                                * (double)display.Height,
                            Math.Max(1, current.Width),
                            segment.VisualRowCount
                                * (double)display.Height),
                        group.Layer));
                }
            }

            TimingBandCount = timingDraw.Count;
            GroupSegmentCount = groupDraw.Count;

            adorner.SetSnapshot(
                timingDraw,
                groupDraw);

            log(
                $"visual_summary_refresh timing={TimingBandCount} " +
                $"groups={GroupSegmentCount} " +
                $"geometry={geometry.Count}");
        }
        catch (Exception ex)
        {
            log("visual_summary_refresh_error=" + ex);
            adorner.SetSnapshot([], []);
        }
    }

    private static IReadOnlyList<TimingDraw> BuildTimingDraw(
        IReadOnlyList<TimingBand> bands,
        IReadOnlyDictionary<string, TimelineItemGeometry> itemByKey,
        int layerHeight)
    {
        if (bands.Count == 0)
            return Array.Empty<TimingDraw>();

        var result = new List<TimingDraw>(bands.Count);

        foreach (var ownerGroup in bands.GroupBy(
            band => band.OwnerLayer))
        {
            var sources = ownerGroup
                .Select(band => band.SourceLayer)
                .Distinct()
                .OrderBy(layer => layer)
                .ToArray();

            var visibleLaneCount = Math.Max(
                1,
                Math.Min(6, sources.Length));
            var summaryHeight = Math.Max(
                5,
                Math.Min(12, layerHeight * 0.34));
            var laneHeight = summaryHeight
                / visibleLaneCount;
            var row = ownerGroup.First().VisualRow;
            var topBase =
                (row + 1) * (double)layerHeight
                - summaryHeight
                - 1;

            foreach (var band in ownerGroup)
            {
                if (!itemByKey.TryGetValue(
                        band.Key,
                        out var geometry))
                {
                    continue;
                }

                var sourceIndex = Array.IndexOf(
                    sources,
                    band.SourceLayer);
                var lane = sourceIndex < 0
                    ? 0
                    : sourceIndex % visibleLaneCount;

                var rect = new Rect(
                    geometry.Left,
                    topBase + lane * laneHeight,
                    Math.Max(1, geometry.Width),
                    Math.Max(1, laneHeight - 0.5));

                result.Add(new TimingDraw(
                    rect,
                    band.SourceLayer,
                    band.OwnerLayer));
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private void Display_Applied(
        object? sender,
        EventArgs e) =>
        Refresh();

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        display.Applied -= Display_Applied;
        layer.Remove(adorner);
    }

    private readonly record struct TimingDraw(
        Rect Rect,
        int SourceLayer,
        int OwnerLayer);

    private readonly record struct GroupDraw(
        Rect Rect,
        int GroupLayer);

    private sealed class SummaryAdorner : Adorner
    {
        private static readonly Brush TimingBrush =
            Freeze(new SolidColorBrush(
                Color.FromArgb(
                    175,
                    235,
                    235,
                    235)));

        private static readonly Brush GroupFill =
            Freeze(new SolidColorBrush(
                Color.FromArgb(
                    24,
                    90,
                    145,
                    220)));

        private static readonly Pen GroupPen =
            Freeze(new Pen(
                new SolidColorBrush(
                    Color.FromArgb(
                        100,
                        90,
                        145,
                        220)),
                1));

        private IReadOnlyList<TimingDraw> timing = [];
        private IReadOnlyList<GroupDraw> groups = [];

        internal SummaryAdorner(
            UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        internal void SetSnapshot(
            IReadOnlyList<TimingDraw> timing,
            IReadOnlyList<GroupDraw> groups)
        {
            this.timing = timing;
            this.groups = groups;
            InvalidateVisual();
        }

        protected override void OnRender(
            DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var bounds = new Rect(
                AdornedElement.RenderSize);

            if (bounds.Width <= 0
                || bounds.Height <= 0)
            {
                return;
            }

            drawingContext.PushClip(
                new RectangleGeometry(bounds));

            foreach (var group in groups)
            {
                if (!group.Rect.IntersectsWith(bounds))
                    continue;

                drawingContext.DrawRectangle(
                    GroupFill,
                    GroupPen,
                    group.Rect);
            }

            foreach (var band in timing)
            {
                if (!band.Rect.IntersectsWith(bounds))
                    continue;

                drawingContext.DrawRoundedRectangle(
                    TimingBrush,
                    null,
                    band.Rect,
                    1,
                    1);
            }

            drawingContext.Pop();
        }

        private static T Freeze<T>(
            T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
