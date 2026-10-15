using System.Security.Cryptography;
using System.Text;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Core.Document;

public static class SliceEdits
{
    public static IReadOnlyList<DocumentMark> Divide(DocumentMark slice, int columns, int rows, double pixelWidth = 0, double pixelHeight = 0)
    {
        if (slice.Kind != MarkKind.Slice || !slice.IsValid || columns < 1 || rows < 1 || columns * (long)rows > 10000 || pixelWidth < 0 || pixelHeight < 0) return [];
        var result = new List<DocumentMark>();
        for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
        {
            var left = pixelWidth > 0 ? column * pixelWidth : column * slice.Width / columns; var top = pixelHeight > 0 ? row * pixelHeight : row * slice.Height / rows;
            var right = Math.Min(slice.Width, pixelWidth > 0 ? left + pixelWidth : (column + 1) * slice.Width / columns); var bottom = Math.Min(slice.Height, pixelHeight > 0 ? top + pixelHeight : (row + 1) * slice.Height / rows);
            if (right > left && bottom > top) result.Add(slice with { ID = Guid.NewGuid(), X = slice.X + left, Y = slice.Y + top, Width = right - left, Height = bottom - top, Name = $"{slice.Name}-{row + 1}-{column + 1}" });
        }
        return result;
    }
    /// <summary>Uncovered grid cells are automatic slices. Their IDs remain stable while their geometry is unchanged.</summary>
    public static IReadOnlyList<DocumentMark> Automatic(CanvasDocument doc)
    {
        var slices = doc.Marks.Where(m => m.Kind == MarkKind.Slice && m.Visible).ToArray();
        var xs = slices.SelectMany(m => new[] { m.X, m.X + m.Width }).Where(x => x > 0 && x < doc.Width).Append(0).Append(doc.Width).Distinct().Order().ToArray();
        var ys = slices.SelectMany(m => new[] { m.Y, m.Y + m.Height }).Where(y => y > 0 && y < doc.Height).Append(0).Append(doc.Height).Distinct().Order().ToArray();
        if ((long)xs.Length * ys.Length > 20000) return [];
        var result = new List<DocumentMark>();
        for (var y = 0; y + 1 < ys.Length; y++) for (var x = 0; x + 1 < xs.Length; x++)
        {
            var cx = (xs[x] + xs[x + 1]) / 2; var cy = (ys[y] + ys[y + 1]) / 2;
            if (slices.Any(m => cx >= m.X && cy >= m.Y && cx < m.X + m.Width && cy < m.Y + m.Height)) continue;
            var key = FormattableString.Invariant($"{doc.ID}:auto:{xs[x]}:{ys[y]}:{xs[x + 1]}:{ys[y + 1]}");
            result.Add(new DocumentMark { ID = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(key))), Kind = MarkKind.Slice, X = xs[x], Y = ys[y], Width = xs[x + 1] - xs[x], Height = ys[y + 1] - ys[y], Name = $"auto-{result.Count + 1}", Color = 0xff9c9c9c });
        }
        return result;
    }
    public static bool Arrange(CanvasDocument doc, IReadOnlySet<Guid> selected, string action, bool canvas = false)
    {
        var items = doc.Marks.Where(m => m.Kind == MarkKind.Slice && selected.Contains(m.ID)).ToArray(); if (items.Length == 0) return false;
        if (action.Contains("Front") || action.Contains("Back") || action.Contains("Forward"))
        {
            var before = doc.Marks.ToArray();
            if (action.Contains("to Front") || action.Contains("to Back")) { doc.Marks.RemoveAll(m => selected.Contains(m.ID)); if (action.Contains("Front")) doc.Marks.AddRange(items); else doc.Marks.InsertRange(0, items); }
            else if (action.Contains("Forward")) { for (var i = doc.Marks.Count - 2; i >= 0; i--) if (selected.Contains(doc.Marks[i].ID) && !selected.Contains(doc.Marks[i + 1].ID)) (doc.Marks[i], doc.Marks[i + 1]) = (doc.Marks[i + 1], doc.Marks[i]); }
            else { for (var i = 1; i < doc.Marks.Count; i++) if (selected.Contains(doc.Marks[i].ID) && !selected.Contains(doc.Marks[i - 1].ID)) (doc.Marks[i], doc.Marks[i - 1]) = (doc.Marks[i - 1], doc.Marks[i]); }
            return !doc.Marks.SequenceEqual(before);
        }
        var left = canvas ? 0 : items.Min(m => m.X); var top = canvas ? 0 : items.Min(m => m.Y);
        var right = canvas ? doc.Width : items.Max(m => m.X + m.Width); var bottom = canvas ? doc.Height : items.Max(m => m.Y + m.Height);
        var changed = false;
        void Replace(DocumentMark mark, DocumentMark next) { if (next == mark || !next.IsValid) return; doc.Marks[doc.Marks.FindIndex(m => m.ID == mark.ID)] = next; changed = true; }
        if (action.StartsWith("Distribute", StringComparison.Ordinal))
        {
            if (items.Length < 3) return false;
            var horizontal = action.Contains("Horizontal") || action.Contains("Left") || action.Contains("Right") || action.Contains("H Center");
            var spacing = action.Contains("Spacing");
            double Position(DocumentMark m) => horizontal ? m.X + (action.Contains("Right") ? m.Width : action.Contains("Center") ? m.Width / 2 : 0) : m.Y + (action.Contains("Bottom") ? m.Height : action.Contains("Center") ? m.Height / 2 : 0);
            var ordered = items.OrderBy(Position).ToArray(); var start = Position(ordered[0]); var end = Position(ordered[^1]);
            if (canvas)
            {
                var fraction = action.Contains("Center") ? .5 : action.Contains("Right") || action.Contains("Bottom") ? 1 : 0;
                start = (horizontal ? ordered[0].Width : ordered[0].Height) * fraction;
                end = (horizontal ? doc.Width : doc.Height) - (horizontal ? ordered[^1].Width : ordered[^1].Height) * (1 - fraction);
            }
            var distance = (end - start) / (items.Length - 1);
            if (spacing) { start = horizontal ? left : top; distance = ((horizontal ? right - left : bottom - top) - items.Sum(m => horizontal ? m.Width : m.Height)) / (items.Length - 1); }
            var cursor = start;
            for (var i = 0; i < ordered.Length; i++) { var m = ordered[i]; var offset = (spacing ? cursor : start + distance * i) - Position(m); Replace(m, horizontal ? m with { X = m.X + offset } : m with { Y = m.Y + offset }); if (spacing) cursor += (horizontal ? m.Width : m.Height) + distance; }
        }
        else foreach (var m in items)
        {
            var next = action switch { "Align Slice Left" => m with { X = left }, "Align Slice Center" => m with { X = (left + right - m.Width) / 2 }, "Align Slice Right" => m with { X = right - m.Width }, "Align Slice Top" => m with { Y = top }, "Align Slice Middle" => m with { Y = (top + bottom - m.Height) / 2 }, "Align Slice Bottom" => m with { Y = bottom - m.Height }, _ => m }; Replace(m, next);
        }
        return changed;
    }
}
