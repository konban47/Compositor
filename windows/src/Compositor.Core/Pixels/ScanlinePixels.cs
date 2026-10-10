using SkiaSharp;

namespace Compositor.Core.Pixels;

public sealed class ScanlineSettings
{
    public double Spacing { get; set; } = 4;
    public double Thickness { get; set; } = 70;
    public double Dots { get; set; }
    public double Wobble { get; set; }
    public double Displace { get; set; }
    public double Smoothness { get; set; } = 50;
    public double Threshold { get; set; }
    public double Split { get; set; }
    public double Density { get; set; }
    public double Contrast { get; set; }
    public double BlackLevel { get; set; }
    public double Glow { get; set; }
    public bool OriginalColors { get; set; }
    public double DarkRed { get; set; }
    public double DarkGreen { get; set; }
    public double DarkBlue { get; set; }
    public double LightRed { get; set; } = 1;
    public double LightGreen { get; set; } = 1;
    public double LightBlue { get; set; } = 1;
    public ScanlineSettings Copy() => (ScanlineSettings)MemberwiseClone();
    public bool IsValid => Range(Spacing, 2, 32) && Range(Thickness, 5, 100) && Range(Dots, 0, 100)
        && Range(Wobble, 0, 64) && Range(Displace, -100, 100) && Range(Smoothness, 0, 100) && Range(Threshold, 0, 100)
        && Range(Split, 0, 16) && Range(Density, -100, 100) && Range(Contrast, -100, 100) && Range(BlackLevel, 0, 100) && Range(Glow, 0, 100)
        && new[] { DarkRed, DarkGreen, DarkBlue, LightRed, LightGreen, LightBlue }.All(value => Range(value, 0, 1));
    private static bool Range(double value, double low, double high) => double.IsFinite(value) && value >= low && value <= high;
}

/// <summary>Port of upstream 1.4.8's scanlines_apply and draw_lines, including displaced-line occlusion.</summary>
public static class ScanlinePixels
{
    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
    public static void Apply(Span<byte> rgba, int width, int height, int stride, ScanlineSettings p)
    {
        if (!p.IsValid || width <= 0 || height <= 0) return;
        var source = rgba.ToArray(); var spacing = (int)Math.Round(p.Spacing); var lines = (height + spacing - 1) / spacing;
        var plane = lines * width; var planes = p.OriginalColors ? 3 : 1; var scan = new double[plane * planes];
        var gamma = Math.Pow(2, p.Density / 100 * 1.5); var contrast = p.Contrast >= 0 ? 1 / (1 - .95 * p.Contrast / 100) : 1 + p.Contrast / 100;
        double Tone(double value) => Clamp((Math.Pow(Clamp(value), gamma) - .5) * contrast + .5);
        for (var line = 0; line < lines; line++)
        {
            var shift = (int)Math.Round(p.Wobble * (Math.Sin(line * .45) * .7 + Math.Sin(line * 1.7 + 1.3) * .3));
            for (var x = 0; x < width; x++)
            {
                var sx = x - shift; if (sx < 0 || sx >= width) continue;
                double r = 0, g = 0, b = 0; var count = 0;
                for (var y = line * spacing; y < Math.Min(height, (line + 1) * spacing); y++)
                {
                    var at = y * stride + sx * 4; double alpha = source[at + 3]; if (alpha == 0) continue;
                    var red = source[at] / alpha; var green = source[at + 1] / alpha; var blue = source[at + 2] / alpha;
                    if (p.OriginalColors) { r += Tone(red); g += Tone(green); b += Tone(blue); }
                    else r += Tone(.2126 * red + .7152 * green + .0722 * blue);
                    count++;
                }
                if (count == 0) continue;
                var cell = line * width + x; scan[cell] = r / count;
                if (p.OriginalColors) { scan[plane + cell] = g / count; scan[2 * plane + cell] = b / count; }
            }
        }
        var tone = new double[plane];
        for (var i = 0; i < plane; i++) tone[i] = p.OriginalColors ? .2126 * scan[i] + .7152 * scan[plane + i] + .0722 * scan[2 * plane + i] : scan[i];
        var lift = (double[])tone.Clone(); var radius = (int)Math.Round(p.Smoothness / 100 * spacing * 2);
        if (radius > 0 && p.Displace != 0)
        {
            var copy = new double[width];
            for (var line = 0; line < lines; line++) for (var pass = 0; pass < 3; pass++)
            {
                Array.Copy(lift, line * width, copy, 0, width); double sum = 0;
                for (var x = -radius; x <= radius; x++) sum += copy[Math.Clamp(x, 0, width - 1)];
                for (var x = 0; x < width; x++)
                {
                    lift[line * width + x] = sum / (2 * radius + 1);
                    sum += copy[Math.Clamp(x + radius + 1, 0, width - 1)] - copy[Math.Clamp(x - radius, 0, width - 1)];
                }
            }
            for (var pass = 0; pass < (int)Math.Round(p.Smoothness / 100 * 2); pass++)
            {
                var old = (double[])lift.Clone();
                for (var line = 0; line < lines; line++) for (var x = 0; x < width; x++)
                    lift[line * width + x] = (old[Math.Max(0, line - 1) * width + x] + 2 * old[line * width + x] + old[Math.Min(lines - 1, line + 1) * width + x]) / 4;
            }
        }
        var dark = p.OriginalColors ? new double[3] : new[] { p.DarkRed, p.DarkGreen, p.DarkBlue };
        var light = new[] { p.LightRed, p.LightGreen, p.LightBlue }; var rising = p.Displace >= 0; var middle = spacing / 2.0;
        var cover = new double[height]; var colors = new double[height * 3];
        for (var x = 0; x < width; x++)
        {
            Array.Clear(cover); Array.Clear(colors); var horizon = rising ? double.PositiveInfinity : double.NegativeInfinity;
            var along = (x + .5) % spacing - middle;
            for (var step = 0; step < lines; step++)
            {
                var line = rising ? lines - step - 1 : step;
                var across = along * (p.Dots > 0 ? Clamp((p.Dots / 100 - tone[line * width + x]) / .2) : 0);
                var at = Math.Clamp((int)Math.Round(x - across), 0, width - 1); var i = line * width + at; var t = tone[i];
                var lit = p.Threshold > 0 ? Clamp((t - p.Threshold / 100) / .04) : 1; if (lit <= 0) continue;
                var baseY = line * spacing + middle; var here = baseY - p.Displace * lift[i]; var before = at > 0 ? baseY - p.Displace * lift[i - 1] : here;
                var lo = Math.Min(here, before); var hi = Math.Max(here, before); var level = p.BlackLevel / 100 + (1 - p.BlackLevel / 100) * t;
                var beam = middle * p.Thickness / 100 * (.29 + .71 * Math.Sqrt(Clamp(level)));
                var top = Math.Max(0, (int)Math.Floor(rising ? lo - beam - 1 : Math.Max(lo - beam - 1, horizon - 1)));
                var bottom = Math.Min(height, (int)Math.Ceiling(rising ? Math.Min(hi + beam + 1, horizon + 1) : hi + beam + 1));
                for (var y = top; y < bottom; y++)
                {
                    var yy = y + .5; var off = yy < lo ? lo - yy : yy > hi ? yy - hi : 0;
                    var shown = Clamp(beam - Math.Sqrt(off * off + across * across) + .5) * lit * (rising ? Clamp(horizon - yy + .5) : Clamp(yy - horizon + .5));
                    if (shown <= cover[y]) continue; cover[y] = shown;
                    for (var c = 0; c < 3; c++) colors[y * 3 + c] = p.OriginalColors ? p.BlackLevel / 100 + (1 - p.BlackLevel / 100) * scan[c * plane + i] : dark[c] + (light[c] - dark[c]) * level;
                }
                if (lit > .5) horizon = rising ? Math.Min(horizon, lo - beam) : Math.Max(horizon, hi + beam);
            }
            for (var y = 0; y < height; y++)
            {
                var at = y * stride + x * 4; var alpha = rgba[at + 3];
                for (var c = 0; c < 3; c++) rgba[at + c] = (byte)Math.Round(Clamp(dark[c] + (colors[y * 3 + c] * 1.35 - dark[c]) * cover[y]) * alpha);
            }
        }
        var split = (int)Math.Round(p.Split);
        if (split > 0)
        {
            var copy = rgba.ToArray();
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var at = y * stride + x * 4;
                rgba[at] = (byte)Math.Min(rgba[at + 3], copy[y * stride + Math.Max(0, x - split) * 4]);
                rgba[at + 2] = (byte)Math.Min(rgba[at + 3], copy[y * stride + Math.Min(width - 1, x + split) * 4 + 2]);
            }
        }
        if (p.Glow > 0)
        {
            var glow = rgba.ToArray(); GaussianBlur.Clamped(glow, width, height, 4, stride, spacing * 3 + 3);
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var c = 0; c < 3; c++)
            {
                var at = y * stride + x * 4; double alpha = rgba[at + 3], value = rgba[at + c], room = (alpha - value) * .7;
                if (room > 0) value += room * (1 - Math.Exp(-glow[at + c] * p.Glow / 100 * 2.5 * alpha / 255 / room));
                rgba[at + c] = (byte)Math.Round(Math.Clamp(value, 0, alpha));
            }
        }
    }
}
