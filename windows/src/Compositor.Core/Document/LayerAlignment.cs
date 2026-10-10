using Compositor.Core.Model;

namespace Compositor.Core.Document;

public static class LayerAlignment
{
    public static bool Apply(CanvasDocument document, IReadOnlyCollection<Guid> selected, string operation)
    {
        var units = new List<(LayerTransform Box, Dictionary<Guid, LayerTransform> Members)>();
        var seen = new HashSet<Guid>();
        foreach (var id in selected)
        {
            var layer = document.Layers.FirstOrDefault(item => item.ID == id);
            if (layer is null || selected.Any(parent => parent != id && document.Descendants(parent).Contains(id))) continue;
            var members = TransformEdits.GroupMembers(document, [id]);
            if (members.Count == 0 || members.Any(item => seen.Contains(item.ID))) continue;
            if (TransformEdits.GroupBox(document, [id]) is not { } box) continue;
            units.Add((box, TransformEdits.Originals(document, [id])));
            seen.UnionWith(members.Select(item => item.ID));
        }
        if (units.Count == 0) return false;
        var distribute = operation.StartsWith("Distribute", StringComparison.Ordinal);
        if (distribute && units.Count < 3) return false;
        var horizontal = operation == "Distribute Horizontally";
        if (distribute) units = units.OrderBy(unit => horizontal ? unit.Box.CenterX : unit.Box.CenterY).ToList();
        var left = units.Count == 1 ? 0 : units.Min(unit => unit.Box.X);
        var right = units.Count == 1 ? document.Width : units.Max(unit => unit.Box.X + unit.Box.Width);
        var top = units.Count == 1 ? 0 : units.Min(unit => unit.Box.Y);
        var bottom = units.Count == 1 ? document.Height : units.Max(unit => unit.Box.Y + unit.Box.Height);
        for (var i = 0; i < units.Count; i++)
        {
            var (box, members) = units[i];
            var placed = operation switch
            {
                "Align Left" => box with { X = left }, "Align H Center" => box with { X = (left + right - box.Width) / 2 },
                "Align Right" => box with { X = right - box.Width }, "Align Top" => box with { Y = top },
                "Align V Center" => box with { Y = (top + bottom - box.Height) / 2 }, "Align Bottom" => box with { Y = bottom - box.Height },
                "Distribute Horizontally" => box with { X = units[0].Box.CenterX + (units[^1].Box.CenterX - units[0].Box.CenterX) * i / (units.Count - 1) - box.Width / 2 },
                "Distribute Vertically" => box with { Y = units[0].Box.CenterY + (units[^1].Box.CenterY - units[0].Box.CenterY) * i / (units.Count - 1) - box.Height / 2 },
                _ => box,
            };
            TransformEdits.Carry(document, members, box, placed);
        }
        return true;
    }
}
