using System.Globalization;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// A vector outline as editable nodes, so a path can be picked apart and put back together: each node has a
/// place and, for a curve, a handle on either side. Text-to-vector and a selection's vector mask both write
/// SVG path data; this reads it back, changes it, and writes it again. Quadratic curves are held as cubics.
/// </summary>
public sealed class PathNodes
{
    /// <summary>One anchor: where it sits, and the handle approaching and leaving it, if any.</summary>
    public sealed class Node
    {
        public SKPoint Point { get; set; }
        public SKPoint? In { get; set; }
        public SKPoint? Out { get; set; }

        public Node Clone() => new() { Point = Point, In = In, Out = Out };
    }

    /// <summary>One contour of the outline: its nodes and whether it closes back to the first.</summary>
    public sealed class Subpath
    {
        public List<Node> Nodes { get; } = [];
        public bool Closed { get; set; }
    }

    public List<Subpath> Subpaths { get; } = [];

    public int NodeCount => Subpaths.Sum(subpath => subpath.Nodes.Count);

    /// <summary>Reads SVG path data. Null when it uses an arc or is otherwise too much to edit.</summary>
    public static PathNodes? Parse(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg)) return null;
        var tokens = Tokenize(svg);
        if (tokens is null) return null;
        var result = new PathNodes();
        var command = '\0';
        var at = 0;
        var current = new SKPoint();
        var start = new SKPoint();
        SKPoint? lastCubicControl = null;
        SKPoint? lastQuadControl = null;
        Subpath? subpath = null;

        bool More() => at < tokens.Count;
        float Number()
        {
            var text = tokens[at++];
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        while (at < tokens.Count)
        {
            var token = tokens[at];
            if (IsCommand(token))
            {
                command = token[0];
                at++;
                if (command is 'Z' or 'z')
                {
                    if (subpath is not null) subpath.Closed = true;
                    current = start;
                    lastCubicControl = lastQuadControl = null;
                    continue;
                }
            }
            else if (command == '\0')
            {
                return null;
            }

            var relative = char.IsLower(command);
            var origin = relative ? current : new SKPoint();
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    current = new SKPoint(origin.X + Number(), origin.Y + Number());
                    start = current;
                    subpath = new Subpath();
                    result.Subpaths.Add(subpath);
                    subpath.Nodes.Add(new Node { Point = current });
                    command = relative ? 'l' : 'L';
                    lastCubicControl = lastQuadControl = null;
                    break;
                }
                case 'L':
                    current = new SKPoint(origin.X + Number(), origin.Y + Number());
                    subpath?.Nodes.Add(new Node { Point = current });
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'H':
                    current = new SKPoint((relative ? current.X : 0) + Number(), current.Y);
                    subpath?.Nodes.Add(new Node { Point = current });
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'V':
                    current = new SKPoint(current.X, (relative ? current.Y : 0) + Number());
                    subpath?.Nodes.Add(new Node { Point = current });
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'C':
                {
                    var c1 = new SKPoint(origin.X + Number(), origin.Y + Number());
                    origin = relative ? current : new SKPoint();
                    var c2 = new SKPoint(origin.X + Number(), origin.Y + Number());
                    origin = relative ? current : new SKPoint();
                    var end = new SKPoint(origin.X + Number(), origin.Y + Number());
                    AddCubic(subpath, current, c1, c2, end);
                    current = end;
                    lastCubicControl = c2;
                    lastQuadControl = null;
                    break;
                }
                case 'S':
                {
                    origin = relative ? current : new SKPoint();
                    var c2 = new SKPoint(origin.X + Number(), origin.Y + Number());
                    origin = relative ? current : new SKPoint();
                    var end = new SKPoint(origin.X + Number(), origin.Y + Number());
                    var c1 = lastCubicControl is { } previous
                        ? new SKPoint(2 * current.X - previous.X, 2 * current.Y - previous.Y)
                        : current;
                    AddCubic(subpath, current, c1, c2, end);
                    current = end;
                    lastCubicControl = c2;
                    lastQuadControl = null;
                    break;
                }
                case 'Q':
                {
                    var quad = new SKPoint(origin.X + Number(), origin.Y + Number());
                    origin = relative ? current : new SKPoint();
                    var end = new SKPoint(origin.X + Number(), origin.Y + Number());
                    AddQuad(subpath, current, quad, end);
                    current = end;
                    lastQuadControl = quad;
                    lastCubicControl = null;
                    break;
                }
                case 'T':
                {
                    origin = relative ? current : new SKPoint();
                    var end = new SKPoint(origin.X + Number(), origin.Y + Number());
                    var quad = lastQuadControl is { } previous
                        ? new SKPoint(2 * current.X - previous.X, 2 * current.Y - previous.Y)
                        : current;
                    AddQuad(subpath, current, quad, end);
                    current = end;
                    lastQuadControl = quad;
                    lastCubicControl = null;
                    break;
                }
                default:
                    return null;
            }
            if (!More()) break;
        }
        return result.Subpaths.Count > 0 ? result : null;
    }

    private static void AddCubic(Subpath? subpath, SKPoint from, SKPoint c1, SKPoint c2, SKPoint to)
    {
        if (subpath is null) return;
        if (subpath.Nodes.Count > 0) subpath.Nodes[^1].Out = c1;
        subpath.Nodes.Add(new Node { Point = to, In = c2 });
    }

    private static void AddQuad(Subpath? subpath, SKPoint from, SKPoint control, SKPoint to)
    {
        var c1 = new SKPoint(from.X + 2f / 3 * (control.X - from.X), from.Y + 2f / 3 * (control.Y - from.Y));
        var c2 = new SKPoint(to.X + 2f / 3 * (control.X - to.X), to.Y + 2f / 3 * (control.Y - to.Y));
        AddCubic(subpath, from, c1, c2, to);
    }

    /// <summary>Writes the nodes back as SVG path data, drawing curves as cubics.</summary>
    public string ToSvg()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var subpath in Subpaths)
        {
            if (subpath.Nodes.Count == 0) continue;
            Append(builder, "M", subpath.Nodes[0].Point);
            for (var index = 0; index < subpath.Nodes.Count - 1; index++)
            {
                var from = subpath.Nodes[index];
                var to = subpath.Nodes[index + 1];
                Segment(builder, from, to);
            }
            if (subpath.Closed)
            {
                var from = subpath.Nodes[^1];
                var to = subpath.Nodes[0];
                if (from.Out is not null || to.In is not null)
                {
                    var c1 = from.Out ?? from.Point;
                    var c2 = to.In ?? to.Point;
                    AppendCubic(builder, c1, c2, to.Point);
                }
                builder.Append('Z');
            }
        }
        return builder.ToString();
    }

    /// <summary>Moves a node and its handles by a delta, so a node keeps its shape as it is dragged.</summary>
    public bool MoveNode(int subpath, int index, SKPoint delta)
    {
        if (!Locate(subpath, index, out var node)) return false;
        node.Point = new SKPoint(node.Point.X + delta.X, node.Point.Y + delta.Y);
        if (node.In is { } incoming) node.In = new SKPoint(incoming.X + delta.X, incoming.Y + delta.Y);
        if (node.Out is { } outgoing) node.Out = new SKPoint(outgoing.X + delta.X, outgoing.Y + delta.Y);
        return true;
    }

    /// <summary>Moves one handle of a node.</summary>
    public bool MoveHandle(int subpath, int index, bool outgoing, SKPoint point)
    {
        if (!Locate(subpath, index, out var node)) return false;
        if (outgoing) node.Out = point; else node.In = point;
        return true;
    }

    /// <summary>Adds a node in the middle of the segment after the given node.</summary>
    public bool InsertAfter(int subpath, int index)
    {
        if (subpath < 0 || subpath >= Subpaths.Count) return false;
        var nodes = Subpaths[subpath].Nodes;
        if (index < 0 || index >= nodes.Count - 1) return false;
        var from = nodes[index];
        var to = nodes[index + 1];
        var middle = new SKPoint((from.Point.X + to.Point.X) / 2, (from.Point.Y + to.Point.Y) / 2);
        nodes.Insert(index + 1, new Node { Point = middle });
        return true;
    }

    /// <summary>Removes a node, unless the contour would be left too small to be a path.</summary>
    public bool RemoveAt(int subpath, int index)
    {
        if (subpath < 0 || subpath >= Subpaths.Count) return false;
        var contour = Subpaths[subpath];
        if (index < 0 || index >= contour.Nodes.Count || contour.Nodes.Count <= 2) return false;
        contour.Nodes.RemoveAt(index);
        return true;
    }

    /// <summary>All the places the pointer can grab, as (subpath, node, is a handle, is the outgoing handle).</summary>
    public IEnumerable<(int Subpath, int Node, bool Handle, bool Outgoing, SKPoint Point)> Grabs()
    {
        for (var sub = 0; sub < Subpaths.Count; sub++)
        {
            var nodes = Subpaths[sub].Nodes;
            for (var index = 0; index < nodes.Count; index++)
            {
                yield return (sub, index, false, false, nodes[index].Point);
                if (nodes[index].In is { } incoming) yield return (sub, index, true, false, incoming);
                if (nodes[index].Out is { } outgoing) yield return (sub, index, true, true, outgoing);
            }
        }
    }

    private static void Segment(System.Text.StringBuilder builder, Node from, Node to)
    {
        if (from.Out is null && to.In is null) Append(builder, "L", to.Point);
        else AppendCubic(builder, from.Out ?? from.Point, to.In ?? to.Point, to.Point);
    }

    private static void AppendCubic(System.Text.StringBuilder builder, SKPoint c1, SKPoint c2, SKPoint to)
    {
        builder.Append("C");
        AppendPoint(builder, c1);
        builder.Append(' ');
        AppendPoint(builder, c2);
        builder.Append(' ');
        AppendPoint(builder, to);
    }

    private static void Append(System.Text.StringBuilder builder, string command, SKPoint point)
    {
        builder.Append(command);
        AppendPoint(builder, point);
    }

    private static void AppendPoint(System.Text.StringBuilder builder, SKPoint point)
    {
        builder.Append(point.X.ToString("0.####", CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(point.Y.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private bool Locate(int subpath, int index, out Node node)
    {
        node = null!;
        if (subpath < 0 || subpath >= Subpaths.Count) return false;
        var nodes = Subpaths[subpath].Nodes;
        if (index < 0 || index >= nodes.Count) return false;
        node = nodes[index];
        return true;
    }

    private static bool IsCommand(string token) =>
        token.Length == 1 && char.IsLetter(token[0]) && "MmLlHhVvCcSsQqTtZz".Contains(token[0]);

    private static List<string>? Tokenize(string svg)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < svg.Length)
        {
            var character = svg[index];
            if (char.IsWhiteSpace(character) || character == ',') { index++; continue; }
            if (char.IsLetter(character))
            {
                if (!"MmLlHhVvCcSsQqTtZzAa".Contains(character)) return null;
                tokens.Add(character.ToString());
                index++;
                continue;
            }
            var start = index;
            if (character is '+' or '-') index++;
            while (index < svg.Length && (char.IsDigit(svg[index]) || svg[index] == '.')) index++;
            if (index < svg.Length && (svg[index] == 'e' || svg[index] == 'E'))
            {
                index++;
                if (index < svg.Length && (svg[index] == '+' || svg[index] == '-')) index++;
                while (index < svg.Length && char.IsDigit(svg[index])) index++;
            }
            if (index == start) return null;
            tokens.Add(svg[start..index]);
        }
        return tokens;
    }
}
