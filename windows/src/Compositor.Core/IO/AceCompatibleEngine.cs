using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Compositor.Core.Model;
using ImageMagick;

namespace Compositor.Core.IO;

/// <summary>
/// Independent ICC CMM implementing matrix/TRC and v2/v4 LUT pipelines, intent tables and
/// the published D50 black-point scaling model. It contains no Adobe code or binaries.
/// Adobe ACE's proprietary gamut mapping and rounding are not a bit-exact compatibility contract.
/// </summary>
public static class AceCompatibleEngine
{
    private static readonly double[] D50 = [.9642, 1, .8249];
    public static MagickImage Convert(MagickImage input, byte[] source, byte[] destination, ColorManagementSettings settings)
    {
        var from = new Profile(source); var to = new Profile(destination);
        var forward = from.Transform(true, settings.Intent); var reverse = to.Transform(false, settings.Intent);
        var sourceBlack = settings.BlackPointCompensation ? from.BlackPoint(settings.Intent) : 0;
        var targetBlack = settings.BlackPointCompensation ? to.BlackPoint(settings.Intent) : 0;
        var scale = settings.BlackPointCompensation && settings.Intent != ColorIntent.Absolute ? (1 - targetBlack) / Math.Max(.001, 1 - sourceBlack) : 1;
        using var original = (MagickImage)input.Clone(); original.Alpha(AlphaOption.On);
        if (original.ChannelCount != from.Channels + 1) throw new InvalidDataException("The ICC profile does not match the image channels.");
        var width = checked((int)input.Width); var height = checked((int)input.Height); var stride = to.Channels + 1;
        MemoryBudget.Current.Check(checked((long)width * height * stride * 12));
        var output = new float[checked(width * height * stride)]; var row = new float[width * (from.Channels + 1)];
        using var pixels = original.GetPixelsUnsafe(); Span<double> color = stackalloc double[4];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(pixels.GetAreaPointer(0, y, input.Width, 1), row, 0, row.Length);
            for (var x = 0; x < width; x++)
            {
                var index = x * (from.Channels + 1);
                for (var c = 0; c < from.Channels; c++) { if (!float.IsFinite(row[index + c])) throw new InvalidDataException("ICC input contains non-finite samples."); color[c] = Math.Clamp(row[index + c] / 65535d, 0, 1); }
                forward.Apply(color); from.DecodePcs(color, forward);
                if (settings.Intent == ColorIntent.Absolute) for (var c = 0; c < 3; c++) color[c] *= from.White[c] / Math.Max(.0001, to.White[c]);
                else if (scale != 1) for (var c = 0; c < 3; c++) color[c] = color[c] * scale + D50[c] * (1 - scale);
                to.EncodePcs(color, reverse); reverse.Apply(color);
                var target = (y * width + x) * stride;
                for (var c = 0; c < to.Channels; c++) { if (!double.IsFinite(color[c])) throw new InvalidDataException("Invalid ICC transform result."); output[target + c] = (float)Math.Clamp(color[c], 0, 1); }
                output[target + to.Channels] = row[index + from.Channels] / 65535f;
            }
        }
        var result = new MagickImage();
        try { result.ReadPixels(MemoryMarshal.AsBytes(output.AsSpan()).ToArray(), new PixelReadSettings(input.Width, input.Height, StorageType.Float, (to.Model == "GRAY" ? "I" : to.Model) + "A")); result.ColorSpace = to.Model == "CMYK" ? ColorSpace.CMYK : to.Model == "GRAY" ? ColorSpace.Gray : ColorSpace.sRGB; result.SetProfile(new ColorProfile(destination)); return result; }
        catch { result.Dispose(); throw; }
    }

    private interface Stage { void Apply(Span<double> value); }
    private sealed class Pipeline : Stage
    {
        public List<Stage> Stages { get; } = [];
        public bool IsLut, LegacyLab;
        public double XyzScale = 65535d / 32768;
        public void Apply(Span<double> value) { foreach (var stage in Stages) stage.Apply(value); }
    }
    private sealed class Matrix(double[] values, double[]? offset = null) : Stage
    {
        public void Apply(Span<double> v)
        {
            var x = values[0] * v[0] + values[1] * v[1] + values[2] * v[2];
            var y = values[3] * v[0] + values[4] * v[1] + values[5] * v[2];
            var z = values[6] * v[0] + values[7] * v[1] + values[8] * v[2];
            v[0] = x + (offset?[0] ?? 0); v[1] = y + (offset?[1] ?? 0); v[2] = z + (offset?[2] ?? 0);
        }
        public static double[] Inverse(double[] a)
        {
            var determinant = a[0] * (a[4] * a[8] - a[5] * a[7]) - a[1] * (a[3] * a[8] - a[5] * a[6]) + a[2] * (a[3] * a[7] - a[4] * a[6]);
            if (Math.Abs(determinant) < 1e-12) throw new InvalidDataException("Singular ICC color matrix.");
            return new[] { a[4]*a[8]-a[5]*a[7], a[2]*a[7]-a[1]*a[8], a[1]*a[5]-a[2]*a[4], a[5]*a[6]-a[3]*a[8], a[0]*a[8]-a[2]*a[6], a[2]*a[3]-a[0]*a[5], a[3]*a[7]-a[4]*a[6], a[1]*a[6]-a[0]*a[7], a[0]*a[4]-a[1]*a[3] }.Select(v => v / determinant).ToArray();
        }
    }
    private sealed class Curve
    {
        private readonly Func<double, double> _function;
        public Curve(Func<double, double> function, bool inverse = false)
        {
            if (!inverse) { _function = function; return; }
            var table = new double[4097];
            for (var n = 0; n < table.Length; n++) { var low = 0d; var high = 1d; var target = n / 4096d; for (var i = 0; i < 24; i++) { var middle = (low + high) / 2; if (function(middle) < target) low = middle; else high = middle; } table[n] = (low + high) / 2; }
            _function = x => { var p = x * 4096; var i = Math.Min(4095, (int)p); return table[i] + (table[i + 1] - table[i]) * (p - i); };
        }
        public double Apply(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return _function(value);
        }
        public static Curve Table(double[] values, bool inverse = false) => new(x => { var p = Math.Clamp(x, 0, 1) * (values.Length - 1); var i = Math.Min(values.Length - 2, (int)p); return values[i] + (values[i + 1] - values[i]) * (p - i); }, inverse);
    }
    private sealed class Curves(Curve[] curves) : Stage
    { public void Apply(Span<double> value) { for (var i = 0; i < curves.Length; i++) value[i] = curves[i].Apply(value[i]); } }
    private sealed class Gray(Curve curve, bool forward, double[] white) : Stage
    {
        public void Apply(Span<double> value)
        { if (forward) { var y = curve.Apply(value[0]); for (var c = 0; c < 3; c++) value[c] = y * white[c]; } else value[0] = curve.Apply(value[1] / white[1]); }
    }
    private sealed class Clut(int[] grid, int outputs, double[] table) : Stage
    {
        public void Apply(Span<double> value)
        {
            Span<int> low = stackalloc int[4]; Span<double> fraction = stackalloc double[4]; Span<int> step = stackalloc int[4]; Span<double> result = stackalloc double[4];
            result.Clear();
            var stride = outputs; var index = 0;
            for (var d = grid.Length - 1; d >= 0; d--) { var p = Math.Clamp(value[d], 0, 1) * (grid[d] - 1); low[d] = Math.Min(grid[d] - 2, (int)p); fraction[d] = p - low[d]; step[d] = stride; index += low[d] * stride; stride *= grid[d]; }
            if (grid.Length == 3)
            {
                // Sort the fractional axes; the enclosing simplex uses four, rather than eight, vertices.
                Span<int> order = stackalloc int[] { 0, 1, 2 };
                for (var i = 0; i < 2; i++) for (var j = i + 1; j < 3; j++) if (fraction[order[j]] > fraction[order[i]]) (order[i], order[j]) = (order[j], order[i]);
                for (var c = 0; c < outputs; c++) result[c] = table[index + c];
                for (var i = 0; i < 3; i++) { var next = index + step[order[i]]; for (var c = 0; c < outputs; c++) result[c] += fraction[order[i]] * (table[next + c] - table[index + c]); index = next; }
            }
            else
            {
                for (var corner = 0; corner < 1 << grid.Length; corner++)
                { var at = index; var weight = 1d; for (var d = 0; d < grid.Length; d++) { var high = (corner & (1 << d)) != 0; if (high) at += step[d]; weight *= high ? fraction[d] : 1 - fraction[d]; } for (var c = 0; c < outputs; c++) result[c] += table[at + c] * weight; }
            }
            result[..outputs].CopyTo(value);
        }
    }
    private sealed class Profile
    {
        private readonly byte[] _bytes;
        private int _rangeStart, _rangeEnd;
        private readonly Dictionary<string, (int Offset, int Length)> _tags = [];
        public string Model { get; }
        private readonly string _pcs;
        public int Channels => Model == "CMYK" ? 4 : Model == "GRAY" ? 1 : 3;
        public double[] White { get; }
        private readonly bool _v4;
        public Profile(byte[] bytes)
        {
            if (!IccProfiles.Valid(bytes)) throw new InvalidDataException("Invalid ICC profile."); _bytes = bytes; _rangeEnd = BinaryPrimitives.ReadInt32BigEndian(bytes); Model = IccProfiles.Model(bytes); _pcs = Text(20); _v4 = bytes[8] >= 4;
            if (_pcs is not ("XYZ " or "Lab ") || bytes[8] is not (2 or 4)) throw new InvalidDataException("ACE compatibility supports ICC v2/v4 XYZ or Lab profiles.");
            var count = U32(128); if (count > 1024) throw new InvalidDataException("Too many ICC tags.");
            for (var i = 0; i < count; i++) { var p = 132 + i * 12; var at = U32(p + 4); var length = U32(p + 8); Range(at, length); if (length < 8) throw new InvalidDataException("Truncated ICC tag."); _tags[Text(p)] = (at, length); }
            // Matrix display profiles encode chromatically adapted D50 colorants. Their legacy
            // device white tag is not the paper white used for absolute print simulation.
            White = Text(12) != "mntr" && _tags.TryGetValue("wtpt", out var white) && white.Length >= 20 ? [Fixed(white.Offset + 8), Fixed(white.Offset + 12), Fixed(white.Offset + 16)] : D50;
        }
        private void Range(int offset, int length) { if (offset < _rangeStart || length < 0 || (long)offset + length > _rangeEnd) throw new InvalidDataException("Truncated ICC profile."); }
        private string Text(int p) { Range(p, 4); return Encoding.ASCII.GetString(_bytes, p, 4); }
        private int U32(int p) { Range(p, 4); var n = BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(p)); if (n > int.MaxValue) throw new InvalidDataException("ICC offset exceeds limits."); return (int)n; }
        private int U16(int p) { Range(p, 2); return BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(p)); }
        private double Fixed(int p) { Range(p, 4); return BinaryPrimitives.ReadInt32BigEndian(_bytes.AsSpan(p)) / 65536d; }
        private Curve ReadCurve(int at, out int length, bool inverse = false)
        {
            if (Text(at) == "curv")
            {
                var count = U32(at + 8); if (count > 65536) throw new InvalidDataException("ICC curve exceeds limits."); length = (12 + count * 2 + 3) / 4 * 4; Range(at, 12 + count * 2);
                if (count == 0) return new(x => x, inverse);
                if (count == 1) { var gamma = U16(at + 12) / 256d; if (gamma <= 0) throw new InvalidDataException("Invalid ICC gamma."); return new(x => Math.Pow(x, gamma), inverse); }
                return Curve.Table(Enumerable.Range(0, count).Select(i => U16(at + 12 + i * 2) / 65535d).ToArray(), inverse);
            }
            if (Text(at) != "para") throw new InvalidDataException("Unsupported ICC tone curve.");
            var kind = U16(at + 8); var countP = kind switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => throw new InvalidDataException("Unknown ICC parametric curve.") };
            length = 12 + countP * 4; Range(at, length); var p = Enumerable.Range(0, countP).Select(i => Fixed(at + 12 + i * 4)).ToArray();
            if (p[0] <= 0 || kind > 0 && p[1] == 0) throw new InvalidDataException("Invalid ICC parametric curve.");
            return new(x => kind switch { 0 => Math.Pow(x, p[0]), 1 => x >= -p[2]/p[1] ? Math.Pow(Math.Max(0,p[1]*x+p[2]),p[0]) : 0, 2 => x >= -p[2]/p[1] ? Math.Pow(Math.Max(0,p[1]*x+p[2]),p[0])+p[3] : p[3], 3 => x >= p[4] ? Math.Pow(Math.Max(0,p[1]*x+p[2]),p[0]) : p[3]*x, _ => x >= p[4] ? Math.Pow(Math.Max(0,p[1]*x+p[2]),p[0])+p[5] : p[3]*x+p[6] }, inverse);
        }
        private Stage CurvesAt(int at, int count)
        { var curves = new Curve[count]; for (var i = 0; i < count; i++) { curves[i] = ReadCurve(at, out var length); at += length; } return new Curves(curves); }
        private Curve TaggedCurve((int Offset, int Length) tag, bool inverse)
        {
            var previousStart = _rangeStart; var previousEnd = _rangeEnd;
            _rangeStart = tag.Offset; _rangeEnd = checked(tag.Offset + tag.Length);
            try { return ReadCurve(tag.Offset, out _, inverse); }
            finally { _rangeStart = previousStart; _rangeEnd = previousEnd; }
        }
        public Pipeline Transform(bool forward, ColorIntent intent)
        {
            var wanted = intent == ColorIntent.Perceptual ? 0 : intent == ColorIntent.Saturation ? 2 : 1;
            var prefix = forward ? "A2B" : "B2A";
            if (_tags.TryGetValue(prefix + wanted, out var lut) || _tags.TryGetValue(prefix + "0", out lut))
            {
                var previousStart = _rangeStart; var previousEnd = _rangeEnd; _rangeStart = lut.Offset; _rangeEnd = checked(lut.Offset + lut.Length);
                try { return ReadLut(lut.Offset, forward); } finally { _rangeStart = previousStart; _rangeEnd = previousEnd; }
            }
            var pipeline = new Pipeline();
            if (Model == "GRAY" && _tags.TryGetValue("kTRC", out var gray)) { pipeline.Stages.Add(new Gray(TaggedCurve(gray, !forward), forward, D50)); return pipeline; }
            if (Model != "RGB") throw new InvalidDataException("The ICC profile has no transform for this rendering intent.");
            var matrix = new double[9]; var curves = new Curve[3];
            for (var c = 0; c < 3; c++)
            {
                var letter = "rgb"[c]; if (!_tags.TryGetValue(letter + "XYZ", out var xyz) || !_tags.TryGetValue(letter + "TRC", out var trc)) throw new InvalidDataException("The ICC RGB profile needs matrix/TRC tags.");
                if (xyz.Length < 20 || trc.Length < 12) throw new InvalidDataException("Truncated ICC matrix or curve.");
                for (var row = 0; row < 3; row++) matrix[row * 3 + c] = Fixed(xyz.Offset + 8 + row * 4);
                curves[c] = TaggedCurve(trc, !forward);
            }
            if (forward) { pipeline.Stages.Add(new Curves(curves)); pipeline.Stages.Add(new Matrix(matrix)); }
            else { pipeline.Stages.Add(new Matrix(Matrix.Inverse(matrix))); pipeline.Stages.Add(new Curves(curves)); }
            return pipeline;
        }
        private Pipeline ReadLut(int at, bool forward)
        {
            var type = Text(at); Range(at, 32); var inputs = _bytes[at + 8]; var outputs = _bytes[at + 9];
            if (inputs is < 1 or > 4 || outputs is < 1 or > 4 || inputs != (forward ? Channels : 3) || outputs != (forward ? 3 : Channels)) throw new InvalidDataException("Invalid ICC LUT dimensions.");
            var pipeline = new Pipeline { IsLut = true, LegacyLab = !_v4 && type == "mft2", XyzScale = type == "mft1" ? 255d / 128 : 65535d / 32768 };
            if (type is "mft1" or "mft2")
            {
                var sixteen = type == "mft2"; var grid = _bytes[at + 10]; if (grid < 2) throw new InvalidDataException("Invalid ICC grid.");
                var matrix = Enumerable.Range(0, 9).Select(i => Fixed(at + 12 + i * 4)).ToArray();
                if (!forward && _pcs == "XYZ ") pipeline.Stages.Add(new Matrix(matrix));
                var n = sixteen ? U16(at + 48) : 256; var m = sixteen ? U16(at + 50) : 256;
                if (n < 2 || m < 2) throw new InvalidDataException("Invalid ICC LUT curves.");
                var p = at + (sixteen ? 52 : 48);
                double[] Values(int count) { var bytes = checked(count * (sixteen ? 2 : 1)); Range(p, bytes); var values = new double[count]; for (var i = 0; i < count; i++) values[i] = sixteen ? U16(p + i * 2) / 65535d : _bytes[p + i] / 255d; p += bytes; return values; }
                var pre = new Curve[inputs]; for (var i = 0; i < inputs; i++) pre[i] = Curve.Table(Values(n)); pipeline.Stages.Add(new Curves(pre));
                var count = checked((int)Math.Pow(grid, inputs) * outputs); pipeline.Stages.Add(new Clut(Enumerable.Repeat((int)grid, inputs).ToArray(), outputs, Values(count)));
                var post = new Curve[outputs]; for (var i = 0; i < outputs; i++) post[i] = Curve.Table(Values(m)); pipeline.Stages.Add(new Curves(post)); return pipeline;
            }
            if (type is not ("mAB " or "mBA ")) throw new InvalidDataException("Unsupported ICC LUT type: " + type);
            var aToB = type == "mAB "; var b = U32(at + 12); var matrixAt = U32(at + 16); var middle = U32(at + 20); var clutAt = U32(at + 24); var a = U32(at + 28);
            void Curves(int offset, int count) { if (offset != 0) pipeline.Stages.Add(CurvesAt(checked(at + offset), count)); }
            void MatrixStage() { if (matrixAt != 0) { var p = checked(at + matrixAt); pipeline.Stages.Add(new Matrix(Enumerable.Range(0,9).Select(i=>Fixed(p+i*4)).ToArray(), Enumerable.Range(0,3).Select(i=>Fixed(p+36+i*4)).ToArray())); } }
            void ClutStage()
            {
                if (clutAt == 0) return; var p = checked(at + clutAt); Range(p, 20); var grid = _bytes.Skip(p).Take(inputs).Select(v=>(int)v).ToArray();
                if (grid.Any(v=>v<2)) throw new InvalidDataException("Invalid ICC grid."); var count = (int)outputs; foreach(var g in grid) count=checked(count*g);
                var precision = _bytes[p+16]; if(precision is not(1 or 2)) throw new InvalidDataException("Invalid ICC CLUT precision."); Range(p+20,checked(count*precision));
                pipeline.Stages.Add(new Clut(grid,outputs,Enumerable.Range(0,count).Select(i=>precision==1?_bytes[p+20+i]/255d:U16(p+20+i*2)/65535d).ToArray()));
            }
            if(aToB) { Curves(a,inputs); ClutStage(); Curves(middle,outputs); MatrixStage(); Curves(b,outputs); }
            else { Curves(b,inputs); MatrixStage(); Curves(middle,inputs); ClutStage(); Curves(a,outputs); }
            return pipeline;
        }
        public void DecodePcs(Span<double> value, Pipeline pipeline)
        {
            if (!pipeline.IsLut) return;
            if (_pcs == "XYZ ") { for(var i=0;i<3;i++) value[i]*=pipeline.XyzScale; return; }
            var scale = pipeline.LegacyLab ? 65535d/65280 : 1; var l=value[0]*scale*100; var a=value[1]*scale*255-128; var b=value[2]*scale*255-128;
            var y=(l+16)/116; var x=y+a/500; var z=y-b/200;
            static double Inverse(double t)=>t>6d/29?t*t*t:3*Math.Pow(6d/29,2)*(t-4d/29);
            value[0]=Inverse(x)*D50[0]; value[1]=Inverse(y); value[2]=Inverse(z)*D50[2];
        }
        public void EncodePcs(Span<double> value, Pipeline pipeline)
        {
            if (!pipeline.IsLut) return;
            if (_pcs == "XYZ ") { for(var i=0;i<3;i++) value[i]/=pipeline.XyzScale; return; }
            static double F(double t)=>t>216d/24389?Math.Cbrt(t):t*841d/108+4d/29;
            var x=F(value[0]/D50[0]); var y=F(value[1]); var z=F(value[2]/D50[2]); var scale=pipeline.LegacyLab?65280d/65535:1;
            value[0]=(116*y-16)/100*scale; value[1]=(500*(x-y)+128)/255*scale; value[2]=(200*(y-z)+128)/255*scale;
        }
        public double BlackPoint(ColorIntent intent)
        {
            Span<double> point=stackalloc double[4];
            point.Clear();
            if(Model=="CMYK") { var back=Transform(false,ColorIntent.Perceptual); EncodePcs(point,back); back.Apply(point); }
            var front=Transform(true,intent); front.Apply(point); DecodePcs(point,front);
            return Math.Clamp(point[1],0,.18418652);
        }
    }
}
