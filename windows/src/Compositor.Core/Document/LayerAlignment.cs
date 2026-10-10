using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum AlignmentTarget { Auto, SelectedLayers, Canvas, Selection }

public static class LayerAlignment
{
    public static bool Apply(CanvasDocument document, IReadOnlyCollection<Guid> selected, string operation,
        AlignmentTarget target = AlignmentTarget.Auto)
    {
        var units = new List<(LayerTransform Box, SKRect Bounds, Dictionary<Guid, LayerTransform> Members)>();
        foreach (var id in selected)
        {
            var layer = document.Layers.FirstOrDefault(item => item.ID == id);
            if (layer is null || selected.Any(parent => parent != id && document.Descendants(parent).Contains(id))) continue;
            var members = TransformEdits.GroupMembers(document, [id]);
            IEnumerable<Guid> all = layer.IsGroup ? document.Descendants(id).Append(id) : [id];
            if (all.Any(child => !LayerProtection.CanMove(document, child))) continue;
            if (members.Count == 0 || TransformEdits.GroupBox(document, [id]) is not { } box) continue;
            var points = members.SelectMany(item => new[] { item.Transform.Point(0, 0), item.Transform.Point(1, 0), item.Transform.Point(0, 1), item.Transform.Point(1, 1) }).ToArray();
            var bounds = new SKRect(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
            units.Add((box, bounds, TransformEdits.Originals(document, [id])));
        }
        if (units.Count == 0) return false;
        if (target == AlignmentTarget.Auto) target = units.Count == 1 ? AlignmentTarget.Canvas : AlignmentTarget.SelectedLayers;
        var extent = new SKRect(units.Min(u => u.Bounds.Left), units.Min(u => u.Bounds.Top), units.Max(u => u.Bounds.Right), units.Max(u => u.Bounds.Bottom));
        if (target == AlignmentTarget.Canvas) extent = SKRect.Create(document.Width, document.Height);
        if (target == AlignmentTarget.Selection)
        {
            if (document.Selection.Path is not { IsEmpty: false } path) return false;
            extent = path.Bounds;
        }
        var distribute = operation.StartsWith("Distribute", StringComparison.Ordinal);
        var spacing = operation.EndsWith("Spacing", StringComparison.Ordinal);
        if (distribute && units.Count < (target == AlignmentTarget.SelectedLayers ? 3 : 2)) return false;
        var horizontal = operation is "Distribute Horizontally" or "Distribute Left" or "Distribute Right" or "Distribute Horizontal Spacing";
        double Anchor(SKRect b) => operation switch
        {
            "Distribute Left" => b.Left, "Distribute Right" => b.Right,
            "Distribute Top" => b.Top, "Distribute Bottom" => b.Bottom,
            _ => horizontal ? b.MidX : b.MidY,
        };
        if (distribute) units = units.OrderBy(u => spacing ? horizontal ? u.Bounds.Left : u.Bounds.Top : Anchor(u.Bounds)).ToList();
        var first = distribute ? Anchor(units[0].Bounds) : 0;
        var last = distribute ? Anchor(units[^1].Bounds) : 0;
        if (distribute && target != AlignmentTarget.SelectedLayers)
        {
            var a = units[0].Bounds; var b = units[^1].Bounds;
            first = horizontal ? extent.Left + Anchor(a) - a.Left : extent.Top + Anchor(a) - a.Top;
            last = horizontal ? extent.Right - (b.Right - Anchor(b)) : extent.Bottom - (b.Bottom - Anchor(b));
        }
        double cursor = horizontal ? extent.Left : extent.Top;
        var gap = spacing ? ((horizontal ? extent.Width : extent.Height) - units.Sum(u => horizontal ? u.Bounds.Width : u.Bounds.Height)) / (units.Count - 1) : 0;
        var changed = false;
        for (var i = 0; i < units.Count; i++)
        {
            var (box, bounds, members) = units[i];
            double dx = 0, dy = 0;
            if (distribute)
            {
                var delta = spacing ? cursor - (horizontal ? bounds.Left : bounds.Top) : first + (last - first) * i / (units.Count - 1) - Anchor(bounds);
                if (horizontal) dx = delta; else dy = delta;
                cursor += (horizontal ? bounds.Width : bounds.Height) + gap;
            }
            else switch (operation)
            {
                case "Align Left": dx = extent.Left - bounds.Left; break;
                case "Align H Center": dx = extent.MidX - bounds.MidX; break;
                case "Align Right": dx = extent.Right - bounds.Right; break;
                case "Align Top": dy = extent.Top - bounds.Top; break;
                case "Align V Center": dy = extent.MidY - bounds.MidY; break;
                case "Align Bottom": dy = extent.Bottom - bounds.Bottom; break;
                default: return false;
            }
            if (Math.Abs(dx) + Math.Abs(dy) < .00001) continue;
            changed |= TransformEdits.Carry(document, members, box, box with { X = box.X + dx, Y = box.Y + dy });
        }
        return changed;
    }
}
