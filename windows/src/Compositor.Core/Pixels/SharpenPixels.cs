using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Pixels;

/// <summary>Unsharp mask in straight RGB, preserving original coverage at transparent edges.</summary>
public static class SharpenPixels
{
    public static void Apply(SKBitmap image, double amount, double radius)
    {
        if (!double.IsFinite(amount) || amount is < 0 or > 500 || !double.IsFinite(radius) || radius is < .1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return;
        using var blurred = image.Copy();
        GaussianBlur.Clamped(blurred.GetPixelSpan(), image.Width, image.Height, 4, blurred.RowBytes, radius);
        var pixels = image.GetPixelSpan(); var low = blurred.GetPixelSpan();
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var p = y * image.RowBytes + x * 4; var q = y * blurred.RowBytes + x * 4;
            var alpha = pixels[p + 3]; var softAlpha = low[q + 3];
            if (alpha == 0 || softAlpha == 0) continue;
            for (var c = 0; c < 3; c++)
            {
                var source = pixels[p + c] / (double)alpha;
                var soft = low[q + c] / (double)softAlpha;
                pixels[p + c] = (byte)Math.Round(Math.Clamp(source + (source - soft) * amount / 100, 0, 1) * alpha);
            }
        }
    }
}
