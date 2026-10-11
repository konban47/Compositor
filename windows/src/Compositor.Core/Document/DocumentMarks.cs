using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Non-printing annotations follow document geometry, independently of layer selection.</summary>
public static class DocumentMarks
{
    public static void Transform(CanvasDocument doc, SKMatrix matrix)
    {
        for (var i = 0; i < doc.Marks.Count; i++)
        {
            var mark = doc.Marks[i]; var a = matrix.MapPoint((float)mark.X, (float)mark.Y);
            var b = matrix.MapPoint((float)(mark.X + mark.Width), (float)(mark.Y + mark.Height));
            var next = mark with { X = a.X, Y = a.Y, Width = b.X - a.X, Height = b.Y - a.Y };
            if (mark.Kind == MarkKind.Slice)
            {
                var corners = new[] { a, b, matrix.MapPoint((float)mark.X, (float)(mark.Y + mark.Height)), matrix.MapPoint((float)(mark.X + mark.Width), (float)mark.Y) };
                var x = corners.Min(p => p.X); var y = corners.Min(p => p.Y);
                next = next with { X = x, Y = y, Width = corners.Max(p => p.X) - x, Height = corners.Max(p => p.Y) - y };
            }
            if (!next.IsValid) throw new InvalidOperationException("Annotation geometry exceeds document limits.");
            doc.Marks[i] = next;
        }
    }
}
