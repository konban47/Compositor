using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Disconnected foreground components become separate editable masked groups above the source.</summary>
public static class ObjectMaskGroups
{
    public static IReadOnlyList<Guid> Create(CanvasDocument document, Guid layerID, SKBitmap matte)
    {
        var layer = document.Layers.FirstOrDefault(l => l.ID == layerID);
        if (layer is null || LayerProtection.Effective(document, layerID) != LayerLocks.None) return [];
        var scale = Math.Min(1, 1024.0 / Math.Max(matte.Width, matte.Height));
        using var small = Bitmaps.Scale(matte, Math.Max(1, (int)(matte.Width * scale)), Math.Max(1, (int)(matte.Height * scale)));
        var width = small.Width; var height = small.Height; var bytes = small.GetPixelSpan().ToArray();
        var labels = new int[checked(width * height)]; var next = 0;
        var components = new List<List<int>>();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var start = y * width + x; if (labels[start] != 0 || bytes[y * small.RowBytes + x] < 128) continue;
            next++; var queue = new Queue<int>(); var pixels = new List<int>(); queue.Enqueue(start); labels[start] = next;
            while (queue.TryDequeue(out var at))
            {
                pixels.Add(at); var cx = at % width; var cy = at / width;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = cx + dx; var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height || labels[ny * width + nx] != 0 || bytes[ny * small.RowBytes + nx] < 128) continue;
                    labels[ny * width + nx] = next; queue.Enqueue(ny * width + nx);
                }
            }
            if (pixels.Count >= Math.Max(4, width * height / 10000)) components.Add(pixels);
        }
        var result = new List<Guid>(); var insertion = document.Layers.IndexOf(layer) + 1;
        foreach (var component in components.OrderByDescending(c => c.Count).Take(32))
        {
            var left = component.Min(p => p % width); var top = component.Min(p => p / width);
            var right = component.Max(p => p % width) + 1; var bottom = component.Max(p => p / width) + 1;
            var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(right - left, bottom - top)); mask.Erase(SKColors.Black);
            var target = mask.GetPixelSpan(); foreach (var p in component) target[(p / width - top) * mask.RowBytes + p % width - left] = bytes[p / width * small.RowBytes + p % width];
            var u = (left + right) / (2.0 * width); var v = (top + bottom) / (2.0 * height);
            var center = layer.Transform.Point(layer.Transform.FlipX ? 1 - u : u, layer.Transform.FlipY ? 1 - v : v);
            var w = layer.Transform.Width * (right - left) / width; var h = layer.Transform.Height * (bottom - top) / height;
            var placement = new LayerTransform(center.X - w / 2, center.Y - h / 2, w, h, layer.Transform.Rotation, layer.Transform.FlipX, layer.Transform.FlipY);
            var group = new ImageLayer(Guid.NewGuid(), null, placement, "Object " + (result.Count + 1)) { IsGroup = true, ParentID = layer.ParentID, Mask = LayerMask.AssetFrom(mask) };
            document.Layers.Insert(insertion++, group); result.Add(group.ID);
        }
        return result;
    }
}
