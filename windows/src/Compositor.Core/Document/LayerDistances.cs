using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>A gap shown on the canvas: along x (<paramref name="Horizontal"/>) or y from <paramref name="From"/> to
/// <paramref name="To"/>, at <paramref name="Across"/> on the other axis, in document pixels.</summary>
public readonly record struct DistanceLine(bool Horizontal, float From, float To, float Across)
{
    public float Length => To - From;
}

/// <summary>The pixel gaps the Move tool shows: while a layer is dragged, and with Ctrl held over another layer.</summary>
public static class LayerDistances
{
    /// <summary>While <paramref name="box"/> moves: from each of its sides to the nearest edge facing it, of a layer level
    /// with it or the canvas.</summary>
    public static List<DistanceLine> Moving(SKRect box, IEnumerable<SKRect> others, SKSize canvas)
    {
        var page = SKRect.Create(canvas);
        var lines = new List<DistanceLine>();
        var rects = others.Append(page).ToArray();
        foreach (var horizontal in new[] { true, false })
        {
            var level = rects.Where(rect => horizontal ? Overlap(rect.Top, rect.Bottom, box.Top, box.Bottom) : Overlap(rect.Left, rect.Right, box.Left, box.Right));
            var low = horizontal ? box.Left : box.Top; var high = horizontal ? box.Right : box.Bottom;
            // Each side: the closest edge past it, and the middle of what the two have in common across.
            (float Edge, SKRect Rect)? before = null, after = null;
            foreach (var rect in level)
                foreach (var edge in horizontal ? new[] { rect.Left, rect.Right } : [rect.Top, rect.Bottom])
                {
                    if (edge <= low && edge > (before?.Edge ?? float.NegativeInfinity)) before = (edge, rect);
                    if (edge >= high && edge < (after?.Edge ?? float.PositiveInfinity)) after = (edge, rect);
                }
            if (before is { } b && low - b.Edge >= .5f) lines.Add(new(horizontal, b.Edge, low, Middle(box, b.Rect, horizontal)));
            if (after is { } a && a.Edge - high >= .5f) lines.Add(new(horizontal, high, a.Edge, Middle(box, a.Rect, horizontal)));
        }
        return lines;
    }

    /// <summary>Between the selected <paramref name="box"/> and <paramref name="other"/>: the gap across each axis they're
    /// apart on; where they overlap, from each side of one to the same side of the other.</summary>
    public static List<DistanceLine> Between(SKRect box, SKRect other)
    {
        List<DistanceLine> gaps = [], insets = [];
        foreach (var horizontal in new[] { true, false })
        {
            var (low, high) = horizontal ? (box.Left, box.Right) : (box.Top, box.Bottom);
            var (otherLow, otherHigh) = horizontal ? (other.Left, other.Right) : (other.Top, other.Bottom);
            var across = Middle(box, other, horizontal);
            if (otherLow >= high) gaps.Add(new(horizontal, high, otherLow, across));
            else if (otherHigh <= low) gaps.Add(new(horizontal, otherHigh, low, across));
            else
                foreach (var (a, b) in new[] { (low, otherLow), (high, otherHigh) })
                    if (Math.Abs(a - b) >= .5f) insets.Add(new(horizontal, Math.Min(a, b), Math.Max(a, b), across));
        }
        // Side by side, only the gap between them; the sides' insets count only when one overlaps the other.
        return gaps.Count == 0 ? insets : gaps;
    }

    private static bool Overlap(float a0, float a1, float b0, float b1) => a0 < b1 && a1 > b0;

    /// <summary>Where a line between <paramref name="box"/> and <paramref name="other"/> runs across: the middle of the
    /// span they share, or of the box if none.</summary>
    private static float Middle(SKRect box, SKRect other, bool horizontal)
    {
        var low = horizontal ? Math.Max(box.Top, other.Top) : Math.Max(box.Left, other.Left);
        var high = horizontal ? Math.Min(box.Bottom, other.Bottom) : Math.Min(box.Right, other.Right);
        return high > low ? (low + high) / 2 : horizontal ? box.MidY : box.MidX;
    }
}
