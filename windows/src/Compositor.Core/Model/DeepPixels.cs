using System.Runtime.InteropServices;
using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>Unassociated sRGB float working pixels, with a dithered display proxy. Assets are immutable.</summary>
public static class DeepPixels
{
    public static SKImageInfo Info(int width, int height) => new(width, height, SKColorType.RgbaF32,
        SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());

    public static SKBitmap From(SKBitmap source)
    {
        var result = Bitmaps.Allocate(Info(source.Width, source.Height));
        using var image = SKImage.FromBitmap(source);
        if (!image.ReadPixels(result.Info, result.GetPixels(), result.RowBytes, 0, 0))
        { result.Dispose(); throw new InvalidOperationException("Could not read high precision pixels."); }
        return result;
    }

    public static SKBitmap Preview(SKBitmap deep)
    {
        var result = Bitmaps.Allocate(Bitmaps.ColorInfo(deep.Width, deep.Height));
        var src = MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan()); var dst = result.GetPixelSpan();
        for (var y = 0; y < deep.Height; y++) for (var x = 0; x < deep.Width; x++)
        {
            var from = y * deep.RowBytes / 4 + x * 4; var to = y * result.RowBytes + x * 4;
            uint hash = unchecked((uint)(x + y * deep.Width) * 1664525u + 1013904223u); hash ^= hash >> 13; hash *= 1274126177;
            var noise = (hash & 65535) / 65535.0 - .5;
            for (var c = 0; c < 4; c++) dst[to + c] = (byte)Math.Clamp(Math.Round(src[from + c] * 255 + (c == 3 ? 0 : noise)), 0, 255);
        }
        return result;
    }

    public static void Quantize(SKBitmap deep, int bits)
    {
        var max = bits == 16 ? 65535f : 255f;
        var p = MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan());
        for (var i = 0; i < p.Length; i++)
        {
            var value = float.IsFinite(p[i]) ? p[i] : 0;
            p[i] = bits == 32 ? Math.Clamp(value, i % 4 == 3 ? 0 : -65536, i % 4 == 3 ? 1 : 65536)
                : MathF.Round(Math.Clamp(value, 0, 1) * max) / max;
        }
    }

    public static ImportedImage Copy(ImportedImage image, string? name = null) => image.Deep is { } deep
        ? ImportedImage.CreateDeep(deep.Copy(), name ?? image.Name, image.BitDepth)
        : ImportedImage.Create(image.Image.Copy(), name ?? image.Name);

    /// <summary>Keep untouched deep samples when an 8-bit brush paints a local patch, as upstream does.</summary>
    public static ImportedImage Painted(ImportedImage before, ImportedImage after)
    {
        if (before.Deep is null || after.Deep is not null || before.Width != after.Width || before.Height != after.Height) return after;
        var deep = Bitmaps.Allocate(before.Deep.Info);
        try
        {
            if (!before.Deep.CopyTo(deep)) throw new InvalidOperationException("Could not copy high precision pixels.");
            var p = MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan());
            if (before.Image.ColorType == SKColorType.Rgba8888 && after.Image.ColorType == SKColorType.Rgba8888
                && before.Image.AlphaType == SKAlphaType.Unpremul && after.Image.AlphaType == SKAlphaType.Unpremul)
            {
                var a = before.Image.GetPixelSpan(); var b = after.Image.GetPixelSpan();
                for (var y = 0; y < before.Height; y++) for (var x = 0; x < before.Width * 4; x++)
                {
                    var oldValue = a[y * before.Image.RowBytes + x]; var nextValue = b[y * after.Image.RowBytes + x];
                    if (oldValue != nextValue) p[y * deep.RowBytes / 4 + x] = nextValue / 255f;
                }
            }
            else
            {
                // Nonstandard proxies retain Skia's color/alpha conversion, with only two rows of scratch.
                using var old = SKImage.FromBitmap(before.Image); using var next = SKImage.FromBitmap(after.Image);
                using var a = Bitmaps.Allocate(Info(before.Width, 1)); using var b = Bitmaps.Allocate(a.Info);
                for (var y = 0; y < before.Height; y++)
                {
                    if (!old.ReadPixels(a.Info, a.GetPixels(), a.RowBytes, 0, y)
                        || !next.ReadPixels(b.Info, b.GetPixels(), b.RowBytes, 0, y)) throw new InvalidOperationException("Could not read brush pixels.");
                    var left = MemoryMarshal.Cast<byte, float>(a.GetPixelSpan()); var right = MemoryMarshal.Cast<byte, float>(b.GetPixelSpan());
                    for (var x = 0; x < left.Length; x++) if (left[x] != right[x]) p[y * deep.RowBytes / 4 + x] = right[x];
                }
            }
            return ImportedImage.CreateDeep(deep, after.Name, before.BitDepth);
        }
        catch { deep.Dispose(); throw; }
    }
}
