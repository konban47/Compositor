namespace Compositor.Core.Pixels;

/// <summary>Port of upstream CameraRawShadows / adjust_camera_raw_shadows (735c2cc).
/// The fitted region/texture response runs between Highlights and Whites, outside the colour LUT.</summary>
public static class CameraRawShadows
{
    private sealed record Tables(float NormalFrom, int XCount, int KCount, float XLow, float XStep, float KLow, float KStep, float[] Amounts, float[][][] Coefficients);
    private static readonly Lazy<Tables> Data = new(ReadTables);
    private static Tables ReadTables()
    {
        using var stream = typeof(CameraRawShadows).Assembly.GetManifestResourceStream("Compositor.CameraRawShadows.bin") ?? throw new InvalidDataException("Missing Camera Raw Shadows tables.");
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "CRS2") throw new InvalidDataException("Invalid Camera Raw Shadows tables.");
        int sets = reader.ReadInt16(), count = reader.ReadInt16(), xs = reader.ReadInt16(), ks = reader.ReadInt16(), terms = reader.ReadInt16();
        if (sets != 2 || count < 1 || xs < 2 || ks < 1 || terms != 3 || stream.Length != 34L + count * 4L + sets * count * xs * ks * terms * 4L) throw new InvalidDataException("Invalid Camera Raw Shadows dimensions.");
        float normal = reader.ReadSingle(), xLow = reader.ReadSingle(), xStep = reader.ReadSingle(), kLow = reader.ReadSingle(), kStep = reader.ReadSingle();
        float[] Read(int length) => Enumerable.Range(0, length).Select(_ => reader.ReadSingle()).ToArray();
        var amounts = Read(count);
        var coefficients = Enumerable.Range(0, sets).Select(_ => Enumerable.Range(0, count).Select(_ => Read(xs * ks * terms)).ToArray()).ToArray();
        return new(normal, xs, ks, xLow, xStep, kLow, kStep, amounts, coefficients);
    }
    private static float[] Coefficients(double amount, int set, Tables table)
    {
        var points = table.Amounts.Select((v, i) => (Value: (double)v, Data: (float[]?)table.Coefficients[set][i])).Append((Value: 0d, Data: (float[]?)null)).OrderBy(p => p.Value).ToArray();
        amount = Math.Clamp(amount, points[0].Value, points[^1].Value);
        var index = 0; while (index < points.Length - 2 && amount > points[index + 1].Value) index++;
        var low = points[index]; var high = points[index + 1]; var t = (float)((amount - low.Value) / (high.Value - low.Value));
        var result = new float[table.XCount * table.KCount * 3];
        for (var i = 0; i < result.Length; i++) result[i] = (low.Data?[i] ?? 0) * (1 - t) + (high.Data?[i] ?? 0) * t;
        return result;
    }
    private static double Decode(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
    private static double Encode(double v) => v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
    private static double Luminance(ReadOnlySpan<byte> rgba, int p)
    {
        double alpha = rgba[p + 3]; if (alpha == 0) return 0;
        return .2126 * Decode(Math.Min(1, rgba[p] / alpha)) + .7152 * Decode(Math.Min(1, rgba[p + 1] / alpha)) + .0722 * Decode(Math.Min(1, rgba[p + 2] / alpha));
    }
    private static void BoxMean(float[] src, float[] dst, int width, int height, int radius, float[] temp)
    {
        for (var y = 0; y < height; y++)
        {
            double sum = 0; int lo = 0, hi = 0;
            for (var x = 0; x < width; x++)
            {
                var wantLo = Math.Max(0, x - radius); var wantHi = Math.Min(width, x + radius + 1);
                while (hi < wantHi) sum += src[y * width + hi++];
                while (lo < wantLo) sum -= src[y * width + lo++];
                temp[y * width + x] = (float)(sum / (hi - lo));
            }
        }
        for (var x = 0; x < width; x++)
        {
            double sum = 0; int lo = 0, hi = 0;
            for (var y = 0; y < height; y++)
            {
                var wantLo = Math.Max(0, y - radius); var wantHi = Math.Min(height, y + radius + 1);
                while (hi < wantHi) sum += temp[hi++ * width + x];
                while (lo < wantLo) sum -= temp[lo++ * width + x];
                dst[y * width + x] = (float)(sum / (hi - lo));
            }
        }
    }
    /// <param name="pixelScale">Pixels in this buffer per original layer pixel; previews select the original layer's coefficient set.</param>
    public static void Apply(Span<byte> rgba, int width, int height, int stride, double amount, double pixelScale = 1)
    {
        if (amount == 0 || width <= 0 || height <= 0) return;
        var table = Data.Value; var coefficients = Coefficients(amount, Math.Max(width, height) / (pixelScale > 0 ? pixelScale : 1) >= table.NormalFrom ? 1 : 0, table);
        const double floor = 1d / 8192;
        double sum = 0, count = 0, all = 0, allCount = 0;
        var scale = Math.Max(1, Math.Min(width, height) / 384); var lw = (width + scale - 1) / scale; var lh = (height + scale - 1) / scale; var cells = checked(lw * lh);
        var low = new float[cells]; var weight = new float[cells];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = y * stride + x * 4; if (rgba[p + 3] == 0) continue;
            var luminance = Luminance(rgba, p); all += luminance; allCount++;
            var v = rgba[p + 1];
            if (x > 0 && rgba[p - 3] != v || x + 1 < width && rgba[p + 5] != v || y > 0 && rgba[p - stride + 1] != v || y + 1 < height && rgba[p + stride + 1] != v) { sum += luminance; count++; }
            var cell = y / scale * lw + x / scale; low[cell] += (float)Math.Log2(Math.Max(floor, luminance)); weight[cell]++;
        }
        var key = Math.Log2(Math.Max(floor, count > 0 ? sum / count : allCount > 0 ? all / allCount : .18));
        for (var i = 0; i < cells; i++) low[i] = weight[i] > 0 ? low[i] / weight[i] : (float)key;
        var mean = new float[cells]; var square = new float[cells]; var a = new float[cells]; var b = new float[cells]; var temp = new float[cells];
        var radius = Math.Max(1, (int)Math.Round(.25 * Math.Min(lw, lh), MidpointRounding.AwayFromZero));
        BoxMean(low, mean, lw, lh, radius, temp);
        for (var i = 0; i < cells; i++) a[i] = low[i] * low[i];
        BoxMean(a, square, lw, lh, radius, temp);
        for (var i = 0; i < cells; i++) { var variance = Math.Max(0, square[i] - mean[i] * mean[i]); a[i] = variance / (variance + 1.5f); b[i] = mean[i] - a[i] * mean[i]; }
        BoxMean(a, mean, lw, lh, radius, temp); BoxMean(b, square, lw, lh, radius, temp);
        var kp = Math.Clamp((key - table.KLow) / table.KStep, 0, table.KCount - 1); var k0 = (int)kp; var k1 = Math.Min(k0 + 1, table.KCount - 1); var kt = kp - k0;
        var plane = table.XCount * table.KCount;
        Span<double> term = stackalloc double[3];
        for (var y = 0; y < height; y++)
        {
            var fy = Math.Clamp((y + .5) / scale - .5, 0, lh - 1); var y0 = (int)fy; var y1 = Math.Min(y0 + 1, lh - 1); var ty = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var p = y * stride + x * 4; double alpha = rgba[p + 3]; if (alpha == 0) continue;
                var luminance = Luminance(rgba, p); if (luminance <= floor) continue;
                var fx = Math.Clamp((x + .5) / scale - .5, 0, lw - 1); var x0 = (int)fx; var x1 = Math.Min(x0 + 1, lw - 1); var tx = fx - x0;
                double Sample(float[] values) => (values[y0 * lw + x0] * (1 - tx) + values[y0 * lw + x1] * tx) * (1 - ty) + (values[y1 * lw + x0] * (1 - tx) + values[y1 * lw + x1] * tx) * ty;
                var level = Math.Log2(luminance); var region = Sample(mean) * level + Sample(square); var detail = level - region;
                var xp = Math.Clamp((region - key - table.XLow) / table.XStep, 0, table.XCount - 1); var xc = Math.Min((int)xp, table.XCount - 2); var xt = xp - xc;
                for (var t = 0; t < 3; t++)
                {
                    var at = t * plane;
                    var row0 = coefficients[at + k0 * table.XCount + xc] * (1 - xt) + coefficients[at + k0 * table.XCount + xc + 1] * xt;
                    var row1 = coefficients[at + k1 * table.XCount + xc] * (1 - xt) + coefficients[at + k1 * table.XCount + xc + 1] * xt;
                    term[t] = row0 * (1 - kt) + row1 * kt;
                }
                var gain = Math.Pow(2, term[0] + term[1] * Math.Max(detail, 0) + term[2] * Math.Min(detail, 0));
                for (var c = 0; c < 3; c++) rgba[p + c] = (byte)Math.Clamp(Math.Round(Encode(Decode(Math.Min(1, rgba[p + c] / alpha)) * gain) * alpha), 0, alpha);
            }
        }
    }
}
