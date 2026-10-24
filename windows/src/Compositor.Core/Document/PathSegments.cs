using SkiaSharp;
namespace Compositor.Core.Document;
public static class PathSegments
{
    public static (int Subpath, int Node, float T)? Find(PathNodes path, Func<SKPoint, SKPoint> map, SKPoint at, float tolerance)
    {
        (int, int, float)? best = null; var distance = tolerance;
        for (var s = 0; s < path.Subpaths.Count; s++)
        {
            var contour = path.Subpaths[s]; var count = contour.Closed ? contour.Nodes.Count : contour.Nodes.Count - 1;
            for (var n = 0; n < count; n++) for (var step = 1; step < 32; step++)
            {
                var a = contour.Nodes[n]; var b = contour.Nodes[(n + 1) % contour.Nodes.Count]; var t = step / 32f; var u = 1 - t;
                var p = Scale(a.Point, u*u*u) + Scale(a.Out ?? a.Point, 3*u*u*t) + Scale(b.In ?? b.Point, 3*u*t*t) + Scale(b.Point, t*t*t);
                var d = SKPoint.Distance(map(p), at); if (d < distance) { distance = d; best = (s, n, t); }
            }
        }
        return best;
    }
    public static void Move(PathNodes path, int subpath, int node, float t, SKPoint delta, bool constrain)
    {
        var contour = path.Subpaths[subpath]; var next = (node + 1) % contour.Nodes.Count; var a = contour.Nodes[node]; var b = contour.Nodes[next];
        if (a.Out is null && b.In is null) { path.MoveNode(subpath, node, delta); path.MoveNode(subpath, next, delta); return; }
        t = Math.Clamp(t, .05f, .95f); var c1 = 3*(1-t)*(1-t)*t; var c2 = 3*(1-t)*t*t; var total = c1*c1 + c2*c2;
        var first = (a.Out ?? a.Point) + Scale(delta, c1 / total); var second = (b.In ?? b.Point) + Scale(delta, c2 / total);
        if (!constrain)
        {
            if (a.In is { } oppositeA) { var v = first - a.Point; a.In = a.Point - Scale(v, SKPoint.Distance(oppositeA, a.Point) / Math.Max(.0001f, SKPoint.Distance(first, a.Point))); }
            if (b.Out is { } oppositeB) { var v = second - b.Point; b.Out = b.Point - Scale(v, SKPoint.Distance(oppositeB, b.Point) / Math.Max(.0001f, SKPoint.Distance(second, b.Point))); }
        }
        a.Out = first; b.In = second;
    }
    private static SKPoint Scale(SKPoint p, float n) => new(p.X*n, p.Y*n);
}
