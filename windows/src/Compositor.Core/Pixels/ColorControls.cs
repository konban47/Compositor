using System.Runtime.InteropServices;
using SkiaSharp;

namespace Compositor.Core.Pixels;

public static class ColorControls
{
    public static void Desaturate(SKBitmap bitmap, double amount)
    {
        var data = bitmap.GetPixelSpan(); amount = Math.Clamp(amount, 0, 1);
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
        { var i = y * bitmap.RowBytes + x * 4; var gray = data[i] * .2126 + data[i + 1] * .7152 + data[i + 2] * .0722; for (var c = 0; c < 3; c++) data[i + c] = (byte)Math.Round(data[i + c] * (1 - amount) + gray * amount); }
    }
    /// <summary>Normal source-over in the requested power-law blending space. Inputs are premultiplied;
    /// the transfer affects RGB only and preserves source-over alpha. Gamma 2.2 matches encoded blending.</summary>
    public static void Composite(SKBitmap source, SKBitmap destination, int left, int top, double opacity, double gamma)
    {
        var power = 2.2 / gamma; var src = source.GetPixelSpan(); var dst = destination.GetPixelSpan();
        var deep = source.ColorType == SKColorType.RgbaF32 && destination.ColorType == SKColorType.RgbaF32;
        for (var y = Math.Max(0, -top); y < Math.Min(source.Height, destination.Height - top); y++)
        for (var x = Math.Max(0, -left); x < Math.Min(source.Width, destination.Width - left); x++)
        {
            var s = y * source.RowBytes + x * (deep ? 16 : 4); var d = (top + y) * destination.RowBytes + (left + x) * (deep ? 16 : 4);
            var a = Read(src, s, 3, deep); var b = Read(dst, d, 3, deep); var coverage = a * opacity; var alpha = coverage + b * (1 - coverage);
            for (var c = 0; c < 3; c++)
            {
                var foreground = a > 0 ? Read(src, s, c, deep) / a : 0; var background = b > 0 ? Read(dst, d, c, deep) / b : 0;
                var mixed = alpha > 0 ? (Power(foreground, power) * coverage + Power(background, power) * b * (1 - coverage)) / alpha : 0;
                Write(dst, d, c, deep, Power(mixed, 1 / power) * alpha);
            }
            Write(dst, d, 3, deep, alpha);
        }
    }
    private static double Power(double x, double exponent) => Math.Sign(x) * Math.Pow(Math.Abs(x), exponent);
    private static double Read(Span<byte> pixels, int index, int channel, bool deep) => deep ? MemoryMarshal.Cast<byte, float>(pixels.Slice(index, 16))[channel] : pixels[index + channel] / 255d;
    private static void Write(Span<byte> pixels, int index, int channel, bool deep, double value) { if (deep) MemoryMarshal.Cast<byte, float>(pixels.Slice(index, 16))[channel] = (float)value; else pixels[index + channel] = (byte)Math.Clamp(Math.Round(value * 255), 0, 255); }
}
