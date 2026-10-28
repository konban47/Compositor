using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum LensEffect { [System.Text.Json.Serialization.JsonStringEnumMemberName("Anamorphic Flare")] AnamorphicFlare, Starburst, Mist, Prism, Bokeh }
public enum BokehShape { Round, Hexagon, Oval, [System.Text.Json.Serialization.JsonStringEnumMemberName("Cat's Eye")] CatsEye }
public sealed record LensEffectSettings
{
    public LensEffect Effect { get; set; }
    public double Threshold { get; set; } = 90;
    public double FlareAmount { get; set; } = 50;
    public double FlareLength { get; set; } = 60;
    public double FlareBlue { get; set; } = 80;
    public double StarAmount { get; set; } = 50;
    public double StarLength { get; set; } = 12;
    public int StarPoints { get; set; } = 6;
    public double StarAngle { get; set; } = 15;
    public double MistStrength { get; set; } = 50;
    public double MistSize { get; set; } = 30;
    public double PrismAmount { get; set; } = 80;
    public int PrismCopies { get; set; } = 5;
    public double PrismOffset { get; set; } = 6;
    public double PrismAngle { get; set; } = 180;
    public bool PrismBothWays { get; set; }
    public double PrismBlur { get; set; }
    public double PrismFringe { get; set; } = 30;
    public double BokehRadius { get; set; } = 2;
    public double BokehBrightness { get; set; } = 50;
    public double BokehRing { get; set; } = 100;
    public BokehShape BokehShape { get; set; }
    public bool IsValid => Enum.IsDefined(Effect) && Enum.IsDefined(BokehShape) && new[] { Threshold, FlareAmount, FlareLength, FlareBlue, StarAmount, StarLength, MistStrength, MistSize, PrismAmount, PrismBlur, PrismFringe, BokehBrightness, BokehRing }.All(v => double.IsFinite(v) && v >= 0 && v <= 100)
        && Threshold >= 50 && FlareLength >= 1 && StarLength is >= 2 and <= 40 && new[] { 4, 6, 8, 12, 14 }.Contains(StarPoints)
        && double.IsFinite(StarAngle) && Math.Abs(StarAngle) <= 180 && double.IsFinite(PrismAngle) && Math.Abs(PrismAngle) <= 180
        && PrismCopies is >= 1 and <= 8 && double.IsFinite(PrismOffset) && PrismOffset is >= 0 and <= 50 && double.IsFinite(BokehRadius) && BokehRadius is >= .5 and <= 6;
    public bool DoesAnything => Effect switch { LensEffect.AnamorphicFlare => FlareAmount > 0, LensEffect.Starburst => StarAmount > 0, LensEffect.Mist => MistStrength > 0, LensEffect.Prism => PrismAmount > 0, _ => true };
}

/// <summary>CPU lens optics based on upstream 1.5.8. Light is calculated in linear RGB;
/// bounded working grids keep long streaks and aperture integration responsive without Metal.</summary>
public static class LensEffects
{
    public static SKBitmap Apply(SKBitmap source, LensEffectSettings s)
    {
        if (!s.IsValid) throw new ArgumentException("Invalid lens effect settings.", nameof(s));
        if (!s.DoesAnything) return DeepPixels.From(source);
        var side = Math.Max(source.Width, source.Height); var scale = Math.Min(1d, (s.Effect == LensEffect.Bokeh ? 384 : 1024) / (double)side);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale)); var h = Math.Max(1, (int)Math.Round(source.Height * scale)); var size = Math.Max(w, h);
        using var original = DeepPixels.From(source);
        using var small = Bitmaps.Allocate(DeepPixels.Info(w, h));
        using (var canvas = new SKCanvas(small)) canvas.DrawBitmap(original, SKRect.Create(w, h), new SKSamplingOptions(SKFilterMode.Linear));
        var pixels = MemoryMarshal.Cast<byte, float>(small.GetPixelSpan()).ToArray(); var light = new float[pixels.Length];
        for (var i = 0; i < light.Length; i += 4) { for (var c = 0; c < 3; c++) light[i + c] = Linear(pixels[i + c]); light[i + 3] = 1; }
        var result = (float[])light.Clone();
        float[] Highlights(double threshold)
        {
            var data = new float[light.Length]; var t = Math.Min(.95, threshold);
            for (var i = 0; i < data.Length; i += 4)
            {
                var lum = .2126 * pixels[i] + .7152 * pixels[i + 1] + .0722 * pixels[i + 2];
                var f = Smooth((lum - t) / (1 - t));
                for (var c = 0; c < 3; c++) data[i + c] = light[i + c] * f; data[i + 3] = 1;
            }
            if (threshold > .95)
            {
                var density = (threshold - .95) / .05; var around = Blur(data, w, h, size * .03 * density); var peak = around.Where((_, i) => i % 4 != 3).DefaultIfEmpty().Max();
                if (peak > .001) for (var i = 0; i < data.Length; i += 4) { var lum = Math.Max(around[i], Math.Max(around[i + 1], around[i + 2])); var f = Smooth((lum / peak - (.2 + .6 * density)) / .1); for (var c = 0; c < 3; c++) data[i + c] *= f; }
            }
            return data;
        }
        void Add(float[] data, double gain, int channel = -1, double limit = 0)
        {
            for (var i = 0; i < result.Length; i += 4) for (var c = 0; c < 3; c++) if (channel < 0 || channel == c)
            { var v = Math.Max(0, data[i + c] * gain); result[i + c] += (float)(limit > 0 ? limit * (1 - Math.Exp(-v / limit)) : v); }
        }
        switch (s.Effect)
        {
            case LensEffect.Mist:
                var radius = size * (.2 + s.MistSize / 100 * 1.8) / 100; var strength = s.MistStrength / 100; var scatter = strength * .4;
                for (var i = 0; i < result.Length; i++) if (i % 4 != 3) result[i] *= (float)(1 - scatter);
                Add(Blur(light, w, h, radius * .6), scatter * .6); Add(Blur(light, w, h, radius * 4), scatter * .4); Add(Blur(Highlights(.75), w, h, radius * 1.5), strength * .35);
                break;
            case LensEffect.AnamorphicFlare:
                var lights = Blur(Highlights(s.Threshold / 100), w, h, Math.Max(.5, size * .004)); var blue = s.FlareBlue / 100; var gain = s.FlareAmount / 50;
                for (var i = 0; i < lights.Length; i++) if (i % 4 != 3) lights[i] *= (lights[i] + .12f) * 1.6f;
                for (var c = 0; c < 3; c++)
                {
                    var reach = size * s.FlareLength / 100 * (1 - blue * new[] { .7, .4, 0 }[c]); var coat = 1 - blue * new[] { .9, .62, 0 }[c];
                    Add(Streak(lights, w, h, reach, 0), gain * 4 * coat, c, .6);
                    foreach (var (fraction, weight) in new[] { (.08, .8), (.3, .35), (.9, .15) }) Add(Streak(lights, w, h, reach * fraction, 0), weight * Math.Sqrt(Math.Max(.001, reach * fraction / (size * .08))) * gain * coat, c, .7);
                }
                Add(Blur(lights, w, h, size * .04), gain * .5); Add(Streak(lights, w, h, size * .015, 0), gain * 1.2); break;
            case LensEffect.Starburst:
                var stars = Highlights(s.Threshold / 100);
                for (var spoke = 0; spoke < s.StarPoints / 2; spoke++)
                {
                    var angle = s.StarAngle * Math.PI / 180 + spoke * Math.PI / (s.StarPoints / 2);
                    foreach (var (fraction, weight) in new[] { (.04, 1.8), (.12, .9), (.35, .35), (1d, .1) })
                        Add(Streak(stars, w, h, Math.Max(1, size * s.StarLength / 100 * fraction), angle), weight * s.StarAmount / 50);
                }
                break;
            case LensEffect.Prism:
                var theta = s.PrismAngle * Math.PI / 180;
                for (var copy = s.PrismCopies; copy > 0; copy--)
                {
                    var data = s.PrismBlur > 0 ? Blur(light, w, h, size * s.PrismBlur / 10000 * copy) : light; var fade = Math.Pow(.78, copy - 1) * s.PrismAmount / 100;
                    for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) for (var c = 0; c < 3; c++)
                    {
                        var at = (y * w + x) * 4 + c; var shift = size * (s.PrismOffset / 100 * copy + ((1 - c) * s.PrismFringe / 10000));
                        for (var way = 1; way >= (s.PrismBothWays ? -1 : 1); way -= 2) result[at] = Math.Max(result[at], (float)(Sample(data, w, h, x - way * shift * Math.Cos(theta), y + way * shift * Math.Sin(theta), c, false) * fade));
                    }
                }
                break;
            case LensEffect.Bokeh:
                var glow = Highlights(.8); for (var i = 0; i < glow.Length; i++) if (i % 4 != 3) glow[i] *= (float)(12 + s.BokehBrightness * .7);
                result = Aperture(light, w, h, size * s.BokehRadius / 100, s); Add(Aperture(glow, w, h, size * s.BokehRadius / 100, s), 1, limit: .8); break;
        }
        // Upsample only the optical change; preserve untouched full resolution detail and the original alpha.
        for (var i = 0; i < result.Length; i += 4) { for (var c = 0; c < 3; c++) if (s.Effect != LensEffect.Bokeh) result[i + c] -= light[i + c]; result[i + 3] = 1; }
        using var change = Bitmap(result, w, h); using var full = Bitmaps.Allocate(DeepPixels.Info(source.Width, source.Height));
        using (var canvas = new SKCanvas(full)) canvas.DrawBitmap(change, SKRect.Create(full.Width, full.Height), new SKSamplingOptions(SKFilterMode.Linear));
        var delta = MemoryMarshal.Cast<byte, float>(full.GetPixelSpan()); var before = MemoryMarshal.Cast<byte, float>(original.GetPixelSpan());
        for (var i = 0; i < delta.Length; i += 4) { for (var c = 0; c < 3; c++) delta[i + c] = Gamma(Math.Max(0, (s.Effect == LensEffect.Bokeh ? 0 : Linear(before[i + c])) + delta[i + c])); delta[i + 3] = before[i + 3]; }
        return DeepPixels.From(full);
    }
    private static float Linear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
    private static float Gamma(float v) => v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f;
    private static float Smooth(double value) { var v = (float)Math.Clamp(value, 0, 1); return v * v * (3 - 2 * v); }
    private static SKBitmap Bitmap(float[] data, int w, int h) { var image = Bitmaps.Allocate(DeepPixels.Info(w, h)); data.CopyTo(MemoryMarshal.Cast<byte, float>(image.GetPixelSpan())); return image; }
    private static float[] Blur(float[] data, int w, int h, double radius)
    { using var source = Bitmap(data, w, h); using var blurred = DeepAdjustments.Blur(source, Math.Max(.01, radius), true); return MemoryMarshal.Cast<byte, float>(blurred.GetPixelSpan()).ToArray(); }
    private static float Sample(float[] data, int w, int h, double x, double y, int c, bool clamp = true)
    {
        if (!clamp && (x < 0 || y < 0 || x > w - 1 || y > h - 1)) return 0;
        x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, h - 1); var ix = (int)x; var iy = (int)y; var fx = (float)(x - ix); var fy = (float)(y - iy);
        var a = data[(iy * w + ix) * 4 + c]; var b = data[(iy * w + Math.Min(w - 1, ix + 1)) * 4 + c];
        var d = data[(Math.Min(h - 1, iy + 1) * w + ix) * 4 + c]; var e = data[(Math.Min(h - 1, iy + 1) * w + Math.Min(w - 1, ix + 1)) * 4 + c];
        return (a + (b - a) * fx) * (1 - fy) + (d + (e - d) * fx) * fy;
    }
    private static float[] Streak(float[] data, int w, int h, double reach, double angle)
    {
        var degrees = (float)(angle * 180 / Math.PI); var side = (int)Math.Ceiling(Math.Sqrt(w * w + h * h));
        using var source = Bitmap(data, w, h);
        using var turned = Bitmaps.Allocate(DeepPixels.Info(side, side).WithAlphaType(SKAlphaType.Premul));
        using (var canvas = new SKCanvas(turned)) { canvas.Clear(SKColors.Black); canvas.Translate(side / 2f, side / 2f); canvas.RotateDegrees(degrees); canvas.DrawBitmap(source, -w / 2f, -h / 2f, new SKSamplingOptions(SKFilterMode.Linear)); }
        using var blurred = Bitmaps.Allocate(turned.Info);
        using (var canvas = new SKCanvas(blurred)) using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur((float)Math.Max(.5, reach), .5f, SKShaderTileMode.Decal) }) canvas.DrawBitmap(turned, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
        using var output = Bitmaps.Allocate(DeepPixels.Info(w, h));
        using (var canvas = new SKCanvas(output)) { canvas.Translate(w / 2f, h / 2f); canvas.RotateDegrees(-degrees); canvas.DrawBitmap(blurred, -side / 2f, -side / 2f, new SKSamplingOptions(SKFilterMode.Linear)); }
        return MemoryMarshal.Cast<byte, float>(output.GetPixelSpan()).ToArray();
    }

    private static float[] Aperture(float[] data, int w, int h, double radius, LensEffectSettings s)
    {
        radius = Math.Max(.5, radius); var result = new float[data.Length]; var rx = (int)Math.Ceiling(radius); var ry = (int)Math.Ceiling(radius * (s.BokehShape == BokehShape.Oval ? 1.62 : 1));
        Parallel.For(0, h, y => { for (var x = 0; x < w; x++) {
            var at = (y * w + x) * 4; double total = 0; var ox = (x - w * .5) / (Math.Max(w, h) * .5); var oy = (y - h * .5) / (Math.Max(w, h) * .5); var outward = Math.Min(1, Math.Sqrt(ox * ox + oy * oy));
            for (var dy = -ry; dy <= ry; dy++) for (var dx = -rx; dx <= rx; dx++) {
                var u = dx / radius; var v = dy / radius; var d = s.BokehShape switch {
                    BokehShape.Hexagon => Math.Max(Math.Abs(u) * .866025 + Math.Abs(v) * .5, Math.Abs(v)) / .866025 * .93,
                    BokehShape.Oval => Math.Sqrt(u * u + v * v * .62 * .62),
                    BokehShape.CatsEye => Math.Max(Math.Sqrt(u * u + v * v), Math.Sqrt(Math.Pow(u + ox * outward * 1.25, 2) + Math.Pow(v + oy * outward * 1.25, 2))), _ => Math.Sqrt(u * u + v * v) };
                var weight = (1 - Smooth((d - (1 - 1 / radius)) * radius)) * (1 + s.BokehRing / 100 * 2.5 * Smooth((d - .6) / .37)); if (weight <= 0) continue;
                var from = (Math.Clamp(y + dy, 0, h - 1) * w + Math.Clamp(x + dx, 0, w - 1)) * 4;
                for (var c = 0; c < 3; c++) result[at + c] += data[from + c] * (float)weight; total += weight;
            }
            for (var c = 0; c < 3; c++) result[at + c] = total > 0 ? result[at + c] / (float)total : data[at + c]; result[at + 3] = 1;
        } }); return result;
    }
}
