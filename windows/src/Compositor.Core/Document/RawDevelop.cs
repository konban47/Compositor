using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public sealed record RawDevelopSettings(double Exposure = 0, double Temperature = 0, double Tint = 0,
    double HighlightRecovery = 0, double NoiseReduction = 0, double LensCorrection = 0);
public static class RawDevelop
{
    public static ImportedImage Apply(ImportedImage asset, RawDevelopSettings settings)
    {
        if (new[] { settings.Exposure, settings.Temperature, settings.Tint, settings.HighlightRecovery, settings.NoiseReduction, settings.LensCorrection }.Any(v => !double.IsFinite(v))
            || Math.Abs(settings.Exposure) > 5 || Math.Abs(settings.Temperature) > 100 || Math.Abs(settings.Tint) > 100
            || settings.HighlightRecovery is < 0 or > 100 || settings.NoiseReduction is < 0 or > 100 || Math.Abs(settings.LensCorrection) > 100) throw new ArgumentException("Invalid RAW settings.");
        using var work = DeepPixels.From(asset.WorkingImage); var p = MemoryMarshal.Cast<byte, float>(work.GetPixelSpan());
        var exposure = Math.Pow(2, settings.Exposure); var warm = Math.Pow(2, settings.Temperature / 300); var tint = Math.Pow(2, settings.Tint / 300);
        for (var i = 0; i < p.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                double value = p[i + c]; value = value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                value *= exposure * (c == 0 ? warm * tint : c == 1 ? 1 / tint : tint / warm);
                var recovery = settings.HighlightRecovery / 100; if (value > .5) value = .5 + (value - .5) / (1 + (value - .5) * recovery * 3);
                p[i + c] = (float)(value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055);
            }
        }
        if (settings.NoiseReduction > 0)
        {
            using var blur = DeepAdjustments.Blur(work, 1.2, true); var mean = MemoryMarshal.Cast<byte, float>(blur.GetPixelSpan());
            for (var i = 0; i < p.Length; i += 4)
            {
                var difference = Math.Max(Math.Abs(p[i] - mean[i]), Math.Max(Math.Abs(p[i + 1] - mean[i + 1]), Math.Abs(p[i + 2] - mean[i + 2])));
                var amount = settings.NoiseReduction / 100 * Math.Exp(-difference * difference / .008);
                for (var c = 0; c < 3; c++) p[i + c] += (float)((mean[i + c] - p[i + c]) * amount);
            }
        }
        if (settings.LensCorrection != 0)
        {
            var source = p.ToArray(); var radius = Math.Max(1, Math.Sqrt(work.Width * (double)work.Width + work.Height * (double)work.Height) / 2);
            for (var y = 0; y < work.Height; y++) for (var x = 0; x < work.Width; x++)
            {
                var dx = x - (work.Width - 1) / 2.0; var dy = y - (work.Height - 1) / 2.0; var f = 1 + settings.LensCorrection / 100 * .35 * (dx * dx + dy * dy) / (radius * radius);
                var sx = Math.Clamp(dx * f + (work.Width - 1) / 2.0, 0, work.Width - 1); var sy = Math.Clamp(dy * f + (work.Height - 1) / 2.0, 0, work.Height - 1);
                var x0 = (int)sx; var y0 = (int)sy; var x1 = Math.Min(x0 + 1, work.Width - 1); var y1 = Math.Min(y0 + 1, work.Height - 1); var fx = sx - x0; var fy = sy - y0;
                for (var c = 0; c < 4; c++) p[(y * work.Width + x) * 4 + c] = (float)((source[(y0 * work.Width + x0) * 4 + c] * (1 - fx) + source[(y0 * work.Width + x1) * 4 + c] * fx) * (1 - fy) + (source[(y1 * work.Width + x0) * 4 + c] * (1 - fx) + source[(y1 * work.Width + x1) * 4 + c] * fx) * fy);
            }
        }
        return asset.BitDepth > 8 ? ImportedImage.CreateDeep(work.Copy(), asset.Name, asset.BitDepth) : ImportedImage.Create(DeepPixels.Preview(work), asset.Name);
    }
}
