using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Local retouch kernels. Coverage already includes brush hardness, opacity and the active selection.</summary>
internal static class RetouchBrush
{
    internal static bool Apply(SKBitmap image, float[] coverage, BrushSettings settings, SKMatrix toDocument, SKPoint start, IReadOnlyList<SKPoint> points, SKBitmap? source = null)
    {
        using var original = Model.Bitmaps.Copy(image);
        var sampled = original.GetPixel(Math.Clamp((int)start.X, 0, image.Width - 1), Math.Clamp((int)start.Y, 0, image.Height - 1));
        var foreground = new SKColor((byte)(settings.Red * 255), (byte)(settings.Green * 255), (byte)(settings.Blue * 255));
        var changed = false;
        var radius = settings.HealingLegacy ? 3 : 1 + Math.Clamp(settings.HealingDiffusion, 1, 7) * 2;
        NeighborhoodMean? targetMeans = null, donorMeans = null;
        if (settings.Mode == BrushMode.HealingSample && settings.HealingBlend != HealingBlend.Replace && source is not null)
        {
            var left = image.Width; var top = image.Height; var right = 0; var bottom = 0;
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
                if (coverage[y * image.Width + x] > 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
            if (right <= left || bottom <= top) return false;
            targetMeans = new NeighborhoodMean(original, new SKRectI(left, top, right, bottom), radius);
            donorMeans = new NeighborhoodMean(source, SKRectI.Create(source.Width, source.Height), radius);
        }
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
        {
            var amount = Math.Clamp(coverage[y * image.Width + x] * settings.Opacity, 0, 1);
            if (amount <= 0) continue;
            var color = original.GetPixel(x, y); var result = color;
            var at = toDocument.MapPoint(x + .5f, y + .5f);
            switch (settings.Mode)
            {
                case BrushMode.HealingSample:
                    if (source is null) return false;
                    var offset = settings.CloneFrom ?? default;
                    var donorAt = settings.CloneMapping?.MapPoint(at) ?? at + new SKPoint(offset.X, offset.Y);
                    var sx = (int)Math.Floor(donorAt.X) - settings.SampleOrigin.X; var sy = (int)Math.Floor(donorAt.Y) - settings.SampleOrigin.Y;
                    if (sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height) continue;
                    var donor = source.GetPixel(sx, sy); if (donor.Alpha == 0) continue;
                    var targetMean = targetMeans?.At(x, y) ?? color; var donorMean = donorMeans?.At(sx, sy) ?? donor;
                    if (settings.HealingContentAware)
                    {
                        var score = Distance(targetMean, donorMean);
                        for (var dy = -2; dy <= 2; dy += 2) for (var dx = -2; dx <= 2; dx += 2)
                        {
                            var nx = sx + dx; var ny = sy + dy; if (nx < 0 || ny < 0 || nx >= source.Width || ny >= source.Height) continue;
                            var candidate = source.GetPixel(nx, ny); if (candidate.Alpha == 0) continue;
                            var mean = donorMeans?.At(nx, ny) ?? candidate; var nextScore = Distance(targetMean, mean) + Math.Abs(dx) + Math.Abs(dy);
                            if (nextScore < score) { score = nextScore; donor = candidate; donorMean = mean; }
                        }
                    }
                    result = settings.HealingBlend == HealingBlend.Replace ? donor : new SKColor(Byte(donor.Red + targetMean.Red - donorMean.Red), Byte(donor.Green + targetMean.Green - donorMean.Green), Byte(donor.Blue + targetMean.Blue - donorMean.Blue), Math.Max(color.Alpha, donor.Alpha));
                    result = BlendHealing(color, result, settings.HealingBlend); break;
                case BrushMode.ColorReplacement:
                    if (Distance(color, sampled) > settings.Tolerance || color.Alpha == 0) continue;
                    color.ToHsv(out var h, out var s, out var v); foreground.ToHsv(out var fh, out var fs, out var fv);
                    result = settings.ReplaceMode switch { 1 => SKColor.FromHsv(fh, s, v, color.Alpha), 2 => SKColor.FromHsv(h, fs, v, color.Alpha), 3 => SKColor.FromHsv(h, s, fv, color.Alpha), _ => SKColor.FromHsv(fh, fs, v, color.Alpha) }; break;
                case BrushMode.Mixer:
                    if (!toDocument.TryInvert(out var back)) continue;
                    var index = 0; var near = float.MaxValue;
                    for (var i = 0; i < points.Count; i++) { var d = SKPoint.Distance(at, points[i]); if (d < near) { near = d; index = i; } }
                    var carryAt = back.MapPoint(points[Math.Max(0, index - 1)]); var carry = original.GetPixel(Math.Clamp((int)carryAt.X, 0, image.Width - 1), Math.Clamp((int)carryAt.Y, 0, image.Height - 1));
                    var wet = Math.Clamp(settings.MixerWet, 0, 1); var mix = Math.Clamp(settings.MixerMix, 0, 1); var load = Math.Clamp(settings.MixerLoad, 0, 1);
                    var loadedWeight = (1 - mix) * load;
                    double Mix(byte current, byte carried, byte loaded) => (current * (1 - wet) + carried * wet) * (1 - loadedWeight) + loaded * loadedWeight;
                    result = new SKColor(Byte(Mix(color.Red, carry.Red, foreground.Red)), Byte(Mix(color.Green, carry.Green, foreground.Green)), Byte(Mix(color.Blue, carry.Blue, foreground.Blue)), Byte(Math.Max(color.Alpha, 255 * load)));
                    amount *= Math.Clamp(settings.MixerFlow, 0, 1) * Math.Max(wet, load); break;
                case BrushMode.Pattern:
                    var scale = Math.Clamp(settings.PatternScale, .01, 100);
                    if (settings.Pattern is { } tile)
                        result = tile.GetPixel(Mod((int)Math.Floor(at.X / scale), tile.Width), Mod((int)Math.Floor(at.Y / scale), tile.Height));
                    else
                    {
                        var dark = (Mod((int)Math.Floor(at.X / (8 * scale)), 2) + Mod((int)Math.Floor(at.Y / (8 * scale)), 2)) % 2 == 0;
                        result = dark ? foreground : SKColors.White;
                    }
                    break;
                case BrushMode.ArtHistory:
                    if (settings.History is not { } history) return false;
                    var cell = Math.Max(2, (int)(settings.Diameter / (settings.ArtStyle == 0 ? 3 : 6)));
                    var hx = (int)Math.Floor(at.X / cell) * cell + cell / 2;
                    var hy = (int)Math.Floor(at.Y / cell) * cell + cell / 2;
                    if (settings.ArtStyle == 1) hx += (int)(Math.Sin(at.Y / cell) * cell / 2);
                    if (settings.ArtStyle == 2) hy += (int)(Math.Cos(at.X / cell) * cell / 2);
                    result = history.GetPixel(Math.Clamp(hx, 0, history.Width - 1), Math.Clamp(hy, 0, history.Height - 1));
                    break;
                case BrushMode.BackgroundErase:
                    var sample = sampled;
                    if (settings.ContinuousSampling && toDocument.TryInvert(out var inverse))
                    {
                        var nearest = points.MinBy(p => SKPoint.Distance(p, at)); var local = inverse.MapPoint(nearest);
                        sample = original.GetPixel(Math.Clamp((int)local.X, 0, image.Width - 1), Math.Clamp((int)local.Y, 0, image.Height - 1));
                    }
                    if (Distance(color, sample) > settings.Tolerance || settings.ProtectForeground && Distance(color, foreground) <= settings.Tolerance) continue;
                    result = color.WithAlpha(0); break;
                case BrushMode.Sharpen:
                    double red = 0, green = 0, blue = 0, weight = 0;
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        var c = original.GetPixel(Math.Clamp(x + dx, 0, image.Width - 1), Math.Clamp(y + dy, 0, image.Height - 1));
                        var a = c.Alpha / 255.0; red += c.Red * a; green += c.Green * a; blue += c.Blue * a; weight += a;
                    }
                    if (weight <= 0) continue;
                    result = new SKColor(Byte(color.Red + (color.Red - red / weight) * settings.Strength * 3),
                        Byte(color.Green + (color.Green - green / weight) * settings.Strength * 3), Byte(color.Blue + (color.Blue - blue / weight) * settings.Strength * 3), color.Alpha); break;
                case BrushMode.Dodge: case BrushMode.Burn:
                    var luminance = (color.Red * .2126 + color.Green * .7152 + color.Blue * .0722) / 255;
                    var tone = settings.ToneRange switch { 0 => Math.Pow(1 - luminance, 2), 2 => luminance * luminance, _ => 4 * luminance * (1 - luminance) };
                    var exponent = Math.Pow(2, settings.Strength * (settings.Mode == BrushMode.Dodge ? -1 : 1) * tone);
                    result = new SKColor(Byte(255 * Math.Pow(color.Red / 255.0, exponent)), Byte(255 * Math.Pow(color.Green / 255.0, exponent)), Byte(255 * Math.Pow(color.Blue / 255.0, exponent)), color.Alpha); break;
                case BrushMode.Sponge:
                    var gray = color.Red * .2126 + color.Green * .7152 + color.Blue * .0722;
                    var saturation = 1 + settings.Strength * (settings.Saturate ? 1 : -1);
                    result = new SKColor(Byte(gray + (color.Red - gray) * saturation), Byte(gray + (color.Green - gray) * saturation), Byte(gray + (color.Blue - gray) * saturation), color.Alpha); break;
            }
            if (result == color) continue;
            image.SetPixel(x, y, Mix(color, result, amount));
            changed = true;
        }
        return changed;
    }
    internal static SKColor Mix(SKColor original, SKColor result, double amount)
    {
        var a1 = original.Alpha * (1 - amount); var a2 = result.Alpha * amount; var alpha = a1 + a2;
        return alpha < .5 ? SKColors.Transparent : new SKColor(Byte((original.Red * a1 + result.Red * a2) / alpha), Byte((original.Green * a1 + result.Green * a2) / alpha), Byte((original.Blue * a1 + result.Blue * a2) / alpha), Byte(alpha));
    }
    internal static SKColor BlendHealing(SKColor backdrop, SKColor source, HealingBlend mode)
    {
        if (mode is HealingBlend.Normal or HealingBlend.Replace) return source;
        var blend = mode switch { HealingBlend.Multiply => PaintBlend.Multiply, HealingBlend.Screen => PaintBlend.Screen, HealingBlend.Darken => PaintBlend.Darken, HealingBlend.Lighten => PaintBlend.Lighten, HealingBlend.Color => PaintBlend.Color, _ => PaintBlend.Luminosity };
        return BrushBlending.Composite(backdrop.WithAlpha(255), source.WithAlpha(255), 1, blend).WithAlpha(source.Alpha);
    }
    private static double Distance(SKColor a, SKColor b) => Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
    private static int Mod(int n, int d) => (n % d + d) % d;
    private static byte Byte(double n) => (byte)Math.Clamp(Math.Round(n), 0, 255);
}
