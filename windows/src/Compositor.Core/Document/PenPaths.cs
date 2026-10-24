using SkiaSharp;

namespace Compositor.Core.Document;

public enum PenOutput { Shape, Path }
public enum PathOperation { New, Combine, Subtract, Intersect, Exclude }

public sealed class PenToolSettings
{
    public PenOutput Output = PenOutput.Path;
    public PathOperation Operation;
    public bool AutoAddDelete = true, RubberBand, AlignEdges, Magnetic, Pressure;
    public double Stroke, CurveFit = 2, MagneticWidth = 10, MagneticContrast = 10, MagneticFrequency = 57;
    public int AlignmentTarget;
    public bool AllLayers, ConstrainDragging, LinkDimensions;
}

/// <summary>Work paths use document coordinates and never contribute visible pixels.</summary>
public static class PenPaths
{
    public static void Transform(Model.CanvasDocument document, SKMatrix matrix)
    {
        if (document.WorkPath is not { } svg) return;
        using var path = SKPath.ParseSvgPathData(svg);
        if (path is null) return;
        path.Transform(matrix); document.WorkPath = path.ToSvgPathData();
    }

    public static bool Valid(string svg)
    {
        if (svg.Length is 0 or > 1_000_000) return false;
        try
        {
            using var path = SKPath.ParseSvgPathData(svg);
            return path is { IsEmpty: false } && path.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y));
        }
        catch (ArgumentException) { return false; }
    }

    public static string? Combine(string? previous, string next, PathOperation operation)
    {
        if (!Valid(next)) return null;
        if (previous is null || operation == PathOperation.New) return next;
        using var a = SKPath.ParseSvgPathData(previous); using var b = SKPath.ParseSvgPathData(next);
        if (a is null || b is null) return null;
        // Keep separate editable components until the user asks to merge them.
        if (operation == PathOperation.Combine)
        {
            using var builder = new SKPathBuilder(); builder.AddPath(a); builder.AddPath(b);
            using var combined = builder.Detach(); return combined.ToSvgPathData();
        }
        using var result = a.Op(b, operation switch { PathOperation.Subtract => SKPathOp.Difference, PathOperation.Intersect => SKPathOp.Intersect, _ => SKPathOp.Xor });
        return result is null ? null : WindingSvg(result);
    }

    public static string? Merge(string svg)
    {
        using var path = SKPath.ParseSvgPathData(svg);
        if (path is null) return null;
        // Simplify the compound fill as a whole so subtraction holes remain holes.
        using var merged = path.Simplify();
        return merged is null ? null : WindingSvg(merged);
    }

    // SVG path data cannot store a fill rule. Normalize boolean results before serialization.
    public static string? WindingSvg(SKPath path)
    {
        using var winding = path.ToWinding();
        return winding?.ToSvgPathData();
    }

    public static PathNodes Fit(IReadOnlyList<SKPoint> points, double tolerance, bool closed)
    {
        var result = new PathNodes(); var contour = new PathNodes.Subpath { Closed = closed }; result.Subpaths.Add(contour);
        if (points.Count == 0) return result;
        var keep = new SortedSet<int> { 0, points.Count - 1 }; var pending = new Stack<(int A, int B)>(); pending.Push((0, points.Count - 1));
        while (pending.TryPop(out var segment))
        {
            var a = points[segment.A]; var b = points[segment.B]; var delta = b - a;
            var length = delta.X * delta.X + delta.Y * delta.Y; var greatest = tolerance * tolerance; var chosen = -1;
            for (var i = segment.A + 1; i < segment.B; i++)
            {
                var d = points[i] - a; var t = length > 0 ? Math.Clamp((d.X * delta.X + d.Y * delta.Y) / length, 0, 1) : 0;
                var e = points[i] - (a + PlacementMesh.Scale(delta, t)); var distance = e.X * e.X + e.Y * e.Y;
                if (distance > greatest) { greatest = distance; chosen = i; }
            }
            if (chosen < 0) continue; keep.Add(chosen); pending.Push((segment.A, chosen)); pending.Push((chosen, segment.B));
        }
        if (closed && points.Count >= 3 && keep.Count < 3) keep.Add(points.Count / 2);
        contour.Closed &= keep.Count >= 3;
        foreach (var index in keep) contour.Nodes.Add(new PathNodes.Node { Point = points[index] });
        Smooth(contour); return result;
    }

    public static void Smooth(PathNodes.Subpath contour)
    {
        for (var i = 0; i < contour.Nodes.Count; i++)
        {
            if (!contour.Closed && (i == 0 || i == contour.Nodes.Count - 1)) continue;
            var previous = contour.Nodes[(i + contour.Nodes.Count - 1) % contour.Nodes.Count].Point;
            var next = contour.Nodes[(i + 1) % contour.Nodes.Count].Point;
            var tangent = PlacementMesh.Scale(next - previous, 1f / 6);
            contour.Nodes[i].In = contour.Nodes[i].Point - tangent; contour.Nodes[i].Out = contour.Nodes[i].Point + tangent;
        }
    }

    public static double DistanceToPath(PathNodes nodes, SKPoint point)
    {
        var nearest = double.PositiveInfinity;
        foreach (var contour in nodes.Subpaths)
        for (var i = 0; i < contour.Nodes.Count - (contour.Closed ? 0 : 1); i++)
        {
            var a = contour.Nodes[i]; var b = contour.Nodes[(i + 1) % contour.Nodes.Count]; var last = a.Point;
            for (var j = 1; j <= 32; j++)
            {
                var t = j / 32f; var u = 1 - t;
                var at = PlacementMesh.Scale(a.Point, u*u*u) + PlacementMesh.Scale(a.Out ?? a.Point, 3*u*u*t) + PlacementMesh.Scale(b.In ?? b.Point, 3*u*t*t) + PlacementMesh.Scale(b.Point, t*t*t);
                var v = at - last; var d = point - last; var length = v.X*v.X + v.Y*v.Y;
                var f = length > 0 ? Math.Clamp((d.X*v.X+d.Y*v.Y)/length, 0, 1) : 0;
                nearest = Math.Min(nearest, SKPoint.Distance(point, last + PlacementMesh.Scale(v, f))); last = at;
            }
        }
        return nearest;
    }
}
