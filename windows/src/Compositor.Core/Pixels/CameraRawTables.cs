using System.IO.Compression;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Core.Pixels;

/// <summary>Upstream 75147a2's measured 17³ tables composed onto a 33³ grid.</summary>
public static class CameraRawTables
{
    private const int Size = 17, Grid = 33, Cells = 4913, Entries = Cells * 3, Count = 285;
    private static readonly byte[] Tables = Load();
    private readonly record struct Stage(int[] Tables, double[] Weights);
    private static readonly object Gate = new();
    private static string? _key;
    private static float[]? _cached;
    private static byte[] Load()
    {
        using var stream = typeof(CameraRawTables).Assembly.GetManifestResourceStream("Compositor.CameraRawTables.bin") ?? throw new InvalidDataException("Missing Camera Raw tables.");
        Span<byte> header = stackalloc byte[8]; stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("CRT2"u8) || BitConverter.ToUInt16(header[4..6]) != Size || BitConverter.ToUInt16(header[6..]) != Count) throw new InvalidDataException("Invalid Camera Raw tables.");
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
        var planes = new byte[Count * Entries]; deflate.ReadExactly(planes);
        if (deflate.ReadByte() != -1) throw new InvalidDataException("Unexpected Camera Raw table data.");
        var tables = new byte[planes.Length];
        for (var table = 0; table < Count; table++) for (var channel = 0; channel < 3; channel++)
        {
            byte running = 0;
            for (var cell = 0; cell < Cells; cell++)
            {
                running = unchecked((byte)(running + planes[(table * 3 + channel) * Cells + cell]));
                var axis = channel == 0 ? cell / (Size * Size) : channel == 1 ? cell / Size % Size : cell % Size;
                tables[table * Entries + cell * 3 + channel] = unchecked((byte)(running + (byte)Math.Round(axis * 255.0 / 16)));
            }
        }
        return tables;
    }
    private static (int Index, double Fraction) Between(double value, double low, double step, int count)
    {
        var position = Math.Clamp((value - low) / step, 0, count - 1); var index = Math.Min(count - 2, (int)position);
        return (index, position - index);
    }
    private static Stage Slider(int start, double value, double low = -100, double step = 25, int count = 9)
    {
        var (index, fraction) = Between(value, low, step, count);
        return new([start + index, start + index + 1], [1 - fraction, fraction]);
    }
    private static Stage WhiteBalance(double temperature, double tint)
    {
        var (row, down) = Between(temperature, -100, 25, 9); var (column, across) = Between(tint, -100, 25, 9);
        return new([row * 9 + column, row * 9 + column + 1, (row + 1) * 9 + column, (row + 1) * 9 + column + 1], [(1 - down) * (1 - across), (1 - down) * across, down * (1 - across), down * across]);
    }
    private static (double R, double G, double B) StageColor(Stage stage, (double R, double G, double B) input)
    {
        double red = 0, green = 0, blue = 0;
        var rx = Math.Clamp(input.R, 0, 1) * 16; var gx = Math.Clamp(input.G, 0, 1) * 16; var bx = Math.Clamp(input.B, 0, 1) * 16;
        var ri = Math.Min(15, (int)rx); var gi = Math.Min(15, (int)gx); var bi = Math.Min(15, (int)bx);
        var rf = rx - ri; var gf = gx - gi; var bf = bx - bi;
        for (var t = 0; t < stage.Tables.Length; t++) for (var corner = 0; corner < 8; corner++)
        {
            var dr = corner >> 2; var dg = corner >> 1 & 1; var db = corner & 1;
            var weight = stage.Weights[t] * (dr == 1 ? rf : 1 - rf) * (dg == 1 ? gf : 1 - gf) * (db == 1 ? bf : 1 - bf);
            if (weight == 0) continue;
            var at = stage.Tables[t] * Entries + (((ri + dr) * Size + gi + dg) * Size + bi + db) * 3;
            red += weight * Tables[at]; green += weight * Tables[at + 1]; blue += weight * Tables[at + 2];
        }
        return (red / 255, green / 255, blue / 255);
    }
    private static float[]? Compose(List<Stage> stages)
    {
        if (stages.Count == 0) return null;
        var result = new float[Grid * Grid * Grid * 3];
        Parallel.For(0, Grid, r =>
        {
            for (var g = 0; g < Grid; g++) for (var b = 0; b < Grid; b++)
            {
                var color = (R: r / 32.0, G: g / 32.0, B: b / 32.0);
                foreach (var stage in stages) color = StageColor(stage, color);
                var at = ((r * Grid + g) * Grid + b) * 3;
                result[at] = (float)color.R; result[at + 1] = (float)color.G; result[at + 2] = (float)color.B;
            }
        });
        return result;
    }
    public readonly record struct Brightness(double Brightest = 128, double Luminance = 128, double LogBrightest = 128);
    private static double Decode(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
    private static double Encode(double v) => v <= 0 ? 0 : v >= 1 ? 1 : v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
    public static Brightness Statistics(ReadOnlySpan<byte> rgba, int width, int height, int stride)
    {
        double brightest = 0, luminance = 0, logBrightest = 0, weight = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = y * stride + x * 4; double alpha = rgba[p + 3]; if (alpha == 0) continue;
            var red = Decode(Math.Min(1, rgba[p] / alpha)); var green = Decode(Math.Min(1, rgba[p + 1] / alpha)); var blue = Decode(Math.Min(1, rgba[p + 2] / alpha));
            var top = Math.Max(red, Math.Max(green, blue)); var w = alpha / 255;
            brightest += w * top; luminance += w * (.2126 * red + .7152 * green + .0722 * blue);
            logBrightest += w * Math.Log(top + .0003); weight += w;
        }
        return weight <= 0 ? new Brightness(127.5, 127.5, 127.5) : new Brightness(255 * Encode(brightest / weight),
            255 * Encode(luminance / weight), 255 * Encode(Math.Exp(logBrightest / weight) - .0003));
    }
    private static Stage Adaptive(int start, double value, double brightness)
    {
        static (int Index, double Fraction) Find(double v, double[] points)
        {
            v = Math.Clamp(v, points[0], points[^1]); var i = 0;
            while (i < points.Length - 2 && v > points[i + 1]) i++;
            return (i, (v - points[i]) / (points[i + 1] - points[i]));
        }
        var (row, down) = Find(brightness, [53, 80, 104, 130, 160, 190, 224]);
        var (column, across) = Find(value, [-100, -50, -25, 0, 25, 50, 100]); var at = start + row * 7 + column;
        return new Stage([at, at + 1, at + 7, at + 8], [(1 - down) * (1 - across), (1 - down) * across, down * (1 - across), down * across]);
    }
    public static float[]? Compose(CameraRawSettings s, Brightness brightness)
    {
        var key = string.Join("|", s.Exposure, s.Temperature, s.Tint, s.Contrast, s.Highlights, s.Shadows,
            s.Whites, s.Blacks, s.Saturation, s.Vibrance, brightness.Brightest, brightness.Luminance, brightness.LogBrightest);
        lock (Gate)
        {
            if (_key == key) return _cached;
            var stages = new List<Stage>();
            if (s.Exposure != 0) stages.Add(Slider(81, s.Exposure, -5, .5, 21));
            if (s.Temperature != 0 || s.Tint != 0) stages.Add(WhiteBalance(s.Temperature, s.Tint));
            if (s.Contrast != 0) stages.Add(Adaptive(138, s.Contrast, brightness.Brightest));
            if (s.Highlights != 0) stages.Add(Adaptive(187, s.Highlights, brightness.LogBrightest));
            if (s.Shadows != 0) stages.Add(Adaptive(236, s.Shadows, brightness.Luminance));
            if (s.Whites != 0) stages.Add(Slider(102, s.Whites));
            if (s.Blacks != 0) stages.Add(Slider(111, s.Blacks));
            if (s.Saturation != 0) stages.Add(Slider(120, s.Saturation));
            if (s.Vibrance != 0) stages.Add(Slider(129, s.Vibrance));
            _cached = Compose(stages); _key = key; return _cached;
        }
    }
    internal static void Lookup(float[]? table, ref double r, ref double g, ref double b)
    {
        if (table is null) return;
        var rx = Math.Clamp(r, 0, 1) * 32; var gx = Math.Clamp(g, 0, 1) * 32; var bx = Math.Clamp(b, 0, 1) * 32;
        var ri = Math.Min(31, (int)rx); var gi = Math.Min(31, (int)gx); var bi = Math.Min(31, (int)bx);
        var rf = rx - ri; var gf = gx - gi; var bf = bx - bi; r = g = b = 0;
        for (var corner = 0; corner < 8; corner++)
        {
            var dr = corner >> 2; var dg = corner >> 1 & 1; var db = corner & 1;
            var weight = (dr == 1 ? rf : 1 - rf) * (dg == 1 ? gf : 1 - gf) * (db == 1 ? bf : 1 - bf);
            var at = (((ri + dr) * Grid + gi + dg) * Grid + bi + db) * 3;
            r += weight * table[at]; g += weight * table[at + 1]; b += weight * table[at + 2];
        }
    }
    public static (double Temperature, double Tint)? Neutralize(double red, double green, double blue)
    {
        if (Math.Min(red, Math.Min(green, blue)) <= .0001) return null;
        double Cast(double temperature, double tint)
        {
            var (r, g, b) = StageColor(WhiteBalance(temperature, tint), (red, green, blue));
            return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        }
        var best = (Temperature: 0.0, Tint: 0.0, Cast: Cast(0, 0));
        foreach (var (step, reach) in new[] { (10.0, 100.0), (2.0, 10.0), (.5, 2.0) })
        {
            var center = best;
            for (var t = center.Temperature - reach; t <= center.Temperature + reach; t += step)
            for (var i = center.Tint - reach; i <= center.Tint + reach; i += step)
            {
                if (Math.Abs(t) > 100 || Math.Abs(i) > 100) continue;
                var cast = Cast(t, i); if (cast < best.Cast) best = (t, i, cast);
            }
        }
        return (best.Temperature, best.Tint);
    }

    public static (double Temperature, double Tint)? AutoBalance(SKBitmap bitmap)
    {
        static double Decode(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        static double Encode(double value) => value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
        double red = 0, green = 0, blue = 0, count = 0;
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y); if (pixel.Alpha == 0) continue;
            red += Decode(pixel.Red / 255.0); green += Decode(pixel.Green / 255.0); blue += Decode(pixel.Blue / 255.0); count++;
        }
        return count == 0 ? null : Neutralize(Encode(red / count), Encode(green / count), Encode(blue / count));
    }
}

public static partial class AdjustPixels
{
    public static void CameraRawMeasured(Span<byte> rgba, int width, int height, int stride, CameraRawSettings settings)
    {
        var table = CameraRawTables.Compose(settings, CameraRawTables.Statistics(rgba, width, height, stride));
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = y * stride + x * 4; double alpha = rgba[p + 3]; if (alpha == 0) continue;
            double r = rgba[p] / alpha, g = rgba[p + 1] / alpha, b = rgba[p + 2] / alpha;
            CameraRawTables.Lookup(table, ref r, ref g, ref b);
            rgba[p] = (byte)Math.Clamp(Math.Round(r * alpha), 0, alpha); rgba[p + 1] = (byte)Math.Clamp(Math.Round(g * alpha), 0, alpha); rgba[p + 2] = (byte)Math.Clamp(Math.Round(b * alpha), 0, alpha);
        }
    }
}
