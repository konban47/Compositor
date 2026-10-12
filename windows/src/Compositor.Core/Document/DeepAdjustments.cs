using System.Runtime.InteropServices;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>High precision paths for the upstream v1.4.10 color/blur changes.</summary>
public static class DeepAdjustments
{
    public static bool Supports(LayerAdjustment s) => s.Kind is AdjustmentKind.Levels or AdjustmentKind.Curves or AdjustmentKind.Exposure
        or AdjustmentKind.Invert or AdjustmentKind.HueSaturation or AdjustmentKind.BlackWhite or AdjustmentKind.ColorBalance or AdjustmentKind.GradientMap;
    public static bool Apply(CanvasDocument document, ImageLayer layer, LayerAdjustment settings)
    {
        if (layer.Asset is not { } asset || !Supports(settings)) return false;
        using var work = DeepPixels.From(asset.WorkingImage); Adjust(work, settings);
        return ImageAdjustments.SetPixels(document, layer, work);
    }
    public static void Adjust(SKBitmap image, LayerAdjustment settings)
    {
        var data = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan());
        var independent = settings.Kind is AdjustmentKind.Levels or AdjustmentKind.Curves or AdjustmentKind.Exposure or AdjustmentKind.Invert;
        if (independent)
        {
            var ramp = new byte[256 * 4]; for (var i = 0; i < 256; i++) { ramp[i * 4] = ramp[i * 4 + 1] = ramp[i * 4 + 2] = (byte)i; ramp[i * 4 + 3] = 255; }
            AdjustmentOperators.Apply(settings, 1, ramp, 256, 1, 1024);
            for (var i = 0; i < data.Length; i += 4) for (var c = 0; c < 3; c++)
            {
                var v = data[i + c];
                if (settings.Kind == AdjustmentKind.Invert) { data[i + c] = 1 - v; continue; }
                if (settings.Kind == AdjustmentKind.Levels)
                {
                    double Level(double input, LevelsChannelRange range) => (range.OutputBlack + (range.OutputWhite - range.OutputBlack) * Math.Pow(Math.Clamp((input * 255 - range.Black) / (range.White - range.Black), 0, 1), 1 / range.Gamma)) / 255;
                    data[i + c] = (float)Level(Level(v, settings.Levels.Ranges[0]), settings.Levels.Ranges[c + 1]); continue;
                }
                if (settings.Kind == AdjustmentKind.Exposure)
                {
                    var e = settings.Exposure; var linear = v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
                    linear = Math.Pow(Math.Max(0, linear * Math.Pow(2, e.Exposure) + e.Offset), 1 / e.Gamma);
                    data[i + c] = (float)(linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055); continue;
                }
                // Upstream deep_merge reads the local slope across 1 or 4 eight-bit steps.
                var scaled = Math.Clamp(v, 0, 1) * 255; var down = (int)scaled; var past = scaled - down;
                var near = Math.Min(255, down + 1); var far = Math.Min(255, down + 4); var lo = ramp[down * 4 + c]; var change = ramp[near * 4 + c] - lo;
                var slope = Math.Abs(change) >= 2 || far == near ? change : far > down ? (ramp[far * 4 + c] - lo) / (float)(far - down) : 0;
                data[i + c] = (lo + slope * past) / 255;
            }
            return;
        }
        const int n = 65; var samples = new byte[n * n * n * 4];
        for (var b = 0; b < n; b++) for (var g = 0; g < n; g++) for (var r = 0; r < n; r++)
        { var at = ((b * n + g) * n + r) * 4; samples[at] = (byte)Math.Round(r * 255.0 / (n - 1)); samples[at + 1] = (byte)Math.Round(g * 255.0 / (n - 1)); samples[at + 2] = (byte)Math.Round(b * 255.0 / (n - 1)); samples[at + 3] = 255; }
        AdjustmentOperators.Apply(settings, 1, samples, n * n, n, n * n * 4);
        var values = new Color3[n * n * n]; for (var i = 0; i < values.Length; i++) values[i] = new(samples[i * 4] / 255.0, samples[i * 4 + 1] / 255.0, samples[i * 4 + 2] / 255.0);
        var cube = new ColorCube { Size = n, Values = values };
        for (var i = 0; i < data.Length; i += 4) { var c = cube.Apply(new(data[i], data[i + 1], data[i + 2])); data[i] = (float)c.R; data[i + 1] = (float)c.G; data[i + 2] = (float)c.B; }
    }
    public static SKBitmap Blur(SKBitmap source, double radius, bool preserveAlpha)
    {
        using var premul = new SKBitmap(DeepPixels.Info(source.Width, source.Height).WithAlphaType(SKAlphaType.Premul));
        using (var canvas = new SKCanvas(premul))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur((float)radius, (float)radius, SKShaderTileMode.Clamp), BlendMode = SKBlendMode.Src }) canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        var result = DeepPixels.From(premul);
        if (preserveAlpha)
        {
            using var original = DeepPixels.From(source); var before = MemoryMarshal.Cast<byte, float>(original.GetPixelSpan()); var after = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
            for (var i = 3; i < after.Length; i += 4) after[i] = before[i];
        }
        return result;
    }
}
