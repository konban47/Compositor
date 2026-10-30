using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum HdrMode { Bits32, Bits16, Bits8 }
public enum HdrToneMethod { LocalAdaptation, EqualizeHistogram, ExposureAndGamma, HighlightCompression }

/// <summary>Merge to HDR Pro's toning for a 16- or 8-bit result. Radius is in pixels; Strength 0.1–4; Gamma 0.1–2;
/// Exposure in stops, −5–5; the rest −100–100, as Photoshop's panel has them.</summary>
public sealed record HdrToning
{
    public HdrToneMethod Method { get; init; } = HdrToneMethod.LocalAdaptation;
    public double Radius { get; init; } = 115;
    public double Strength { get; init; } = .52;
    public double Gamma { get; init; } = 1;
    public double Exposure { get; init; }
    public double Detail { get; init; } = 30;
    public double Shadow { get; init; }
    public double Highlight { get; init; }
    public double Vibrance { get; init; }
    public double Saturation { get; init; } = 20;

    public bool IsValid => Enum.IsDefined(Method) && new[] { Radius, Strength, Gamma, Exposure, Detail, Shadow, Highlight, Vibrance, Saturation }.All(double.IsFinite)
        && Radius is >= 1 and <= 500 && Strength is >= .1 and <= 4 && Gamma is >= .1 and <= 2 && Exposure is >= -5 and <= 5
        && new[] { Detail, Shadow, Highlight, Vibrance, Saturation }.All(v => v is >= -100 and <= 300);
}

public sealed record HdrOptions(HdrMode Mode = HdrMode.Bits16, bool RemoveGhosts = false, bool AutoAlign = true, HdrToning? Toning = null);

/// <summary>The merged picture as a one-layer document, and each source's exposure relative to the darkest.</summary>
public sealed record HdrResult(CanvasDocument Document, IReadOnlyList<double> Exposures);

/// <summary>
/// File › Automate › Merge to HDR Pro: bracketed exposures of one scene merged into the light it held (Debevec and
/// Malik's weighted average, each photo's sRGB undone and divided by its exposure, which is worked out from the photos
/// themselves), then kept at 32 bits or toned into 16 or 8. Remove Ghosts keeps, where the photos disagree, only the
/// middle exposure's reading of anything that moved.
/// </summary>
public static class HdrMerge
{
    public static HdrResult Merge(IReadOnlyList<(string Name, SKBitmap Image)> sources, HdrOptions options, CancellationToken cancellation = default)
    {
        if (sources.Count < 2) throw new ArgumentException("Merge to HDR Pro needs at least two exposures.");
        if (sources.Any(s => s.Image.Width != sources[0].Image.Width || s.Image.Height != sources[0].Image.Height))
            throw new ArgumentException("The exposures must be the same size.");
        var toning = options.Toning ?? new HdrToning();
        if (!toning.IsValid) throw new ArgumentException("Invalid HDR toning.");
        var images = sources.Select(s => s.Image).ToArray();
        var shifted = options.AutoAlign ? Align(images, cancellation) : null;
        try
        {
            var radiance = Radiance(shifted ?? images, options.RemoveGhosts, out var exposures, cancellation);
            int width = images[0].Width, height = images[0].Height;
            var result = options.Mode == HdrMode.Bits32 ? Encode(radiance, width, height) : Tone(radiance, width, height, toning);
            var depth = options.Mode switch { HdrMode.Bits32 => 32, HdrMode.Bits16 => 16, _ => 8 };
            var document = new CanvasDocument(Guid.NewGuid(), width, height) { Image = new Format.ImageSettings { Depth = depth } };
            var name = sources[0].Name;
            var asset = depth == 8 ? ImportedImage.Create(DeepPixels.Preview(result), name) : ImportedImage.CreateDeep(result, name, depth);
            if (depth == 8) result.Dispose();
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), asset, new Model.LayerTransform(0, 0, width, height), name));
            return new HdrResult(document, exposures);
        }
        finally { if (shifted is not null) foreach (var image in shifted) image.Dispose(); }
    }

    /// <summary>Each exposure moved onto the middle one, by the shift their edges agree on best.</summary>
    private static SKBitmap[] Align(SKBitmap[] images, CancellationToken cancellation)
    {
        var middle = images.Length / 2;
        var result = new SKBitmap[images.Length];
        var maxShift = Math.Max(2, Math.Max(images[0].Width, images[0].Height) / 20);
        for (var i = 0; i < images.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var (dx, dy) = i == middle ? (0, 0) : Shift(images[middle], images[i], maxShift);
            var copy = Bitmaps.Allocate(images[i].Info);
            using (var canvas = new SKCanvas(copy))
            using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(images[i], dx, dy, new SKSamplingOptions(SKFilterMode.Nearest), paint);
            }
            result[i] = copy;
        }
        return result;
    }

    /// <summary>
    /// How far to move <paramref name="moving"/> onto <paramref name="reference"/>: the gradients of their log
    /// brightness, which an exposure change leaves as they are, correlated over the pixels neither clips; searched on a
    /// small copy, then refined at full size.
    /// </summary>
    internal static (int X, int Y) Shift(SKBitmap reference, SKBitmap moving, int maxShift)
    {
        (float[] Gx, float[] Gy, bool[] Valid, int W, int H) Edges(SKBitmap image, int w, int h)
        {
            var rgba = LightPlanes.Rgba(image, w, h);
            var log = new float[w * h]; var valid = new bool[w * h];
            for (var i = 0; i < log.Length; i++)
            {
                var z = .2126f * rgba[i * 4] + .7152f * rgba[i * 4 + 1] + .0722f * rgba[i * 4 + 2];
                log[i] = MathF.Log(LightPlanes.Linear(z) + 1e-3f); valid[i] = z is > .02f and < .97f && rgba[i * 4 + 3] > .5f;
            }
            var gx = new float[log.Length]; var gy = new float[log.Length];
            for (var y = 1; y < h - 1; y++) for (var x = 1; x < w - 1; x++)
            { gx[y * w + x] = log[y * w + x + 1] - log[y * w + x - 1]; gy[y * w + x] = log[(y + 1) * w + x] - log[(y - 1) * w + x]; }
            return (gx, gy, valid, w, h);
        }
        double Score((float[] Gx, float[] Gy, bool[] Valid, int W, int H) a, (float[] Gx, float[] Gy, bool[] Valid, int W, int H) b, int dx, int dy)
        {
            double product = 0, aa = 0, bb = 0; var count = 0;
            for (var y = Math.Max(1, dy + 1); y < Math.Min(a.H - 1, a.H - 1 + dy); y++)
                for (var x = Math.Max(1, dx + 1); x < Math.Min(a.W - 1, a.W - 1 + dx); x++)
                {
                    var i = y * a.W + x; var j = (y - dy) * b.W + x - dx;
                    if (!a.Valid[i] || !b.Valid[j]) continue;
                    product += a.Gx[i] * b.Gx[j] + a.Gy[i] * b.Gy[j];
                    aa += a.Gx[i] * a.Gx[i] + a.Gy[i] * a.Gy[i]; bb += b.Gx[j] * b.Gx[j] + b.Gy[j] * b.Gy[j]; count++;
                }
            return count < 16 || aa * bb <= 1e-12 ? double.NegativeInfinity : product / Math.Sqrt(aa * bb) - (Math.Abs(dx) + Math.Abs(dy)) * 1e-6;
        }
        var (w, h, s) = LightPlanes.Grid(reference.Width, reference.Height, 320);
        var small = (A: Edges(reference, w, h), B: Edges(moving, w, h));
        var limit = Math.Max(1, (int)Math.Ceiling(maxShift * s));
        (int X, int Y) best = (0, 0); var bestScore = double.NegativeInfinity;
        for (var dy = -limit; dy <= limit; dy++) for (var dx = -limit; dx <= limit; dx++)
        {
            var score = Score(small.A, small.B, dx, dy);
            if (score > bestScore) { bestScore = score; best = (dx, dy); }
        }
        if (s >= 1) return best;
        var full = (A: Edges(reference, reference.Width, reference.Height), B: Edges(moving, moving.Width, moving.Height));
        var center = ((int)Math.Round(best.X / s), (int)Math.Round(best.Y / s)); var reach = (int)Math.Ceiling(1 / s);
        (int X, int Y) refined = center; bestScore = double.NegativeInfinity;
        for (var dy = center.Item2 - reach; dy <= center.Item2 + reach; dy++) for (var dx = center.Item1 - reach; dx <= center.Item1 + reach; dx++)
        {
            var score = Score(full.A, full.B, dx, dy);
            if (score > bestScore) { bestScore = score; refined = (dx, dy); }
        }
        return refined;
    }
    /// <summary>The scene's light per pixel and channel (linear, relative to the middle exposure), and each photo's
    /// exposure relative to the darkest.</summary>
    public static float[] Radiance(IReadOnlyList<SKBitmap> images, bool removeGhosts, out double[] exposures, CancellationToken cancellation = default)
    {
        int width = images[0].Width, height = images[0].Height, n = width * height;
        var linear = images.Select(image =>
        {
            var rgba = LightPlanes.Rgba(image, width, height);
            for (var i = 0; i < rgba.Length; i++) if (i % 4 != 3) rgba[i] = LightPlanes.Linear(rgba[i]);
            return rgba;
        }).ToArray();
        // Darkest to brightest by their mean, and each one's exposure from the median ratio where both are well exposed.
        var order = Enumerable.Range(0, images.Count).OrderBy(i => Mean(linear[i])).ToArray();
        var relative = new double[images.Count];
        relative[order[0]] = 1;
        for (var k = 1; k < order.Length; k++)
        {
            cancellation.ThrowIfCancellationRequested();
            var (dark, bright) = (linear[order[k - 1]], linear[order[k]]);
            var ratios = new List<double>();
            for (var i = 0; i < n; i += Math.Max(1, n / 60000))
            {
                var a = Luma(dark, i); var b = Luma(bright, i);
                if (a > .01 && b > .01 && a < .8 && b < .8) ratios.Add(b / a);
            }
            ratios.Sort();
            var ratio = ratios.Count > 20 ? ratios[ratios.Count / 2] : Math.Max(1.0001, (Mean(bright) + 1e-6) / (Mean(dark) + 1e-6));
            relative[order[k]] = relative[order[k - 1]] * Math.Max(1, ratio);
        }
        exposures = relative;
        var middle = order[order.Length / 2];
        var reference = relative[middle];
        var radiance = new float[n * 4];
        var exposure = relative.Select(e => (float)(e / reference)).ToArray();
        var darkest = order[0]; var brightest = order[^1];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellation }, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                // Where the middle exposure is well exposed, it says what the light was for Remove Ghosts.
                var guide = Luma(linear[middle], i);
                var trusted = removeGhosts && guide is > .02f and < .9f ? guide / exposure[middle] : -1f;
                for (var c = 0; c < 3; c++)
                {
                    double sum = 0, weights = 0;
                    for (var k = 0; k < linear.Length; k++)
                    {
                        var v = linear[k][i * 4 + c];
                        var z = LightPlanes.Gamma(v);
                        // A hat on the stored value; the darkest still counts in highlights and the brightest in shadows.
                        var w = Math.Min(z, 1 - z);
                        if (k == darkest && z > .5f) w = Math.Max(w, .5f - .25f * (1 - z));
                        if (k == brightest && z < .5f) w = Math.Max(w, .25f * z + .02f);
                        w = Math.Max(w, 1e-4f) * (z >= .995f && k != darkest ? 1e-3f : 1);
                        if (trusted > 0 && k != middle)
                        {
                            var reading = Luma(linear[k], i) / exposure[k];
                            if (reading > 0 && Math.Abs(Math.Log(reading / trusted)) > .35) continue;
                        }
                        sum += w * v / exposure[k]; weights += w;
                    }
                    radiance[i * 4 + c] = weights > 0 ? (float)(sum / weights) : 0;
                }
                radiance[i * 4 + 3] = linear.Max(l => l[i * 4 + 3]);
            }
        });
        return radiance;
    }

    private static float Luma(float[] rgba, int i) => .2126f * rgba[i * 4] + .7152f * rgba[i * 4 + 1] + .0722f * rgba[i * 4 + 2];
    private static double Mean(float[] rgba)
    {
        double sum = 0; var count = 0;
        for (var i = 0; i < rgba.Length / 4; i += 7) { sum += Luma(rgba, i); count++; }
        return sum / Math.Max(1, count);
    }

    /// <summary>A 32-bit layer's pixels: the light sRGB-encoded, past 1 where it is brighter than white.</summary>
    private static SKBitmap Encode(float[] radiance, int width, int height)
    {
        var result = Bitmaps.Allocate(DeepPixels.Info(width, height));
        var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        for (var i = 0; i < radiance.Length; i++) output[i] = i % 4 == 3 ? radiance[i] : LightPlanes.Gamma(Math.Max(0, radiance[i]));
        return result;
    }

    /// <summary>The light toned into 0–1 (sRGB, RgbaF32) by one of the four methods.</summary>
    public static SKBitmap Tone(float[] radiance, int width, int height, HdrToning toning)
    {
        var n = width * height;
        var luma = new float[n];
        for (var i = 0; i < n; i++) luma[i] = Math.Max(1e-6f, Luma(radiance, i));
        var toned = new float[n];
        var exposure = (float)Math.Pow(2, toning.Exposure);
        switch (toning.Method)
        {
            case HdrToneMethod.ExposureAndGamma:
                for (var i = 0; i < n; i++) toned[i] = (float)Math.Pow(luma[i] * exposure, 1 / toning.Gamma);
                break;
            case HdrToneMethod.HighlightCompression:
            {
                // Reinhard's global curve, the scene's log-average put at middle gray.
                var key = Math.Exp(luma.Average(v => Math.Log(v)));
                for (var i = 0; i < n; i++) { var l = luma[i] * .18 / key; toned[i] = (float)(l / (1 + l)); }
                break;
            }
            case HdrToneMethod.EqualizeHistogram:
            {
                var logs = luma.Select(v => Math.Log(v)).ToArray();
                var sorted = (double[])logs.Clone(); Array.Sort(sorted);
                for (var i = 0; i < n; i++)
                {
                    var rank = Array.BinarySearch(sorted, logs[i]); if (rank < 0) rank = ~rank;
                    toned[i] = LightPlanes.Linear((float)(rank / (double)Math.Max(1, n - 1)));
                }
                break;
            }
            default:
            {
                // Local Adaptation: the log light split into a base the edges hold (a guided filter of Radius) and
                // the detail on it; the base's range is compressed by Strength, the detail kept and boosted by Detail.
                var logs = new float[n];
                for (var i = 0; i < n; i++) logs[i] = (float)Math.Log2(luma[i] * exposure);
                float low = logs.Min(), high = logs.Max();
                var span = Math.Max(1e-3f, high - low);
                var normal = logs.Select(v => (v - low) / span).ToArray();
                var radius = (int)Math.Clamp(toning.Radius * Math.Max(width, height) / 1000.0, 1, 200);
                var smooth = LightPlanes.Guided(normal, normal, width, height, radius, .01f * (float)toning.Strength);
                var compression = (float)Math.Clamp(6.5 / span / Math.Sqrt(toning.Strength), .05, 1);
                var detail = 1 + (float)toning.Detail / 100;
                var shaped = new float[n];
                for (var i = 0; i < n; i++)
                {
                    var b = smooth[i] * span + low; var d = logs[i] - b;
                    var t = smooth[i];
                    // Shadow lifts the dark end of the base, Highlight the bright end.
                    shaped[i] = (b - high) * compression + d * detail + (float)(toning.Shadow / 100 * (1 - t) * 1.5 + toning.Highlight / 100 * t * 1.5);
                }
                // The brightest of the picture (all but its last half percent) sits a fifth of a stop under white, and
                // what is brighter still rolls off into white rather than clipping.
                var sorted = (float[])shaped.Clone(); Array.Sort(sorted);
                var top = sorted[(int)Math.Min(n - 1, n * .995)];
                var lift = -.2f - top;
                for (var i = 0; i < n; i++)
                {
                    var v = (float)Math.Pow(Math.Pow(2, shaped[i] + lift), 1 / toning.Gamma);
                    toned[i] = v < .8f ? v : .8f + .2f * (1 - MathF.Exp(-(v - .8f) / .2f));
                }
                break;
            }
        }
        var result = Bitmaps.Allocate(DeepPixels.Info(width, height));
        var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        var saturation = 1 + toning.Saturation / 100;
        for (var i = 0; i < n; i++)
        {
            var scale = toned[i] / luma[i];
            float r = radiance[i * 4] * scale, g = radiance[i * 4 + 1] * scale, b = radiance[i * 4 + 2] * scale;
            var gray = toned[i];
            // Vibrance adds more to colors that have little; Saturation to all.
            var chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            var amount = saturation * (1 + toning.Vibrance / 100 * (1 - Math.Clamp(chroma / Math.Max(1e-4, gray), 0, 1)));
            output[i * 4] = LightPlanes.Gamma(Math.Clamp((float)(gray + (r - gray) * amount), 0, 1));
            output[i * 4 + 1] = LightPlanes.Gamma(Math.Clamp((float)(gray + (g - gray) * amount), 0, 1));
            output[i * 4 + 2] = LightPlanes.Gamma(Math.Clamp((float)(gray + (b - gray) * amount), 0, 1));
            output[i * 4 + 3] = radiance[i * 4 + 3];
        }
        return result;
    }
}
