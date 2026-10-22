using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Rendering;

/// <summary>Conservative damage for an immutable metadata revision. Null means rebuild everything;
/// empty means only non-rendering document state changed.</summary>
public static class ViewportDamage
{
    public static SKRectI? Between(CanvasDocument before, CanvasDocument after)
    {
        if (before.ID != after.ID || before.Width != after.Width || before.Height != after.Height
            || before.Image != after.Image || before.Layers.Count != after.Layers.Count) return null;
        var changed = new List<(ImageLayer Before, ImageLayer After)>();
        for (var i = 0; i < before.Layers.Count; i++)
        {
            var a = before.Layers[i]; var b = after.Layers[i];
            if (a.ID != b.ID) return null;
            if (!a.SameAs(b)) changed.Add((a, b));
        }
        if (changed.Count == 0) return SKRectI.Empty;
        // Groups, effects, clipping and live masks may propagate outside a layer's bounds.
        if (before.Layers.Concat(after.Layers).Any(l => l.IsGroup || l.Effects is not null
            || l.MaskSourceID is not null || l.Blending is { IsDefault: false })) return null;
        SKRectI dirty = SKRectI.Empty;
        foreach (var (a, b) in changed)
        {
            if (a.Adjustment is not null || b.Adjustment is not null || a.ParentID != b.ParentID) return null;
            foreach (var layer in new[] { a, b })
            {
                if (!layer.IsVisible || layer.Asset is null) continue;
                var t = layer.Transform; var radians = t.Rotation * Math.PI / 180;
                var w = Math.Abs(t.Width * Math.Cos(radians)) + Math.Abs(t.Height * Math.Sin(radians));
                var h = Math.Abs(t.Width * Math.Sin(radians)) + Math.Abs(t.Height * Math.Cos(radians));
                var area = new SKRectI((int)Math.Floor(t.X + t.Width / 2 - w / 2) - 2,
                    (int)Math.Floor(t.Y + t.Height / 2 - h / 2) - 2,
                    (int)Math.Ceiling(t.X + t.Width / 2 + w / 2) + 2,
                    (int)Math.Ceiling(t.Y + t.Height / 2 + h / 2) + 2);
                dirty = dirty.IsEmpty ? area : SKRectI.Union(dirty, area);
            }
        }
        if (!dirty.IsEmpty)
        {
            var halo = Math.Max(DocumentRenderer.Halo(before), DocumentRenderer.Halo(after));
            dirty.Inflate(halo, halo);
        }
        return dirty;
    }
}
