using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class ProgressiveBlur
{
    /// <summary>A Gaussian-radius ramp in layer coordinates, evaluated at float precision.</summary>
    public static SKBitmap Apply(SKBitmap source, FilterSettings settings)
    {
        if (!settings.IsValid(FilterKind.ProgressiveBlur)) throw new ArgumentOutOfRangeException(nameof(settings));
        var from = settings.ProgressiveInverted ? settings.ProgressiveTo : settings.ProgressiveFrom;
        var to = settings.ProgressiveInverted ? settings.ProgressiveFrom : settings.ProgressiveTo;
        var min = Math.Min(from, to); var max = Math.Max(from, to); const int levels = 12;
        var x0 = settings.ProgressiveStartX / 100 * source.Width; var y0 = settings.ProgressiveStartY / 100 * source.Height;
        var dx = (settings.ProgressiveEndX - settings.ProgressiveStartX) / 100 * source.Width;
        var dy = (settings.ProgressiveEndY - settings.ProgressiveStartY) / 100 * source.Height;
        var distance = Math.Max(.0001, dx * dx + dy * dy);
        var output = new SKBitmap(DeepPixels.Info(source.Width, source.Height)); output.Erase(SKColors.Transparent);
        var result = MemoryMarshal.Cast<byte, float>(output.GetPixelSpan());
        for (var level = 0; level <= levels; level++)
        {
            var radius = min + (max - min) * level / levels;
            using var blurred = radius < .001 ? DeepPixels.From(source) : DeepAdjustments.Blur(source, radius, false);
            var pixels = MemoryMarshal.Cast<byte, float>(blurred.GetPixelSpan());
            for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
            {
                var t = settings.ProgressiveRadial ? Math.Sqrt(((x - x0) * (x - x0) + (y - y0) * (y - y0)) / distance) : ((x - x0) * dx + (y - y0) * dy) / distance;
                var wanted = from + (to - from) * Math.Clamp(t, 0, 1);
                var position = max == min ? 0 : (wanted - min) / (max - min) * levels;
                var weight = (float)Math.Max(0, 1 - Math.Abs(position - level)); if (weight == 0) continue;
                var at = y * output.RowBytes / 4 + x * 4; var index = y * blurred.RowBytes / 4 + x * 4;
                var alpha = pixels[index + 3]; result[at + 3] += alpha * weight;
                for (var c = 0; c < 3; c++) result[at + c] += pixels[index + c] * alpha * weight;
            }
        }
        for (var i = 0; i < result.Length; i += 4) if (result[i + 3] > 0) for (var c = 0; c < 3; c++) result[i + c] /= result[i + 3];
        return output;
    }
}
