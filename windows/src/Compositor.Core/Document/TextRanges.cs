using Compositor.Core.Format;

namespace Compositor.Core.Document;

/// <summary>UTF-16 character ranges. Runs own flat styles, never other runs or document content.</summary>
public static class TextRanges
{
    public static LayerTextStyle At(LayerTextStyle style, int index)
    {
        var runs = style.StyleRuns;
        if (runs is not null)
        {
            var lo = 0; var hi = runs.Count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2; var r = runs[mid];
                if (index < r.Location) hi = mid - 1;
                else if (index >= r.Location + r.Length) lo = mid + 1;
                else return r.Style;
            }
        }
        return style;
    }

    public static LayerTextStyle Flat(LayerTextStyle style)
    {
        var value = TextEdits.Copy(style); value.Content = ""; value.StyleRuns = null;
        value.ColorRuns = null; value.FontRuns = null; value.BoxSize = null; value.Dynamic = null;
        return value;
    }

    private static LayerTextStyle Resolved(LayerTextStyle style, int index)
    {
        var source = At(style, index); var value = Flat(source);
        if (!ReferenceEquals(source, style)) return value;
        var color = style.ColorRuns?.FirstOrDefault(r => index >= r.Location && index < r.Location + r.Length);
        if (color is not null) { value.Red = color.Red; value.Green = color.Green; value.Blue = color.Blue; }
        var font = style.FontRuns?.FirstOrDefault(r => index >= r.Location && index < r.Location + r.Length);
        if (font is not null) value.FontName = font.FontName;
        return value;
    }

    private static SortedSet<int> Boundaries(LayerTextStyle style)
    {
        var points = new SortedSet<int> { 0, style.Content.Length };
        foreach (var r in style.StyleRuns ?? []) { points.Add(r.Location); points.Add(r.Location + r.Length); }
        foreach (var r in style.ColorRuns ?? []) { points.Add(r.Location); points.Add(r.Location + r.Length); }
        foreach (var r in style.FontRuns ?? []) { points.Add(r.Location); points.Add(r.Location + r.Length); }
        return points;
    }

    public static LayerTextStyle Apply(LayerTextStyle style, int start, int length, Action<LayerTextStyle> change)
    {
        if (start < 0 || length < 0 || start > style.Content.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
        var result = TextEdits.Copy(style);
        if (length == 0) { change(result); return result; }
        var points = Boundaries(style); points.Add(start); points.Add(start + length);
        var cuts = points.ToArray(); var runs = new List<LayerTextStyleRun>();
        for (var i = 0; i + 1 < cuts.Length; i++)
        {
            var value = Resolved(style, cuts[i]);
            if (cuts[i] >= start && cuts[i] < start + length) change(value);
            runs.Add(new() { Location = cuts[i], Length = cuts[i + 1] - cuts[i], Style = Flat(value) });
        }
        result.StyleRuns = runs.Count > 0 ? runs : null; result.ColorRuns = null; result.FontRuns = null;
        return result;
    }

    public static LayerTextStyle Splice(LayerTextStyle style, int at, int remove, string insert)
    {
        var result = TextEdits.Copy(style); result.Content = style.Content.Remove(at, remove).Insert(at, insert);
        var points = Boundaries(style); points.Add(at); points.Add(at + remove);
        var cuts = points.ToArray(); var runs = new List<LayerTextStyleRun>();
        for (var i = 0; i + 1 < cuts.Length; i++)
        {
            var start = cuts[i]; var end = cuts[i + 1];
            if (start >= at && end <= at + remove) continue;
            runs.Add(new() { Location = start >= at + remove ? start - remove + insert.Length : start,
                Length = end - start, Style = Resolved(style, start) });
        }
        if (insert.Length > 0) runs.Add(new() { Location = at, Length = insert.Length, Style = Resolved(style, Math.Clamp(remove > 0 || at == 0 ? at : at - 1, 0, Math.Max(0, style.Content.Length - 1))) });
        result.StyleRuns = runs.Count > 0 && (style.StyleRuns is not null || style.ColorRuns is not null || style.FontRuns is not null)
            ? runs.OrderBy(r => r.Location).ToList() : null;
        result.ColorRuns = null; result.FontRuns = null; return result;
    }
}
