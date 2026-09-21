using System.Collections.Immutable;
using System.Windows;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal readonly record struct CollapsedSpan(int Start, int End)
{
    public bool ContainsHidden(int layer) => Start < layer && layer <= End;
    public bool ContainsSpan(CollapsedSpan other) => Start <= other.Start && other.End <= End;
    public bool Intersects(CollapsedSpan other) => Start <= other.End && other.Start <= End;
}

internal sealed class FolderLayout
{
    private readonly ImmutableArray<CollapsedSpan> spans;
    private readonly ImmutableArray<int> visibleLayers;
    private readonly ImmutableDictionary<int, int> displayRowByLogical;
    private readonly ImmutableDictionary<int, int> ownerByLogical;

    private FolderLayout(
        int maxLayer,
        ImmutableArray<CollapsedSpan> spans,
        ImmutableArray<int> visibleLayers,
        ImmutableDictionary<int, int> displayRowByLogical,
        ImmutableDictionary<int, int> ownerByLogical)
    {
        MaxLayer = maxLayer;
        this.spans = spans;
        this.visibleLayers = visibleLayers;
        this.displayRowByLogical = displayRowByLogical;
        this.ownerByLogical = ownerByLogical;
    }

    public int MaxLayer { get; }
    public IReadOnlyList<int> VisibleLayers => visibleLayers;
    public string VisibleCsv => string.Join(",", visibleLayers);

    public static FolderLayout Create(int maxLayer, IEnumerable<CollapsedSpan> input)
    {
        if (maxLayer < 0)
            throw new ArgumentOutOfRangeException(nameof(maxLayer));

        var spans = input
            .Distinct()
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.End)
            .ToImmutableArray();

        foreach (var span in spans)
        {
            if (span.Start < 0 || span.End < span.Start || span.End > maxLayer)
                throw new ArgumentOutOfRangeException(nameof(input), $"Invalid span {span} for maxLayer={maxLayer}.");
        }

        for (var i = 0; i < spans.Length; i++)
        {
            for (var j = i + 1; j < spans.Length; j++)
            {
                var a = spans[i];
                var b = spans[j];
                if (!a.Intersects(b))
                    continue;
                if (a.ContainsSpan(b) || b.ContainsSpan(a))
                    continue;
                throw new InvalidOperationException($"Crossing collapsed spans are ambiguous: {a} vs {b}.");
            }
        }

        var hidden = new bool[maxLayer + 1];
        foreach (var span in spans)
            for (var layer = span.Start + 1; layer <= span.End; layer++)
                hidden[layer] = true;

        var visible = Enumerable.Range(0, maxLayer + 1)
            .Where(layer => !hidden[layer])
            .ToImmutableArray();

        var display = visible
            .Select((layer, row) => (layer, row))
            .ToImmutableDictionary(x => x.layer, x => x.row);

        var owners = ImmutableDictionary.CreateBuilder<int, int>();
        for (var layer = 0; layer <= maxLayer; layer++)
        {
            if (!hidden[layer])
            {
                owners[layer] = layer;
                continue;
            }

            var owner = spans
                .Where(span => span.ContainsHidden(layer) && !hidden[span.Start])
                .Select(span => span.Start)
                .DefaultIfEmpty(-1)
                .Min();

            if (owner < 0)
                throw new InvalidOperationException($"No visible collapsed-folder owner for hidden logical layer {layer}.");

            owners[layer] = owner;
        }

        return new FolderLayout(maxLayer, spans, visible, display, owners.ToImmutable());
    }

    public bool IsHidden(int logicalLayer) => OwnerLogical(logicalLayer) != logicalLayer;

    public int OwnerLogical(int logicalLayer)
    {
        if (!ownerByLogical.TryGetValue(logicalLayer, out var owner))
            throw new ArgumentOutOfRangeException(nameof(logicalLayer));
        return owner;
    }

    public int VisualRowOfLogical(int logicalLayer)
    {
        var owner = OwnerLogical(logicalLayer);
        return displayRowByLogical[owner];
    }

    public int DisplayRowToLogical(int displayRow)
    {
        if ((uint)displayRow >= (uint)visibleLayers.Length)
            throw new ArgumentOutOfRangeException(nameof(displayRow));
        return visibleLayers[displayRow];
    }

    public int DisplayYToLogical(double y, int layerHeight)
    {
        if (layerHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(layerHeight));
        var row = Math.Max(0, (int)Math.Floor(y / layerHeight));
        row = Math.Min(row, visibleLayers.Length - 1);
        return DisplayRowToLogical(row);
    }

    public Point MapDisplayPointToLogical(Point point, int layerHeight)
    {
        if (layerHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(layerHeight));

        var displayRow = Math.Max(0, (int)Math.Floor(point.Y / layerHeight));
        displayRow = Math.Min(displayRow, visibleLayers.Length - 1);
        var offset = point.Y - displayRow * layerHeight;
        var logical = DisplayRowToLogical(displayRow);
        return new Point(point.X, logical * layerHeight + offset);
    }

    public double TranslationY(int logicalLayer, int layerHeight) =>
        (VisualRowOfLogical(logicalLayer) - logicalLayer) * (double)layerHeight;
}
