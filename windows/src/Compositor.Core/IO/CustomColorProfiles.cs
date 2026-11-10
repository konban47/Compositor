using System.Buffers.Binary;
using System.Text;

namespace Compositor.Core.IO;

public enum SeparationType { Gcr, Ucr }
public enum BlackGeneration { None, Light, Medium, Heavy, Maximum, Custom }

/// <summary>A set of process inks as printed: CIE Lab of each ink, each two-ink overprint, all three, the paper and black.</summary>
public sealed record InkSet(string Name, double[] Lab)
{
    /// <summary>The order <see cref="Lab"/> holds its nine colors in, three numbers each.</summary>
    public static readonly string[] Colors = ["C", "M", "Y", "MY", "CY", "CM", "CMY", "W", "K"];
    public bool IsValid => Lab is { Length: 27 } && Lab.All(double.IsFinite) && Enumerable.Range(0, 9).All(i => Lab[i * 3] is >= 0 and <= 100 && Math.Abs(Lab[i * 3 + 1]) <= 128 && Math.Abs(Lab[i * 3 + 2]) <= 128);
}

/// <summary>What Custom CMYK builds a press profile from: the inks, how much the dots grow, and how black replaces color.</summary>
public sealed record CustomCmykSettings
{
    public string Name { get; init; } = "SWOP (Coated), 20%, GCR, Medium";
    public InkSet Inks { get; init; } = CustomColorProfiles.InkSets[5];
    /// <summary>Dot gain at a 50% dot, in percent; ignored when <see cref="GainCurve"/> is set.</summary>
    public double DotGain { get; init; } = 20;
    /// <summary>Printed dot at 2, 4, 6, 8, 10, 20 … 90% when the gain is given as a curve, blank points left as NaN.</summary>
    public double[]? GainCurve { get; init; }
    public SeparationType Separation { get; init; } = SeparationType.Gcr;
    public BlackGeneration Black { get; init; } = BlackGeneration.Medium;
    /// <summary>Where a custom black generation starts, as a fraction of the gray component, and how much black it reaches.</summary>
    public double BlackStart { get; init; } = .3;
    public double BlackAmount { get; init; } = 1;
    public double BlackLimit { get; init; } = 100;
    public double TotalLimit { get; init; } = 300;
    public double UndercolorAddition { get; init; }
    public bool IsValid => Name is { Length: > 0 and <= 200 } && Inks.IsValid && double.IsFinite(DotGain) && DotGain is >= -10 and <= 40
        && (GainCurve is null || GainCurve.Length == 13 && GainCurve.All(v => double.IsNaN(v) || v is >= 0 and <= 100))
        && Enum.IsDefined(Separation) && Enum.IsDefined(Black) && BlackStart is >= 0 and < 1 && BlackAmount is >= 0 and <= 1
        && BlackLimit is >= 0 and <= 100 && TotalLimit is >= 200 and <= 400 && UndercolorAddition is >= 0 and <= 100;
    /// <summary>The name Custom CMYK gives these settings until it is typed over.</summary>
    public string DefaultName => $"{Inks.Name}, {(GainCurve is null ? DotGain.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "Curves")}, {(Separation == SeparationType.Gcr ? "GCR" : "UCR")}, {Black}";
}

/// <summary>
/// Edit ▸ Color Settings ▸ Custom RGB and Custom CMYK: ICC profiles built from a handful of numbers. An RGB profile is a
/// gamma and a set of primaries around a white; a CMYK one models the inks on paper (Yule-Nielsen weighted overprints, the
/// dots grown by the press) and inverts that model with the black generation, ink limits and undercolor addition asked for.
/// </summary>
public static class CustomColorProfiles
{
    private static double[] Lab(params double[] values) => values;
    /// <summary>Approximate measured ink sets, coated stocks the most saturated and newsprint the least.</summary>
    public static readonly InkSet[] InkSets =
    [
        new("AD-LITHO (Newsprint)", Lab(57, -24, -35, 54, 46, 1, 81, -3, 62, 51, 41, 22, 50, -35, 12, 35, 8, -24, 36, 1, 2, 83, 0, 6, 34, 1, 3)),
        new("Dainippon Ink", Lab(56, -36, -48, 49, 72, -2, 88, -6, 89, 48, 66, 44, 50, -62, 25, 25, 22, -45, 23, 1, 0, 95, 0, -2, 18, 0, 1)),
        new("Eurostandard (Coated)", Lab(55, -38, -46, 48, 74, -3, 89, -5, 93, 47, 68, 48, 50, -65, 27, 24, 22, -46, 23, 0, 0, 95, 0, -2, 16, 0, 0)),
        new("Eurostandard (Newsprint)", Lab(57, -23, -32, 53, 44, 2, 80, -3, 58, 51, 40, 21, 51, -33, 11, 36, 7, -22, 37, 1, 2, 82, 0, 6, 35, 1, 3)),
        new("Eurostandard (Uncoated)", Lab(60, -26, -42, 56, 61, -1, 89, -4, 78, 54, 55, 35, 54, -48, 15, 39, 13, -33, 32, 1, 1, 95, 0, -2, 31, 1, 1)),
        new("SWOP (Coated)", Lab(55, -37, -50, 48, 74, -3, 89, -5, 93, 47, 68, 48, 50, -65, 27, 24, 22, -46, 23, 0, 0, 93, 0, -2, 16, 0, 0)),
        new("SWOP (Newsprint)", Lab(57, -24, -34, 54, 45, 1, 80, -3, 60, 51, 40, 21, 51, -34, 11, 36, 8, -23, 37, 1, 2, 83, 0, 6, 35, 1, 3)),
        new("SWOP (Uncoated)", Lab(60, -27, -44, 56, 62, -1, 88, -4, 80, 54, 56, 36, 54, -49, 16, 39, 14, -34, 32, 1, 1, 94, 0, -2, 30, 1, 1)),
        new("Toyo Inks (Coated Web Offset)", Lab(56, -37, -47, 49, 73, -3, 89, -5, 90, 48, 67, 46, 51, -63, 26, 25, 22, -45, 24, 0, 0, 93, 0, -1, 18, 0, 0)),
        new("Toyo Inks (Coated)", Lab(55, -38, -49, 48, 75, -3, 89, -5, 94, 47, 69, 49, 50, -66, 27, 24, 23, -47, 22, 0, 0, 95, 0, -2, 15, 0, 0)),
        new("Toyo Inks (Dull Coated)", Lab(57, -34, -46, 50, 70, -2, 89, -5, 88, 49, 64, 44, 52, -59, 23, 28, 20, -42, 26, 1, 1, 94, 0, -2, 21, 0, 0)),
        new("Toyo Inks (Uncoated)", Lab(60, -27, -43, 56, 61, -1, 88, -4, 79, 54, 55, 35, 54, -48, 15, 39, 13, -33, 32, 1, 1, 94, 0, -2, 30, 1, 1)),
    ];
    public static readonly (string Name, double X, double Y)[] WhitePoints =
    [
        ("5000° K (D50)", .3457, .3585), ("5500° K (D55)", .3324, .3474), ("6500° K (D65)", .3127, .3290), ("7500° K (D75)", .2990, .3149),
        ("9300° K", .2848, .2932), ("Standard Illuminant A", .4476, .4074), ("Standard Illuminant B", .3484, .3516), ("Standard Illuminant C", .3101, .3162), ("Standard Illuminant E", 1 / 3d, 1 / 3d),
    ];
    public static readonly (string Name, double[] XY)[] Primaries =
    [
        ("700/525/450nm", [.7347, .2653, .1152, .8264, .1566, .0177]), ("Adobe RGB (1998)", [.64, .33, .21, .71, .15, .06]), ("CIE RGB", [.7347, .2653, .2738, .7174, .1666, .0089]),
        ("EBU/ITU", [.64, .33, .29, .60, .15, .06]), ("HDTV (ITU-R 709-2)", [.64, .33, .30, .60, .15, .06]), ("NTSC (1953)", [.67, .33, .21, .71, .14, .08]),
        ("P22-EBU", [.625, .34, .28, .595, .155, .07]), ("SMPTE-C (ITU-R 601-5)", [.63, .34, .31, .595, .155, .07]), ("Trinitron", [.625, .34, .28, .595, .155, .07]),
    ];
    private static readonly double[] D50 = [.9642, 1, .8249];
    /// <summary>The dot sizes a custom dot gain curve is given at.</summary>
    public static readonly double[] GainKnots = [2, 4, 6, 8, 10, 20, 30, 40, 50, 60, 70, 80, 90];

    /// <summary>A matrix/TRC display profile with the given gamma, white point and primaries, adapted to the D50 connection space.</summary>
    public static byte[] Rgb(string name, double gamma, double whiteX, double whiteY, IReadOnlyList<double> primaries)
    {
        if (!double.IsFinite(gamma) || gamma is < .75 or > 3 || primaries.Count != 6 || !new[] { whiteX, whiteY }.Concat(primaries).All(v => double.IsFinite(v) && v is > 0 and < 1)) throw new ArgumentException("Invalid custom RGB.");
        double[] Chroma(double x, double y) => [x / y, 1, (1 - x - y) / y];
        var white = Chroma(whiteX, whiteY); var r = Chroma(primaries[0], primaries[1]); var g = Chroma(primaries[2], primaries[3]); var b = Chroma(primaries[4], primaries[5]);
        double[] columns = [r[0], g[0], b[0], r[1], g[1], b[1], r[2], g[2], b[2]];
        var scale = Multiply(Invert(columns), white);
        if (scale.Any(s => !double.IsFinite(s) || s <= 0)) throw new ArgumentException("The primaries do not enclose the white point.");
        var adapt = Bradford(white, D50);
        byte[] Colorant(double[] primary, double s) => Xyz(Multiply(adapt, [primary[0] * s, primary[1] * s, primary[2] * s]));
        var curve = new byte[14]; Ascii(curve, 0, "curv"); BinaryPrimitives.WriteUInt32BigEndian(curve.AsSpan(8), 1); BinaryPrimitives.WriteUInt16BigEndian(curve.AsSpan(12), (ushort)Math.Round(gamma * 256));
        return Write("mntr", "RGB ", "XYZ ", [("desc", Description(name)), ("wtpt", Xyz(D50)), ("rXYZ", Colorant(r, scale[0])), ("gXYZ", Colorant(g, scale[1])), ("bXYZ", Colorant(b, scale[2])), ("rTRC", curve), ("gTRC", curve), ("bTRC", curve), ("cprt", Copyright())]);
    }

    /// <summary>A press profile: CMYK to Lab through the ink model, and Lab to CMYK through its inverse with the separation asked for.</summary>
    public static byte[] Cmyk(CustomCmykSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentException("Invalid custom CMYK.");
        var press = new Press(settings);
        const int forwardGrid = 9, backwardGrid = 33;
        var forward = new ushort[(int)Math.Pow(forwardGrid, 4) * 3]; var at = 0;
        for (var c = 0; c < forwardGrid; c++) for (var m = 0; m < forwardGrid; m++) for (var y = 0; y < forwardGrid; y++) for (var k = 0; k < forwardGrid; k++)
        {
            var lab = press.Lab(c / 8d, m / 8d, y / 8d, k / 8d); EncodeLab(lab, forward, at); at += 3;
        }
        var backward = new ushort[backwardGrid * backwardGrid * backwardGrid * 4]; at = 0;
        double[] guess = [0, 0, 0];
        for (var l = 0; l < backwardGrid; l++) for (var a = 0; a < backwardGrid; a++) for (var b = 0; b < backwardGrid; b++)
        {
            var lab = DecodeLab(l * 65535d / (backwardGrid - 1), a * 65535d / (backwardGrid - 1), b * 65535d / (backwardGrid - 1));
            var ink = press.Separate(lab, guess); guess = [ink[0], ink[1], ink[2]];
            for (var i = 0; i < 4; i++) backward[at++] = (ushort)Math.Round(Math.Clamp(ink[i], 0, 1) * 65535);
        }
        var a2b = Lut(4, 3, forwardGrid, forward); var b2a = Lut(3, 4, backwardGrid, backward);
        return Write("prtr", "CMYK", "Lab ", [("desc", Description(settings.Name)), ("wtpt", Xyz(D50)), ("A2B0", a2b), ("A2B1", a2b), ("A2B2", a2b), ("B2A0", b2a), ("B2A1", b2a), ("B2A2", b2a), ("cprt", Copyright())]);
    }

    /// <summary>The C, M, Y and K a neutral ramp from white to black separates into, for the Gray Ramp graph.</summary>
    public static double[][] GrayRamp(CustomCmykSettings settings, int steps = 33)
    {
        var press = new Press(settings); double[] guess = [0, 0, 0];
        var ramp = new double[steps][];
        for (var i = 0; i < steps; i++) { var ink = press.Separate([100 - 100d * i / (steps - 1), 0, 0], guess); guess = [ink[0], ink[1], ink[2]]; ramp[i] = ink; }
        return ramp;
    }

    /// <summary>The printed dot for a film dot, by the single 50% gain or by the curve's known points.</summary>
    public static double Gain(CustomCmykSettings settings, double dot)
    {
        dot = Math.Clamp(dot, 0, 1);
        if (settings.GainCurve is not { } curve) return Math.Clamp(dot + settings.DotGain / 100 * 4 * dot * (1 - dot), 0, 1);
        var xs = new List<double> { 0 }; var ys = new List<double> { 0 };
        for (var i = 0; i < curve.Length; i++) if (!double.IsNaN(curve[i])) { xs.Add(GainKnots[i] / 100); ys.Add(curve[i] / 100); }
        xs.Add(1); ys.Add(1);
        var j = 0; while (j < xs.Count - 2 && dot > xs[j + 1]) j++;
        var t = (dot - xs[j]) / Math.Max(1e-9, xs[j + 1] - xs[j]);
        return Math.Clamp(ys[j] + (ys[j + 1] - ys[j]) * t, 0, 1);
    }

    private sealed class Press
    {
        private readonly CustomCmykSettings _settings;
        private readonly double[][] _roots = new double[9][];
        private const double N = 2;
        /// <summary>The lightness black ink alone prints at, 0 to 100% in 256 steps, filled in as it is asked for.</summary>
        private readonly double[] _blackLightness = new double[257];
        internal Press(CustomCmykSettings settings)
        {
            _settings = settings;
            for (var i = 0; i < _blackLightness.Length; i++) _blackLightness[i] = double.NaN;
            for (var i = 0; i < 9; i++) _roots[i] = LabToXyz([settings.Inks.Lab[i * 3], settings.Inks.Lab[i * 3 + 1], settings.Inks.Lab[i * 3 + 2]]).Select(v => Math.Pow(Math.Max(v, 1e-6), 1 / N)).ToArray();
        }
        internal double[] Lab(double c, double m, double y, double k) => XyzToLab(Xyz(c, m, y, k));
        private double[] Xyz(double c, double m, double y, double k)
        {
            c = Gain(_settings, c); m = Gain(_settings, m); y = Gain(_settings, y); k = Gain(_settings, k);
            // Demichel weights of white, C, M, Y, MY, CY, CM and CMY, in the order the ink set lists them.
            double[] weights = [c * (1 - m) * (1 - y), (1 - c) * m * (1 - y), (1 - c) * (1 - m) * y, (1 - c) * m * y, c * (1 - m) * y, c * m * (1 - y), c * m * y, (1 - c) * (1 - m) * (1 - y)];
            var result = new double[3];
            for (var channel = 0; channel < 3; channel++)
            {
                var sum = 0d; for (var i = 0; i < 8; i++) sum += weights[i] * _roots[i][channel];
                var paper = _roots[7][channel]; var black = (1 - k) + k * _roots[8][channel] / paper;
                result[channel] = Math.Pow(sum * black, N);
            }
            return result;
        }
        /// <summary>The inks for a Lab color: the three colors that match it, black generated from their gray, the colors matched again around that black, then the limits.</summary>
        internal double[] Separate(double[] lab, double[] guess)
        {
            var cmy = Solve(lab, guess, 0);
            var gray = Math.Min(cmy[0], Math.Min(cmy[1], cmy[2])); var most = Math.Max(cmy[0], Math.Max(cmy[1], cmy[2]));
            // Black follows how dark the color is, scaled by how neutral it is (a saturated color has no gray to replace), and never
            // goes past the black that alone would print this light. Three inks alone stop near L* 23, so their own gray component
            // saturates far too early to be the measure.
            var neutral = most <= 1e-6 ? 1 : gray / most; var darkness = Math.Clamp(1 - lab[0] / 100, 0, 1);
            var k = Math.Min(BlackFor(darkness) * neutral, BlackMatching(lab[0]));
            if (_settings.Separation == SeparationType.Ucr) k = Math.Min(BlackMatching(lab[0]), Math.Pow(Math.Clamp((darkness - .5) / .45, 0, 1), 1.2) * neutral * neutral);
            k = Math.Min(k, _settings.BlackLimit / 100);
            if (k > 0) cmy = Solve(lab, cmy, k);
            if (_settings.UndercolorAddition > 0 && _settings.Separation == SeparationType.Gcr)
            {
                var shadow = Math.Clamp((k - .5) / .5, 0, 1) * _settings.UndercolorAddition / 100;
                for (var i = 0; i < 3; i++) cmy[i] = Math.Min(1, cmy[i] + shadow * gray * .5);
            }
            var total = cmy[0] + cmy[1] + cmy[2]; var room = _settings.TotalLimit / 100 - k;
            if (total > room && total > 0) { var factor = Math.Max(0, room) / total; for (var i = 0; i < 3; i++) cmy[i] *= factor; }
            return [cmy[0], cmy[1], cmy[2], k];
        }
        private double BlackMatching(double lightness)
        {
            for (var i = 0; i < 257; i++)
            {
                if (double.IsNaN(_blackLightness[i])) _blackLightness[i] = Lab(0, 0, 0, i / 256d)[0];
                if (_blackLightness[i] <= lightness) return i == 0 ? 0 : (i - 1 + (_blackLightness[i - 1] - lightness) / Math.Max(1e-9, _blackLightness[i - 1] - _blackLightness[i])) / 256;
            }
            return 1;
        }
        private double BlackFor(double gray)
        {
            var (start, amount, power) = _settings.Black switch
            {
                BlackGeneration.None => (1d, 0d, 1d), BlackGeneration.Light => (.5, .85, 1.4), BlackGeneration.Medium => (.3, .95, 1.3),
                BlackGeneration.Heavy => (.12, 1d, 1.15), BlackGeneration.Maximum => (0d, 1d, 1d), _ => (_settings.BlackStart, _settings.BlackAmount, 1.2),
            };
            if (gray <= start || amount <= 0) return 0;
            return Math.Clamp(amount * Math.Pow((gray - start) / (1 - start), power), 0, 1);
        }
        /// <summary>Damped Newton steps on C, M and Y, with black held, toward the Lab color; clamped to printable dots.</summary>
        private double[] Solve(double[] target, double[] start, double k)
        {
            var x = new[] { Math.Clamp(start[0], 0, 1), Math.Clamp(start[1], 0, 1), Math.Clamp(start[2], 0, 1) };
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var now = Lab(x[0], x[1], x[2], k); var error = new[] { now[0] - target[0], now[1] - target[1], now[2] - target[2] };
                if (error[0] * error[0] + error[1] * error[1] + error[2] * error[2] < .01) break;
                var jacobian = new double[9]; const double h = 1e-3;
                for (var i = 0; i < 3; i++)
                {
                    var moved = (double[])x.Clone(); moved[i] = moved[i] + h > 1 ? moved[i] - h : moved[i] + h; var step = moved[i] - x[i];
                    var other = Lab(moved[0], moved[1], moved[2], k); for (var row = 0; row < 3; row++) jacobian[row * 3 + i] = (other[row] - now[row]) / step;
                }
                var inverse = Invert(jacobian); if (inverse.Any(v => !double.IsFinite(v))) break;
                var delta = Multiply(inverse, error);
                for (var i = 0; i < 3; i++) x[i] = Math.Clamp(x[i] - Math.Clamp(delta[i], -.25, .25), 0, 1);
            }
            return x;
        }
    }

    private static byte[] Lut(int inputs, int outputs, int grid, ushort[] table)
    {
        var size = 52 + inputs * 2 * 2 + table.Length * 2 + outputs * 2 * 2; var data = new byte[size];
        Ascii(data, 0, "mft2"); data[8] = (byte)inputs; data[9] = (byte)outputs; data[10] = (byte)grid;
        for (var i = 0; i < 3; i++) BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12 + (i * 3 + i) * 4), 65536);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(48), 2); BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(50), 2);
        var at = 52;
        for (var i = 0; i < inputs; i++) { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), 0); BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at + 2), 65535); at += 4; }
        foreach (var value in table) { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), value); at += 2; }
        for (var i = 0; i < outputs; i++) { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), 0); BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at + 2), 65535); at += 4; }
        return data;
    }
    // ICC v2 legacy Lab encoding: L 0–100 over 0–0xFF00, a and b −128–127.996 over 0–0xFFFF.
    private static void EncodeLab(double[] lab, ushort[] into, int at)
    {
        into[at] = (ushort)Math.Round(Math.Clamp(lab[0], 0, 100) / 100 * 65280);
        into[at + 1] = (ushort)Math.Round(Math.Clamp(lab[1] + 128, 0, 255.996) * 256);
        into[at + 2] = (ushort)Math.Round(Math.Clamp(lab[2] + 128, 0, 255.996) * 256);
    }
    private static double[] DecodeLab(double l, double a, double b) => [Math.Min(100, l / 65280 * 100), a / 256 - 128, b / 256 - 128];

    internal static double[] LabToXyz(double[] lab)
    {
        var fy = (lab[0] + 16) / 116; var fx = fy + lab[1] / 500; var fz = fy - lab[2] / 200;
        static double F(double t) => t > 6 / 29d ? t * t * t : 3 * 36 / 841d * (t - 4 / 29d);
        return [F(fx) * D50[0], F(fy) * D50[1], F(fz) * D50[2]];
    }
    internal static double[] XyzToLab(double[] xyz)
    {
        static double F(double t) => t > 216 / 24389d ? Math.Cbrt(t) : (24389 / 27d * t + 16) / 116;
        var fx = F(xyz[0] / D50[0]); var fy = F(xyz[1] / D50[1]); var fz = F(xyz[2] / D50[2]);
        return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)];
    }
    private static double[] Bradford(double[] from, double[] to)
    {
        double[] cone = [.8951, .2664, -.1614, -.7502, 1.7135, .0367, .0389, -.0685, 1.0296];
        var a = Multiply(cone, from); var b = Multiply(cone, to);
        double[] diagonal = [b[0] / a[0], 0, 0, 0, b[1] / a[1], 0, 0, 0, b[2] / a[2]];
        return Product(Invert(cone), Product(diagonal, cone));
    }
    private static double[] Product(double[] a, double[] b)
    {
        var result = new double[9];
        for (var row = 0; row < 3; row++) for (var column = 0; column < 3; column++) for (var i = 0; i < 3; i++) result[row * 3 + column] += a[row * 3 + i] * b[i * 3 + column];
        return result;
    }
    private static double[] Multiply(double[] m, double[] v) => [m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[3] * v[0] + m[4] * v[1] + m[5] * v[2], m[6] * v[0] + m[7] * v[1] + m[8] * v[2]];
    private static double[] Invert(double[] m)
    {
        var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(det) < 1e-12) return Enumerable.Repeat(double.NaN, 9).ToArray();
        return [(m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
            (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
            (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det];
    }

    private static void Ascii(byte[] into, int at, string text) => Encoding.ASCII.GetBytes(text).CopyTo(into, at);
    private static byte[] Xyz(double[] value)
    {
        var data = new byte[20]; Ascii(data, 0, "XYZ ");
        for (var i = 0; i < 3; i++) BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8 + i * 4), (int)Math.Round(value[i] * 65536));
        return data;
    }
    private static byte[] Description(string name)
    {
        var text = Encoding.ASCII.GetBytes(new string(name.Select(c => c < 128 ? c : '?').ToArray())); var unicode = Encoding.BigEndianUnicode.GetBytes(name + "\0");
        var data = new byte[12 + text.Length + 1 + 8 + unicode.Length + 70]; Ascii(data, 0, "desc");
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)text.Length + 1); text.CopyTo(data, 12);
        var u = 12 + text.Length + 1; BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(u + 4), (uint)(unicode.Length / 2)); unicode.CopyTo(data, u + 8);
        return data;
    }
    private static byte[] Copyright() => Encoding.ASCII.GetBytes("text\0\0\0\0Compositor generated profile\0");
    /// <summary>An ICC v2.1 profile of the given class, color space and connection space; tags whose data is the same array share it.</summary>
    private static byte[] Write(string profileClass, string space, string pcs, (string Signature, byte[] Data)[] tags)
    {
        var unique = tags.Select(t => t.Data).Distinct().ToList(); var offsets = new Dictionary<byte[], int>();
        var position = 132 + tags.Length * 12;
        foreach (var data in unique) { offsets[data] = position; position += (data.Length + 3) / 4 * 4; }
        var result = new byte[position];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)position); result[8] = 2; result[9] = 0x10;
        Ascii(result, 12, profileClass); Ascii(result, 16, space); Ascii(result, 20, pcs); Ascii(result, 36, "acsp"); Ascii(result, 40, "MSFT");
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(24), 2026); BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(26), 11); BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(28), 10);
        for (var i = 0; i < 3; i++) BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(68 + i * 4), (int)Math.Round(D50[i] * 65536));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(128), (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var at = 132 + i * 12; Ascii(result, at, tags[i].Signature);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 4), (uint)offsets[tags[i].Data]); BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 8), (uint)tags[i].Data.Length);
        }
        foreach (var data in unique) data.CopyTo(result, offsets[data]);
        return result;
    }
}
