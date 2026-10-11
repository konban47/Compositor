using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Local retouch kernels. Coverage already includes brush hardness, opacity and the active selection.</summary>
internal static class RetouchBrush
{
    internal static bool Apply(SKBitmap image, float[] coverage, BrushSettings settings, SKMatrix toDocument, SKPoint start, IReadOnlyList<SKPoint> points)
    {
        using var original = image.Copy();
        var sampled = original.GetPixel(Math.Clamp((int)start.X, 0, image.Width - 1), Math.Clamp((int)start.Y, 0, image.Height - 1));
        var foreground = new SKColor((byte)(settings.Red * 255), (byte)(settings.Green * 255), (byte)(settings.Blue * 255));
        var changed = false;
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
        {
            var amount = Math.Clamp(coverage[y * image.Width + x] * settings.Opacity, 0, 1);
            if (amount <= 0) continue;
            var color = original.GetPixel(x, y); var result = color;
            var at = toDocument.MapPoint(x + .5f, y + .5f);
            switch (settings.Mode)
            {
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
            var a1 = color.Alpha * (1 - amount); var a2 = result.Alpha * amount; var alpha = a1 + a2;
            image.SetPixel(x, y, alpha < .5 ? SKColors.Transparent : new SKColor(Byte((color.Red * a1 + result.Red * a2) / alpha),
                Byte((color.Green * a1 + result.Green * a2) / alpha), Byte((color.Blue * a1 + result.Blue * a2) / alpha), Byte(alpha)));
            changed = true;
        }
        return changed;
    }
    private static double Distance(SKColor a, SKColor b) => Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
    private static int Mod(int n, int d) => (n % d + d) % d;
    private static byte Byte(double n) => (byte)Math.Clamp(Math.Round(n), 0, 255);
}
