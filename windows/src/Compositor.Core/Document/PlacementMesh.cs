using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>A subdividable, piecewise bilinear warp, held only during placement. Source pixels are never overwritten by previews.</summary>
public sealed class PlacementMesh
{
    public List<float> Columns { get; } = [0, 1];
    public List<float> Rows { get; } = [0, 1];
    public List<SKPoint> Points { get; } = [];
    private readonly bool _flipX, _flipY;
    public PlacementMesh(LayerTransform box)
    { _flipX = box.FlipX; _flipY = box.FlipY; Points.AddRange([box.Point(0, 0), box.Point(1, 0), box.Point(0, 1), box.Point(1, 1)]); }
    public SKPoint Map(float u, float v)
    {
        var x = Math.Clamp(Columns.FindLastIndex(n => n <= u), 0, Columns.Count - 2);
        var y = Math.Clamp(Rows.FindLastIndex(n => n <= v), 0, Rows.Count - 2);
        var a = (u - Columns[x]) / (Columns[x + 1] - Columns[x]); var b = (v - Rows[y]) / (Rows[y + 1] - Rows[y]);
        var p = Points[y * Columns.Count + x]; var q = Points[y * Columns.Count + x + 1];
        var r = Points[(y + 1) * Columns.Count + x]; var s = Points[(y + 1) * Columns.Count + x + 1];
        return Scale(p, (1 - a) * (1 - b)) + Scale(q, a * (1 - b)) + Scale(r, (1 - a) * b) + Scale(s, a * b);
    }
    public static SKPoint Scale(SKPoint p, float value) => new(p.X * value, p.Y * value);
    public bool Split(bool horizontal, float location)
    {
        var axis = horizontal ? Rows : Columns;
        if (axis.Count >= 9 || location <= .01 || location >= .99 || axis.Any(v => Math.Abs(v - location) < .01)) return false;
        var next = axis.Append(location).Order().ToArray();
        var positions = new List<SKPoint>();
        foreach (var y in horizontal ? next : Rows.ToArray()) foreach (var x in horizontal ? Columns.ToArray() : next) positions.Add(Map(x, y));
        axis.Clear(); axis.AddRange(next); Points.Clear(); Points.AddRange(positions); return true;
    }
    public bool RemoveSplit(int column, int row)
    {
        var removeColumn = column > 0 && column < Columns.Count - 1;
        var removeRow = row > 0 && row < Rows.Count - 1;
        if (!removeColumn && !removeRow) return false;
        var kept = Points.Where((_, i) => (!removeColumn || i % Columns.Count != column) && (!removeRow || i / Columns.Count != row)).ToArray();
        if (removeColumn) Columns.RemoveAt(column); if (removeRow) Rows.RemoveAt(row);
        Points.Clear(); Points.AddRange(kept); return true;
    }
    public WarpedImage? Render(SKBitmap image)
    {
        if (Points.Any(p => !float.IsFinite(p.X + p.Y) || Math.Abs(p.X) > 1_000_000 || Math.Abs(p.Y) > 1_000_000)) return null;
        var bounds = DistortWarp.Bounds(Points);
        if (bounds.Width > DocumentLimits.MaxSide || bounds.Height > DocumentLimits.MaxSide || (long)bounds.Width * bounds.Height > DocumentLimits.MaxSurfacePixels) return null;
        var target = Bitmaps.Allocate(Bitmaps.ColorInfo(bounds.Width, bounds.Height)); target.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(target); canvas.Translate(-bounds.Left, -bounds.Top);
        using var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        const int steps = 8;
        var vertices = new List<SKPoint>(); var texture = new List<SKPoint>();
        for (var cy = 0; cy < Rows.Count - 1; cy++) for (var cx = 0; cx < Columns.Count - 1; cx++)
            for (var y = 0; y < steps; y++) for (var x = 0; x < steps; x++)
            {
                void Add(int dx, int dy)
                {
                    var u = Columns[cx] + (Columns[cx + 1] - Columns[cx]) * (x + dx) / steps;
                    var v = Rows[cy] + (Rows[cy + 1] - Rows[cy]) * (y + dy) / steps;
                    vertices.Add(Map(u, v)); texture.Add(new SKPoint((_flipX ? 1 - u : u) * image.Width, (_flipY ? 1 - v : v) * image.Height));
                }
                Add(0, 0); Add(1, 0); Add(1, 1); Add(0, 0); Add(1, 1); Add(0, 1);
            }
        using var mesh = SKVertices.CreateCopy(SKVertexMode.Triangles, vertices.ToArray(), texture.ToArray(), null);
        canvas.DrawVertices(mesh, SKBlendMode.Modulate, paint); canvas.Flush();
        return new WarpedImage(target, new LayerTransform(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }
}
