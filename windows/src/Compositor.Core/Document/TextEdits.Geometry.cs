using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.Document;

public static partial class TextEdits
{
    private static List<Piece> GeometryLayout(List<Piece> pieces, LayerTextStyle style, List<Stop>? stops, ref double width, ref double height)
    {
        if (style.Geometry is not { } geometry || pieces.Count == 0) return pieces;
        var w = Math.Max(1, width); var h = Math.Max(1, height); var baseline = Padding + Baseline(style);
        SKPoint Map(double x, double y)
        {
            var u = (x - Padding) / w * 2 - 1; var bend = geometry.Bend / 100;
            if (geometry.Shape == TextGeometryShape.Circle)
            {
                var radius = Math.Max(style.FontSize * 1.5, w / (2 * Math.PI));
                var angle = (x - Padding) / w * 2 * Math.PI - Math.PI;
                var r = radius + baseline - y;
                x = radius + Padding + r * Math.Sin(angle); y = radius + Padding - r * Math.Cos(angle);
            }
            else if (geometry.Shape == TextGeometryShape.Arch) y -= bend * w * .25 * (1 - u * u);
            else if (geometry.Shape == TextGeometryShape.Bowtie) y = baseline + (y - baseline) * (1 - bend * (1 - Math.Abs(u)) * .7);
            x += geometry.HorizontalDistortion / 100 * w * (y - baseline) / h;
            y += geometry.VerticalDistortion / 100 * h * u;
            return new((float)x, (float)y);
        }
        var individual = new List<Piece>();
        foreach (var p in pieces)
        {
            if (p.Shaped && p.Glyphs is { } glyphs && p.Positions is { } positions)
                for (var i = 0; i < glyphs.Length; i++) individual.Add(p with { X = p.X + positions[i].X, Y = p.Y + positions[i].Y,
                    Glyphs = [glyphs[i]], Positions = [new(0, 0)], Width = Math.Max(1, i + 1 < positions.Length ? positions[i + 1].X - positions[i].X : p.Width - positions[i].X) });
            else individual.Add(p);
        }
        var left = double.PositiveInfinity; var top = double.PositiveInfinity; var right = double.NegativeInfinity; var bottom = double.NegativeInfinity;
        for (var i = 0; i < individual.Count; i++)
        {
            var p = individual[i]; var origin = Map(p.X, p.Y); var tangent = Map(p.X + 1, p.Y) - origin; var normal = Map(p.X, p.Y + 1) - origin;
            var sx = Math.Max(.05, tangent.Length); var sy = Math.Max(.05, normal.Length);
            individual[i] = p with { X = origin.X, Y = origin.Y, Angle = (float)(Math.Atan2(tangent.Y, tangent.X) * 180 / Math.PI),
                Scale = (float)(p.Scale * sy), Horizontal = (float)(p.Horizontal * sx / sy), Width = (float)(p.Width * sx) };
            var size = style.FontSize * p.Scale;
            foreach (var point in new[] { Map(p.X, p.Y - size), Map(p.X + p.Width, p.Y - size), Map(p.X, p.Y + size * .3), Map(p.X + p.Width, p.Y + size * .3) })
            { left = Math.Min(left, point.X); top = Math.Min(top, point.Y); right = Math.Max(right, point.X); bottom = Math.Max(bottom, point.Y); }
        }
        var dx = (float)(Padding - left); var dy = (float)(Padding - top);
        for (var i = 0; i < individual.Count; i++) individual[i] = individual[i] with { X = individual[i].X + dx, Y = individual[i].Y + dy };
        if (stops is not null) for (var i = 0; i < stops.Count; i++) { var mapped = Map(stops[i].X, stops[i].Y); stops[i] = stops[i] with { X = mapped.X + dx, Y = mapped.Y + dy }; }
        width = right - left; height = bottom - top; return individual;
    }
}
