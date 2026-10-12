using System.Globalization;
using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum ImageAdjustmentKind
{
    BrightnessContrast, Vibrance, PhotoFilter, ChannelMixer, ColorLookup, Posterize, Threshold,
    SelectiveColor, ShadowsHighlights, HdrToning, Desaturate, MatchColor, ReplaceColor, Equalize,
}

public sealed class ImageAdjustmentSettings
{
    public ImageAdjustmentKind Kind { get; set; }
    public Dictionary<string, double> Values { get; set; } = [];
    public uint Color { get; set; } = 0xFFEC8A00;
    public uint Replacement { get; set; } = 0xFF3399FF;
    public bool PreserveLuminosity { get; set; } = true;
    public bool Monochrome { get; set; }
    public bool Absolute { get; set; }
    public int Range { get; set; }
    public ColorCube? Cube { get; set; }
    public double[]? ReferenceMean { get; set; }
    public double[]? ReferenceDeviation { get; set; }
    public double Get(string key, double fallback = 0) => Values.GetValueOrDefault(key, fallback);
    public ImageAdjustmentSettings Copy() => new() { Kind = Kind, Values = new(Values), Color = Color,
        Replacement = Replacement, PreserveLuminosity = PreserveLuminosity, Monochrome = Monochrome,
        Absolute = Absolute, Range = Range, Cube = Cube, ReferenceMean = ReferenceMean, ReferenceDeviation = ReferenceDeviation };
    public bool IsValid => Enum.IsDefined(Kind) && Values.Count <= 128
        && Values.Values.All(v => double.IsFinite(v) && Math.Abs(v) <= 10000)
        && Range is >= 0 and <= 8 && (ReferenceMean is null || ReferenceMean.Length == 3 && ReferenceMean.All(double.IsFinite))
        && (ReferenceDeviation is null || ReferenceDeviation.Length == 3 && ReferenceDeviation.All(v => double.IsFinite(v) && v >= 0));
}

public readonly record struct Color3(double R, double G, double B)
{
    public double Luma => .2126 * R + .7152 * G + .0722 * B;
    public Color3 Clamp() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1));
    public static Color3 operator +(Color3 a, Color3 b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Color3 operator -(Color3 a, Color3 b) => new(a.R - b.R, a.G - b.G, a.B - b.B);
    public static Color3 operator *(Color3 a, double b) => new(a.R * b, a.G * b, a.B * b);
    public static Color3 From(uint argb) => new(((argb >> 16) & 255) / 255.0, ((argb >> 8) & 255) / 255.0, (argb & 255) / 255.0);
    public static Color3 Mix(Color3 a, Color3 b, double t) => a + (b - a) * t;
}

/// <summary>A .cube 3D lookup, red varies fastest; tetrahedral-like continuity via trilinear interpolation.</summary>
public sealed class ColorCube
{
    public required int Size { get; init; }
    public required Color3[] Values { get; init; }
    public Color3 Minimum { get; init; } = new(0, 0, 0);
    public Color3 Maximum { get; init; } = new(1, 1, 1);
    public static ColorCube Parse(string text)
    {
        if (text.Length > 24_000_000) throw new InvalidDataException("The color lookup is too large.");
        int size = 0; var values = new List<Color3>(); var min = new Color3(0, 0, 0); var max = new Color3(1, 1, 1);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim(); if (line.Length == 0 || line.StartsWith("TITLE")) continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            double Number(string n) => double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new InvalidDataException("Invalid color lookup value.");
            if (p[0] == "LUT_3D_SIZE" && p.Length == 2) { size = (int)Number(p[1]); if (size is < 2 or > 65) throw new InvalidDataException("Use a 2–65 point 3D color lookup."); }
            else if (p.Length == 4 && p[0] is "DOMAIN_MIN" or "DOMAIN_MAX")
            { var c = new Color3(Number(p[1]), Number(p[2]), Number(p[3])); if (p[0] == "DOMAIN_MIN") min = c; else max = c; }
            else if (p.Length == 3 && values.Count < 65 * 65 * 65) values.Add(new(Number(p[0]), Number(p[1]), Number(p[2])));
            else throw new InvalidDataException("Unsupported color lookup record.");
        }
        if (size < 2 || values.Count != size * size * size || min.R >= max.R || min.G >= max.G || min.B >= max.B) throw new InvalidDataException("Incomplete color lookup.");
        return new() { Size = size, Values = values.ToArray(), Minimum = min, Maximum = max };
    }
    public Color3 Apply(Color3 c)
    {
        double Scale(double v, double lo, double hi) => Math.Clamp((v - lo) / (hi - lo), 0, 1) * (Size - 1);
        var r = Scale(c.R, Minimum.R, Maximum.R); var g = Scale(c.G, Minimum.G, Maximum.G); var b = Scale(c.B, Minimum.B, Maximum.B);
        var x = Math.Min(Size - 2, (int)r); var y = Math.Min(Size - 2, (int)g); var z = Math.Min(Size - 2, (int)b);
        Color3 At(int i, int j, int k) => Values[(k * Size + j) * Size + i];
        Color3 Plane(int k) => Color3.Mix(Color3.Mix(At(x, y, k), At(x + 1, y, k), r - x),
            Color3.Mix(At(x, y + 1, k), At(x + 1, y + 1, k), r - x), g - y);
        return Color3.Mix(Plane(z), Plane(z + 1), b - z);
    }
}

public static class ImageAdjustments
{
    public static bool Apply(CanvasDocument document, Guid id, ImageAdjustmentSettings settings)
    {
        if (!settings.IsValid || !LayerProtection.CanPaint(document, id)
            || document.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset, IsGroup: false, Adjustment: null } layer) return false;
        using var work = DeepPixels.From(asset.WorkingImage);
        var data = MemoryMarshal.Cast<byte, float>(work.GetPixelSpan());
        var stats = Statistics(work);
        double[][]? equal = settings.Kind == ImageAdjustmentKind.Equalize ? Equalization(work) : null;
        float[]? local = settings.Kind is ImageAdjustmentKind.ShadowsHighlights or ImageAdjustmentKind.HdrToning
            ? LocalLuminance(work, Math.Clamp((int)settings.Get("Radius", 30), 1, 250)) : null;
        for (var y = 0; y < work.Height; y++) for (var x = 0; x < work.Width; x++)
        {
            var i = y * work.RowBytes / 4 + x * 4;
            var color = new Color3(data[i], data[i + 1], data[i + 2]);
            var result = Adjust(color, settings, stats.Mean, stats.Deviation, equal, local?[y * work.Width + x] ?? color.Luma);
            data[i] = (float)result.R; data[i + 1] = (float)result.G; data[i + 2] = (float)result.B;
        }
        return SetPixels(document, layer, work);
    }

    public static bool SetPixels(CanvasDocument document, ImageLayer layer, SKBitmap result, bool selection = true)
    {
        if (layer.Asset is not { } asset || !LayerProtection.CanPaint(document, layer.ID)
            || result.Width != asset.Width || result.Height != asset.Height) return false;
        var made = DeepPixels.From(result); using var original = DeepPixels.From(asset.WorkingImage);
        var dst = MemoryMarshal.Cast<byte, float>(made.GetPixelSpan()); var src = MemoryMarshal.Cast<byte, float>(original.GetPixelSpan());
        var region = document.Selection.CoverageRect(document.Width, document.Height);
        using var mask = selection && document.Selection.Path is not null ? document.Selection.Coverage(region) : null;
        var placement = BrushEdits.PixelToDocument(layer.Transform, made.Width, made.Height);
        var locks = LayerProtection.Effective(document, layer.ID);
        for (var y = 0; y < made.Height; y++) for (var x = 0; x < made.Width; x++)
        {
            var i = y * made.RowBytes / 4 + x * 4; double amount = 1;
            if (mask is not null)
            {
                var p = placement.MapPoint(x + .5f, y + .5f); var mx = (int)Math.Floor(p.X) - region.Left; var my = (int)Math.Floor(p.Y) - region.Top;
                amount = mx < 0 || my < 0 || mx >= mask.Width || my >= mask.Height ? 0 : mask.GetPixelSpan()[my * mask.RowBytes + mx] / 255.0;
            }
            for (var c = 0; c < 4; c++)
            {
                var allowed = c == 3 ? !locks.HasFlag(LayerLocks.Transparency)
                    : (document.EditChannels & (ColorChannels)(1 << c)) != 0;
                dst[i + c] = allowed ? (float)(src[i + c] + (dst[i + c] - src[i + c]) * amount) : src[i + c];
            }
        }
        var depth = Math.Max(asset.BitDepth, document.Image.Depth);
        if (depth > 8) layer.Asset = ImportedImage.CreateDeep(made, asset.Name, depth);
        else { using (made) layer.Asset = ImportedImage.Create(DeepPixels.Preview(made), asset.Name); }
        layer.Text = null; layer.Shape = null; return true;
    }

    public static Color3 Adjust(Color3 c, ImageAdjustmentSettings s, double[]? mean = null, double[]? deviation = null,
        double[][]? equal = null, double local = .5)
    {
        double V(string key, double fallback = 0) => s.Get(key, fallback);
        var gray = new Color3(c.Luma, c.Luma, c.Luma); Color3 result = c;
        switch (s.Kind)
        {
            case ImageAdjustmentKind.BrightnessContrast:
                var contrast = Math.Pow(2, V("Contrast") / 100); result = (c - new Color3(.5, .5, .5)) * contrast + new Color3(.5, .5, .5) + new Color3(1, 1, 1) * (V("Brightness") / 150); break;
            case ImageAdjustmentKind.Vibrance:
                var range = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                var skin = c.R > c.G && c.G > c.B ? .5 : 1;
                result = gray + (c - gray) * (1 + V("Saturation") / 100 + V("Vibrance") / 100 * (1 - range) * skin); break;
            case ImageAdjustmentKind.PhotoFilter:
                result = Color3.Mix(c, new Color3(c.R * Color3.From(s.Color).R, c.G * Color3.From(s.Color).G, c.B * Color3.From(s.Color).B), V("Density", 25) / 100);
                if (s.PreserveLuminosity && result.Luma > 1e-9) result *= c.Luma / result.Luma; break;
            case ImageAdjustmentKind.ChannelMixer:
                double Mix(int channel) => c.R * V($"{channel}R", channel == 0 ? 100 : 0) / 100 + c.G * V($"{channel}G", channel == 1 ? 100 : 0) / 100
                    + c.B * V($"{channel}B", channel == 2 ? 100 : 0) / 100 + V($"{channel}Constant") / 100;
                result = s.Monochrome ? new Color3(Mix(0), Mix(0), Mix(0)) : new(Mix(0), Mix(1), Mix(2)); break;
            case ImageAdjustmentKind.ColorLookup: result = s.Cube?.Apply(c) ?? c; break;
            case ImageAdjustmentKind.Posterize:
                var levels = Math.Clamp(Math.Round(V("Levels", 4)), 2, 256) - 1;
                result = new(Math.Round(c.R * levels) / levels, Math.Round(c.G * levels) / levels, Math.Round(c.B * levels) / levels); break;
            case ImageAdjustmentKind.Threshold: var value = c.Luma >= V("Threshold", 128) / 255 ? 1 : 0; result = new(value, value, value); break;
            case ImageAdjustmentKind.SelectiveColor:
                var max = Math.Max(c.R, Math.Max(c.G, c.B)); var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var weights = new[] { Math.Max(0, c.R - Math.Max(c.G, c.B)), Math.Max(0, Math.Min(c.R, c.G) - c.B),
                    Math.Max(0, c.G - Math.Max(c.R, c.B)), Math.Max(0, Math.Min(c.G, c.B) - c.R),
                    Math.Max(0, c.B - Math.Max(c.R, c.G)), Math.Max(0, Math.Min(c.R, c.B) - c.G),
                    Math.Max(0, min * 2 - 1), 1 - Math.Abs(2 * c.Luma - 1), Math.Max(0, 1 - max * 2) };
                for (var band = 0; band < 9; band++)
                {
                    var w = weights[band]; var k = V($"{band}K") / 100;
                    result -= new Color3((V($"{band}C") / 100 + k) * (s.Absolute ? 1 : c.R),
                        (V($"{band}M") / 100 + k) * (s.Absolute ? 1 : c.G), (V($"{band}Y") / 100 + k) * (s.Absolute ? 1 : c.B)) * w;
                } break;
            case ImageAdjustmentKind.ShadowsHighlights:
                var shadows = Math.Pow(Math.Clamp(1 - local / Math.Max(.01, V("Shadow Width", 50) / 100), 0, 1), 2) * V("Shadows", 35) / 100;
                var highlights = Math.Pow(Math.Clamp((local - (1 - V("Highlight Width", 50) / 100)) / Math.Max(.01, V("Highlight Width", 50) / 100), 0, 1), 2) * V("Highlights") / 100;
                result = c + (new Color3(1, 1, 1) - c) * shadows - c * highlights;
                var mid = 1 + V("Midtone Contrast") / 100; result = (result - new Color3(.5, .5, .5)) * mid + new Color3(.5, .5, .5); break;
            case ImageAdjustmentKind.HdrToning:
                var exposure = Math.Pow(2, V("Exposure")); var detail = 1 + (c.Luma - local) * V("Detail", 30) / 100;
                double Tone(double a) => Math.Pow(Math.Max(0, a * exposure * detail) / (1 + Math.Max(0, a * exposure * detail)), 1 / Math.Max(.1, V("Gamma", 1))) * 2;
                result = new(Tone(c.R), Tone(c.G), Tone(c.B)); var l = new Color3(result.Luma, result.Luma, result.Luma);
                result = l + (result - l) * (1 + V("Saturation") / 100); break;
            case ImageAdjustmentKind.Desaturate:
                var lightness = (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / 2; result = new(lightness, lightness, lightness); break;
            case ImageAdjustmentKind.MatchColor:
                if (mean is null || deviation is null || s.ReferenceMean is null || s.ReferenceDeviation is null) break;
                double Match(double a, int ch) => (a - mean[ch]) * s.ReferenceDeviation![ch] / Math.Max(.001, deviation[ch]) + s.ReferenceMean![ch];
                result = Color3.Mix(new(Match(c.R, 0), Match(c.G, 1), Match(c.B, 2)), c, V("Fade") / 100);
                result *= V("Luminance", 100) / 100; var g = new Color3(result.Luma, result.Luma, result.Luma); result = g + (result - g) * (V("Color Intensity", 100) / 100); break;
            case ImageAdjustmentKind.ReplaceColor:
                var delta = c - Color3.From(s.Color); var distance = Math.Sqrt(delta.R * delta.R + delta.G * delta.G + delta.B * delta.B) / Math.Sqrt(3);
                var weight = Math.Clamp(1 - distance / Math.Max(1.0 / 255, V("Fuzziness", 40) / 255), 0, 1);
                result = Color3.Mix(c, Color3.From(s.Replacement) + new Color3(1, 1, 1) * (c.Luma - Color3.From(s.Color).Luma), weight); break;
            case ImageAdjustmentKind.Equalize:
                if (equal is not null) result = new(equal[0][Math.Clamp((int)(c.R * 4095), 0, 4095)], equal[1][Math.Clamp((int)(c.G * 4095), 0, 4095)], equal[2][Math.Clamp((int)(c.B * 4095), 0, 4095)]); break;
        }
        return s.Kind == ImageAdjustmentKind.HdrToning ? result : result.Clamp();
    }

    public static (double[] Mean, double[] Deviation) Statistics(SKBitmap bitmap)
    {
        using var pixels = DeepPixels.From(bitmap); var p = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
        var sum = new double[3]; var squares = new double[3]; double count = 0;
        for (var i = 0; i < p.Length; i += 4) { var a = p[i + 3]; count += a; for (var c = 0; c < 3; c++) { sum[c] += p[i + c] * a; squares[c] += p[i + c] * p[i + c] * a; } }
        for (var c = 0; c < 3; c++) { sum[c] /= Math.Max(1, count); squares[c] = Math.Sqrt(Math.Max(0, squares[c] / Math.Max(1, count) - sum[c] * sum[c])); }
        return (sum, squares);
    }
    private static double[][] Equalization(SKBitmap bitmap)
    {
        var hist = Enumerable.Range(0, 3).Select(_ => new double[4096]).ToArray(); var p = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan());
        for (var i = 0; i < p.Length; i += 4) for (var c = 0; c < 3; c++) hist[c][Math.Clamp((int)(p[i + c] * 4095), 0, 4095)] += p[i + 3];
        foreach (var h in hist) { var total = h.Sum(); var first = h.FirstOrDefault(v => v > 0); double running = 0; for (var i = 0; i < h.Length; i++) { running += h[i]; h[i] = total <= first ? i / 4095.0 : Math.Clamp((running - first) / (total - first), 0, 1); } }
        return hist;
    }
    private static float[] LocalLuminance(SKBitmap bitmap, int radius)
    {
        var width = bitmap.Width; var height = bitmap.Height; var source = new float[width * height]; var horizontal = new float[source.Length]; var output = new float[source.Length];
        var p = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan());
        for (var i = 0; i < source.Length; i++) source[i] = p[i * 4] * .2126f + p[i * 4 + 1] * .7152f + p[i * 4 + 2] * .0722f;
        for (var y = 0; y < height; y++) { double sum = 0; for (var x = -radius; x <= radius; x++) sum += source[y * width + Math.Clamp(x, 0, width - 1)];
            for (var x = 0; x < width; x++) { horizontal[y * width + x] = (float)(sum / (radius * 2 + 1)); sum += source[y * width + Math.Min(width - 1, x + radius + 1)] - source[y * width + Math.Max(0, x - radius)]; } }
        for (var x = 0; x < width; x++) { double sum = 0; for (var y = -radius; y <= radius; y++) sum += horizontal[Math.Clamp(y, 0, height - 1) * width + x];
            for (var y = 0; y < height; y++) { output[y * width + x] = (float)(sum / (radius * 2 + 1)); sum += horizontal[Math.Min(height - 1, y + radius + 1) * width + x] - horizontal[Math.Max(0, y - radius) * width + x]; } }
        return output;
    }
}
