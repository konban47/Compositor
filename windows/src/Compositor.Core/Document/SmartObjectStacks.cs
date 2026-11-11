using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// Layer ▸ Smart Objects ▸ Stack Mode: the smart object's own layers combined pixel by pixel with a statistic in place of
/// being composited, as Photoshop's stack modes do for exposures of one scene (Median clears passers-by, Mean averages
/// out noise). Each layer's pixels are drawn whole at their place; opacity, masks and blending do not take part.
/// </summary>
public static class SmartObjectStacks
{
    /// <summary>The statistics, in the order Photoshop's menu lists them.</summary>
    public static readonly string[] Modes = ["Standard Deviation", "Range", "Variance", "Kurtosis", "Skewness", "Mean", "Entropy", "Median", "Summation", "Maximum", "Minimum"];

    /// <summary>What a smart object with these contents shows: the normal composite with no mode, the statistic with one.</summary>
    public static SKBitmap Render(CanvasDocument contents, string? mode)
    {
        if (mode is null) return DocumentRenderer.Render(contents);
        if (!Modes.Contains(mode)) throw new ArgumentException("Unknown stack mode.", nameof(mode));
        int width = contents.Width, height = contents.Height;
        var layers = contents.Layers.Where(l => l.IsVisible && l.Asset is not null && !l.IsGroup && l.Adjustment is null).ToArray();
        var planes = new List<SKBitmap>();
        try
        {
            foreach (var layer in layers)
            {
                var plane = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); plane.Erase(SKColors.Transparent);
                using (var canvas = new SKCanvas(plane)) DocumentRenderer.DrawLayerPixels(canvas, layer);
                planes.Add(plane);
            }
            var result = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); result.Erase(SKColors.Transparent);
            if (planes.Count == 0) return result;
            var spans = planes.Select(p => p.GetPixelSpan().ToArray()).ToArray();
            var output = result.GetPixelSpan(); var row = result.RowBytes; var rows = planes[0].RowBytes;
            var samples = new double[planes.Count];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = y * rows + x * 4; var to = y * row + x * 4;
                var alpha = 0; var counted = 0;
                foreach (var span in spans) { alpha = Math.Max(alpha, (int)span[at + 3]); if (span[at + 3] > 0) counted++; }
                if (counted == 0) continue;
                for (var c = 0; c < 3; c++)
                {
                    var n = 0;
                    foreach (var span in spans) if (span[at + 3] > 0) samples[n++] = span[at + c] * 255.0 / span[at + 3];
                    var value = Statistic(mode, samples.AsSpan(0, n));
                    output[to + c] = (byte)Math.Clamp(Math.Round(value * alpha / 255.0), 0, 255);
                }
                output[to + 3] = (byte)alpha;
            }
            return result;
        }
        finally { foreach (var plane in planes) plane.Dispose(); }
    }

    /// <summary>One statistic of 0–255 samples, scaled back into 0–255 where it is not already a level.</summary>
    public static double Statistic(string mode, ReadOnlySpan<double> values)
    {
        var n = values.Length; if (n == 0) return 0; var copy = values.ToArray();
        double mean = 0; foreach (var v in values) mean += v; mean /= n;
        double Moment(int k) { double s = 0; foreach (var v in copy) s += Math.Pow(v - mean, k); return s / n; }
        switch (mode)
        {
            case "Mean": return mean;
            case "Summation": { double s = 0; foreach (var v in values) s += v; return Math.Min(255, s); }
            case "Maximum": { var m = double.MinValue; foreach (var v in values) m = Math.Max(m, v); return m; }
            case "Minimum": { var m = double.MaxValue; foreach (var v in values) m = Math.Min(m, v); return m; }
            case "Range": { double lo = double.MaxValue, hi = double.MinValue; foreach (var v in values) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); } return hi - lo; }
            case "Median":
            {
                var sorted = values.ToArray(); Array.Sort(sorted);
                return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
            }
            case "Variance": return Math.Min(255, Moment(2) / 255);
            case "Standard Deviation": return Math.Min(255, Math.Sqrt(Moment(2)));
            case "Skewness": { var sd = Math.Sqrt(Moment(2)); return sd < 1e-9 ? 127.5 : Math.Clamp(127.5 + Moment(3) / (sd * sd * sd) * 42.5, 0, 255); }
            case "Kurtosis": { var variance = Moment(2); return variance < 1e-9 ? 0 : Math.Clamp(Moment(4) / (variance * variance) * 25.5, 0, 255); }
            case "Entropy":
            {
                var counts = new Dictionary<int, int>(); foreach (var v in values) { var key = (int)Math.Round(v); counts[key] = counts.GetValueOrDefault(key) + 1; }
                double entropy = 0; foreach (var count in counts.Values) { var p = count / (double)n; entropy -= p * Math.Log2(p); }
                return n <= 1 ? 0 : Math.Clamp(entropy / Math.Log2(n) * 255, 0, 255);
            }
            default: throw new ArgumentException("Unknown stack mode.", nameof(mode));
        }
    }

    /// <summary>Sets the Stack Mode of every layer sharing this smart object, null for none, and redraws them.</summary>
    public static bool Apply(CanvasDocument document, Guid layerID, string? mode)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == layerID) is not { SmartObject: { } smart } || smart.StackMode == mode) return false;
        if (mode is not null && !Modes.Contains(mode)) return false;
        var layers = document.Layers.Where(l => l.SmartObject?.ID == smart.ID).ToArray();
        if (!LayerWorkflow.Editable(document, layers.Select(l => l.ID))) return false;
        using var contents = smart.Open().ToDocument();
        using var rendered = Render(contents, mode);
        var updated = new SmartObjectData(smart.ID, smart.Package) { LinkedPath = smart.LinkedPath, LinkedHash = smart.LinkedHash, StackMode = mode };
        foreach (var layer in layers) { layer.SmartObject = updated; layer.Asset = ImportedImage.Create(rendered.Copy(), layer.Name); }
        return true;
    }
}
