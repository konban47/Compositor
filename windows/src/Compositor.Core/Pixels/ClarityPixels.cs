using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Pixels;
public static class ClarityPixels
{
    public static void Apply(SKBitmap image, ImageAdjustmentSettings settings)
    {
        using var blurred = DeepAdjustments.Blur(image, 8, true); var p = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan()); var soft = MemoryMarshal.Cast<byte, float>(blurred.GetPixelSpan());
        var amount = settings.Get("Clarity", 25) / 100;
        for (var i = 0; i < p.Length; i += 4)
        {
            var lum = .2126 * p[i] + .7152 * p[i + 1] + .0722 * p[i + 2]; var average = .2126 * soft[i] + .7152 * soft[i + 1] + .0722 * soft[i + 2];
            var local = (lum - average) * amount * 2 * 4 * Math.Clamp(lum, 0, 1) * (1 - Math.Clamp(lum, 0, 1));
            var color = ImageAdjustments.Adjust(new(p[i] + local, p[i + 1] + local, p[i + 2] + local), settings);
            p[i] = (float)color.R; p[i + 1] = (float)color.G; p[i + 2] = (float)color.B;
        }
    }
    public static void Apply(Span<byte> pixels, int width, int height, int stride, ImageAdjustmentSettings settings)
    {
        using var bitmap = Bitmaps.Allocate(DeepPixels.Info(width, height)); var p = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan());
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var i = y * stride + x * 4; var j = (y * width + x) * 4; var a = pixels[i + 3]; for (var c = 0; c < 3; c++) p[j + c] = a == 0 ? 0 : pixels[i + c] / (float)a; p[j + 3] = a / 255f; }
        Apply(bitmap, settings);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var i = y * stride + x * 4; var j = (y * width + x) * 4; for (var c = 0; c < 3; c++) pixels[i + c] = (byte)Math.Clamp(Math.Round(p[j + c] * pixels[i + 3]), 0, pixels[i + 3]); }
    }
}
