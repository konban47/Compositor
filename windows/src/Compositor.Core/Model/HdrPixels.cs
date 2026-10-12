using System.Runtime.InteropServices;
using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>Upstream deep_hdr_normalize/restore, operating on straight float pixels instead of half floats.</summary>
public static class HdrPixels
{
    public static float[]? Normalize(SKBitmap image)
    {
        var p = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan());
        var wide = false;
        for (var i = 0; i < p.Length; i += 4)
            if (p[i + 3] > 0 && Math.Max(p[i], Math.Max(p[i + 1], p[i + 2])) > 1) { wide = true; break; }
        if (!wide) return null;
        var scales = new float[p.Length / 4];
        for (var i = 0; i < p.Length; i += 4)
        {
            var peak = Math.Max(1, Math.Max(p[i], Math.Max(p[i + 1], p[i + 2])));
            var scale = float.IsFinite(peak) ? peak : 1; scales[i / 4] = scale;
            for (var c = 0; c < 3; c++) p[i + c] = float.IsFinite(p[i + c]) ? Math.Max(0, p[i + c]) / scale : 0;
        }
        return scales;
    }
    public static void Restore(SKBitmap image, float[]? scales)
    {
        if (scales is null) return;
        var p = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan());
        for (var i = 0; i < p.Length; i += 4) for (var c = 0; c < 3; c++) p[i + c] *= scales[i / 4];
    }
}
