using System.Runtime.InteropServices;
using Compositor.Core.Model;

namespace Compositor.Core.Document;

public static class ImageTrapping
{
    public static bool Apply(CanvasDocument document, Guid id, int radius)
    {
        if (radius is < 1 or > 10 || document.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset } layer || !LayerProtection.CanPaint(document, id)) return false;
        using var original = DeepPixels.From(asset.WorkingImage); using var result = original.Copy();
        var a = MemoryMarshal.Cast<byte, float>(original.GetPixelSpan()); var b = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        for (var y = 0; y < result.Height; y++) for (var x = 0; x < result.Width; x++)
        {
            var target = y * result.RowBytes / 4 + x * 4; var best = target; var darkness = a[target] * .2126f + a[target + 1] * .7152f + a[target + 2] * .0722f;
            for (var yy = Math.Max(0, y - radius); yy <= Math.Min(result.Height - 1, y + radius); yy++)
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(result.Width - 1, x + radius); xx++)
            {
                if ((xx - x) * (xx - x) + (yy - y) * (yy - y) > radius * radius) continue;
                var index = yy * original.RowBytes / 4 + xx * 4; var value = a[index] * .2126f + a[index + 1] * .7152f + a[index + 2] * .0722f;
                if (a[index + 3] > 0 && value < darkness) { best = index; darkness = value; }
            }
            if (a[target + 3] > 0) for (var c = 0; c < 3; c++) b[target + c] = a[best + c];
        }
        return ImageAdjustments.SetPixels(document, layer, result);
    }
}
