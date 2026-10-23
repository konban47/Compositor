using SkiaSharp;
namespace Compositor.Core.Document;

public sealed class BrushSymmetryMap
{
    private readonly PaintBrushOptions _options;
    private readonly SKPoint _center;
    private readonly double _radius;
    private readonly SKPoint[] _path;
    public BrushSymmetryMap(PaintBrushOptions options, int width, int height)
    {
        _options = options; _center = new((float)(width * options.SymmetryX), (float)(height * options.SymmetryY)); _radius = Math.Max(1, Math.Min(width, height) * .25 * options.SymmetryScale);
        var points = new List<SKPoint>();
        if (options.Symmetry == BrushSymmetry.SelectedPath && options.SymmetryPath is { } svg)
        {
            using var path = SKPath.ParseSvgPathData(svg); if (path is not null) { using var measure = new SKPathMeasure(path); do { for (var i = 0; i <= 128; i++) if (measure.GetPositionAndTangent(measure.Length * i / 128, out var p, out _)) points.Add(p); } while (points.Count <= 2048 && measure.NextContour()); }
        }
        else if (options.Symmetry is BrushSymmetry.Wavy or BrushSymmetry.Spiral)
            for (var i = 0; i <= 256; i++) { var t = i / 256d; points.Add(options.Symmetry == BrushSymmetry.Wavy ? World(new SKPoint((float)(Math.Sin(t * Math.PI * 4) * _radius * .3), (float)((t * 4 - 2) * _radius))) : World(new SKPoint((float)(Math.Cos(t * Math.PI * 6) * t * _radius * 2), (float)(Math.Sin(t * Math.PI * 6) * t * _radius * 2)))); }
        _path = points.ToArray();
    }
    private SKPoint Local(SKPoint p) { var a = -_options.SymmetryAngle * Math.PI / 180; p -= _center; return new((float)(p.X * Math.Cos(a) - p.Y * Math.Sin(a)), (float)(p.X * Math.Sin(a) + p.Y * Math.Cos(a))); }
    private SKPoint World(SKPoint p) { var a = _options.SymmetryAngle * Math.PI / 180; return _center + new SKPoint((float)(p.X * Math.Cos(a) - p.Y * Math.Sin(a)), (float)(p.X * Math.Sin(a) + p.Y * Math.Cos(a))); }
    public IEnumerable<BrushDab> Dabs(BrushDab dab)
    {
        yield return dab; var p = Local(dab.At);
        BrushDab Mirror(SKPoint q, double angle) => dab with { At = World(q), Angle = angle, FlipX = !dab.FlipX };
        switch (_options.Symmetry)
        {
            case BrushSymmetry.Vertical: yield return Mirror(new(-p.X, p.Y), 180 - dab.Angle + 2 * _options.SymmetryAngle); break;
            case BrushSymmetry.Horizontal: yield return Mirror(new(p.X, -p.Y), -dab.Angle + 2 * _options.SymmetryAngle); break;
            case BrushSymmetry.DualAxis:
                yield return Mirror(new(-p.X, p.Y), 180 - dab.Angle + 2 * _options.SymmetryAngle); yield return Mirror(new(p.X, -p.Y), -dab.Angle + 2 * _options.SymmetryAngle); yield return dab with { At = World(new(-p.X, -p.Y)), Angle = dab.Angle + 180 }; break;
            case BrushSymmetry.Diagonal: yield return Mirror(new(p.Y, p.X), 90 - dab.Angle + 2 * _options.SymmetryAngle); break;
            case BrushSymmetry.Parallel: yield return dab with { At = World(new((float)(p.X + _radius), p.Y)) }; break;
            case BrushSymmetry.Circle:
                var r = Math.Sqrt(p.X * p.X + p.Y * p.Y); if (r > .001) yield return Mirror(new((float)(p.X / r * (2 * _radius - r)), (float)(p.Y / r * (2 * _radius - r))), 2 * Math.Atan2(p.Y, p.X) * 180 / Math.PI - dab.Angle + 180); break;
            case BrushSymmetry.Radial: case BrushSymmetry.Mandala:
                for (var i = 0; i < _options.SymmetrySegments; i++) { var a = Math.PI * 2 * i / _options.SymmetrySegments; if (i > 0) yield return dab with { At = World(new((float)(p.X * Math.Cos(a) - p.Y * Math.Sin(a)), (float)(p.X * Math.Sin(a) + p.Y * Math.Cos(a)))), Angle = dab.Angle + a * 180 / Math.PI }; if (_options.Symmetry == BrushSymmetry.Mandala) yield return Mirror(new((float)(-p.X * Math.Cos(a) - p.Y * Math.Sin(a)), (float)(-p.X * Math.Sin(a) + p.Y * Math.Cos(a))), 180 - dab.Angle + a * 180 / Math.PI + 2 * _options.SymmetryAngle); } break;
            case BrushSymmetry.Wavy: case BrushSymmetry.Spiral: case BrushSymmetry.SelectedPath:
                double closest = double.MaxValue; SKPoint nearest = dab.At; double tangent = 0;
                for (var i = 1; i < _path.Length; i++) { var a = _path[i - 1]; var v = _path[i] - a; var length = v.X * v.X + v.Y * v.Y; if (length <= 0) continue; var t = Math.Clamp(((dab.At.X - a.X) * v.X + (dab.At.Y - a.Y) * v.Y) / length, 0, 1); var q = a + new SKPoint(v.X * t, v.Y * t); var d = SKPoint.Distance(dab.At, q); if (d < closest) { closest = d; nearest = q; tangent = Math.Atan2(v.Y, v.X) * 180 / Math.PI; } }
                if (closest < double.MaxValue) yield return dab with { At = nearest + nearest - dab.At, Angle = tangent * 2 - dab.Angle, FlipX = !dab.FlipX }; break;
        }
    }
    public IReadOnlyList<SKPoint> Guide => _path;
}
