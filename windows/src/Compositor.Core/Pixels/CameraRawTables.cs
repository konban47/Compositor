using System.IO.Compression;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Core.Pixels;

/// <summary>Upstream 1.4.9's measured 17³ tables composed onto a 33³ grid.</summary>
public static class CameraRawTables
{
    private const int Size = 17, Grid = 33, Cells = 4913, Entries = Cells * 3, Count = 763;
    private static readonly byte[] Tables = Load();
    private readonly record struct Stage(int[] Tables, double[] Weights);
    private static readonly object Gate = new();
    private static string? _key;
    private static float[]? _cached;
    private static byte[] Load()
    {
        using var stream = typeof(CameraRawTables).Assembly.GetManifestResourceStream("Compositor.CameraRawTables.bin") ?? throw new InvalidDataException("Missing Camera Raw tables.");
        Span<byte> header = stackalloc byte[10]; stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("WRT3"u8) || BitConverter.ToUInt16(header[4..6]) != Size || BitConverter.ToUInt16(header[6..8]) != Count || BitConverter.ToUInt16(header[8..]) != 3) throw new InvalidDataException("Invalid Camera Raw tables.");
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
                tables[table * Entries + cell * 3 + channel] = (byte)Math.Clamp(Math.Round(axis * 255.0 / 16, MidpointRounding.AwayFromZero) + unchecked((sbyte)running) * 3, 0, 255);
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
            if (stage.Tables[t] < 0)
            {
                red += weight * (ri + dr) / 16.0 * 255; green += weight * (gi + dg) / 16.0 * 255; blue += weight * (bi + db) / 16.0 * 255; continue;
            }
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
    public static float[]? Compose(CameraRawSettings s, Brightness brightness, bool foldColor = false)
    {
        var key = System.Text.Json.JsonSerializer.Serialize(s) + $"|{brightness}|{foldColor}";
        lock (Gate)
        {
            if (_key == key) return _cached;
            var stages = new List<Stage>();
            var calibration = new[] { s.RedHue, s.RedSaturation, s.GreenHue, s.GreenSaturation, s.BlueHue, s.BlueSaturation };
            for (var i = 0; i < calibration.Length; i++) if (calibration[i] != 0) stages.Add(Slider(405 + i * 5, calibration[i], -100, 50, 5));
            if (s.Exposure != 0) stages.Add(Slider(81, s.Exposure, -5, .5, 21));
            if (s.Temperature != 0 || s.Tint != 0) stages.Add(WhiteBalance(s.Temperature, s.Tint));
            if (s.Contrast != 0) stages.Add(Adaptive(138, s.Contrast, brightness.Brightest));
            if (s.Highlights != 0) stages.Add(Adaptive(187, s.Highlights, brightness.LogBrightest));
            if (s.Shadows != 0) stages.Add(Adaptive(236, s.Shadows, brightness.Luminance));
            if (s.Whites != 0) stages.Add(Slider(102, s.Whites));
            if (s.Blacks != 0) stages.Add(Slider(111, s.Blacks));
            if (s.Saturation != 0) stages.Add(Slider(120, s.Saturation));
            if (s.Vibrance != 0) stages.Add(Slider(129, s.Vibrance));
            if (s.MeasuredCurve)
            {
                var amounts = new[] { s.CurveShadows, s.CurveDarks, s.CurveLights, s.CurveHighlights };
                for (var i = 0; i < amounts.Length; i++) if (amounts[i] != 0) stages.Add(Slider(435 + i * 5, amounts[i], -100, 50, 5));
            }
            _cached = Compose(stages);
            if (foldColor && (s.AdjustsCurve || s.AdjustsGrading)) _cached = FoldColor(_cached, s);
            _key = key; return _cached;
        }
    }
    private static List<Stage> MixerStages(CameraRawSettings s)
    {
        var stages = new List<Stage>();
        for (var kind = 0; kind < 3; kind++)
        for (var color = 0; color < 8; color++)
        {
            var value = s.Mixer[(2 - kind) * 8 + color];
            if (value != 0) stages.Add(Slider(285 + (kind * 8 + color) * 5, value, -100, 50, 5));
        }
        return stages;
    }
    private static List<Stage> GradingStages(CameraRawSettings s)
    {
        var stages = new List<Stage>();
        var luminance = new[] { s.ShadowLuminance, s.MidtoneLuminance, s.HighlightLuminance, s.GlobalLuminance };
        for (var i = 0; i < 4; i++) if (luminance[i] != 0) stages.Add(Slider(743 + i * 5, luminance[i], -100, 50, 5));
        var balance = s.GradeBalance / 100;
        var highlightShare = Math.Max(0, balance < 0 ? 1 + 1.4 * balance : 1 + 2 * balance);
        var shadowShare = Math.Max(0, balance < 0 ? 1 - 1.2 * balance : 1 - 1.4 * balance);
        var (blendLow, blendUp) = Between(s.GradeBlending, 0, 50, 3);
        foreach (var (wheel, hue, saturation, share, blends) in new[] {
            (3, s.GlobalHue, s.GlobalSaturation, 1.0, false), (2, s.MidtoneHue, s.MidtoneSaturation, 1.0, false),
            (1, s.HighlightHue, s.HighlightSaturation, highlightShare, true), (0, s.ShadowHue, s.ShadowSaturation, shadowShare, true) })
        {
            var sat = Math.Clamp(saturation * share, 0, 100); if (sat <= 0) continue;
            var around = (hue % 360 + 360) % 360 / 30; var first = (int)around; around -= first;
            var level = sat <= 25 ? 0 : sat <= 50 ? 1 : 2;
            var low = level == 0 ? 0 : level == 1 ? 25 : 50; var high = level == 0 ? 25 : level == 1 ? 50 : 100;
            var up = (sat - low) / (high - low);
            var tables = new List<int>(); var weights = new List<double>();
            foreach (var (blend, blendWeight) in blends ? new[] { (blendLow, 1 - blendUp), (blendLow + 1, blendUp) } : new[] { (1, 1.0) })
            foreach (var (h, hueWeight) in new[] { (first, 1 - around), ((first + 1) % 12, around) })
            foreach (var (saturationIndex, weight) in new[] { (level, 1 - up), (level + 1, up) })
            {
                var section = 455 + (blend == 0 ? 0 : blend == 1 ? 72 : 216) + wheel * 36;
                tables.Add(saturationIndex == 0 ? -1 : section + h * 3 + saturationIndex - 1);
                weights.Add(blendWeight * hueWeight * weight);
            }
            stages.Add(new(tables.ToArray(), weights.ToArray()));
        }
        return stages;
    }
    public static float[]? MixerTable(CameraRawSettings s) => Compose(MixerStages(s));
    public static float[]? GradingTable(CameraRawSettings s) => Compose(GradingStages(s));
    private static float[] FoldColor(float[]? source, CameraRawSettings s)
    {
        var result = new float[Grid * Grid * Grid * 3];
        var curves = s.Curves(); var stages = MixerStages(s).Concat(GradingStages(s)).ToArray();
        Parallel.For(0, Grid, r =>
        {
            for (var g = 0; g < Grid; g++) for (var b = 0; b < Grid; b++)
            {
                var at = ((r * Grid + g) * Grid + b) * 3;
                double red = source?[at] ?? r / 32f, green = source?[at + 1] ?? g / 32f, blue = source?[at + 2] ?? b / 32f;
                AdjustPixels.CameraCurve(ref red, ref green, ref blue, curves.Luma, curves.Red, curves.Green, curves.Blue);
                var color = (R: red, G: green, B: blue);
                foreach (var stage in stages) color = StageColor(stage, color);
                result[at] = (float)color.R; result[at + 1] = (float)color.G; result[at + 2] = (float)color.B;
            }
        });
        return result;
    }
    internal static void ExposureAt(ref double r, ref double g, ref double b, double stops)
    {
        (r, g, b) = StageColor(Slider(81, stops, -5, .5, 21), (r, g, b));
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
    public static void CameraRawMeasured(Span<byte> rgba, int width, int height, int stride, CameraRawSettings settings, bool foldColor = false)
    {
        var table = CameraRawTables.Compose(settings, CameraRawTables.Statistics(rgba, width, height, stride), foldColor);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = y * stride + x * 4; double alpha = rgba[p + 3]; if (alpha == 0) continue;
            double r = rgba[p] / alpha, g = rgba[p + 1] / alpha, b = rgba[p + 2] / alpha;
            CameraRawTables.Lookup(table, ref r, ref g, ref b);
            rgba[p] = (byte)Math.Clamp(Math.Round(r * alpha), 0, alpha); rgba[p + 1] = (byte)Math.Clamp(Math.Round(g * alpha), 0, alpha); rgba[p + 2] = (byte)Math.Clamp(Math.Round(b * alpha), 0, alpha);
        }
    }
}
