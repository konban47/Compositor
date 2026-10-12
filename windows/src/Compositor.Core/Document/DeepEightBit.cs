using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Port of upstream DeepPixels.c split/fit/merge (MIT, Wonder Assembly), adapted to straight float assets.</summary>
public static class DeepEightBit
{
    public delegate void Kernel(Span<byte> pixels, int width, int height, int stride);
    private static float Noise(int x, int y, int channel)
    {
        uint h = unchecked((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)(channel + 1) * 0xC2B2AE3Du);
        h ^= h >> 15; h *= 0x2C1B3C6D; h ^= h >> 12; h *= 0x297A2D39; h ^= h >> 15; return (h >> 8) / 16777216f;
    }
    private static (float Change, float Offset, bool Fit) Fit(float low, float near, float middle, float far, float past, int room)
    {
        if (room <= 0) return (0, 0, false);
        var step = room == 1 || past < 1 ? (near - low) * Math.Min(past, 1) : near - low + (middle - near) * (past - 1);
        if (room < 4) return (step, 0, false);
        var slope = (-1.75f * low - .75f * near + .25f * middle + 2.25f * far) / 8.75f;
        var start = (low + near + middle + far) / 4 - slope * 1.75f;
        var off = Math.Max(Math.Max(Math.Abs(low - start), Math.Abs(near - start - slope)), Math.Max(Math.Abs(middle - start - 2 * slope), Math.Abs(far - start - 4 * slope)));
        return off > 1 ? (step, 0, false) : (slope * past, start - low, true);
    }
    public static SKBitmap Run(SKBitmap source, Kernel operation, bool mixesChannels = true)
    {
        using var straight = DeepPixels.From(source); var src = MemoryMarshal.Cast<byte, float>(straight.GetPixelSpan());
        var count = checked(source.Width * source.Height * 4); var low = new byte[count];
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var i = (y * source.Width + x) * 4; var alpha = (byte)Math.Clamp(MathF.Round(src[i + 3] * 255), 0, 255); low[i + 3] = alpha;
            for (var c = 0; c < 3; c++) low[i + c] = (byte)Math.Clamp(MathF.Floor(src[i + c] * src[i + 3] * 255 + Noise(x, y, c)) - 1, 0, alpha);
        }
        var basis = low.ToArray(); operation(basis, source.Width, source.Height, source.Width * 4);
        var result = Bitmaps.Allocate(DeepPixels.Info(source.Width, source.Height));
        try
        {
            var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()); var offsets = new float[count]; var fitted = new byte[count];
            for (var i = 0; i < count; i++) output[i] = basis[i];
            for (var k = 0; k < (mixesChannels ? 3 : 1); k++)
            {
                var runs = new byte[3][];
                for (var step = 0; step < 3; step++)
                {
                    runs[step] = low.ToArray(); var run = runs[step]; var amount = 1 << step;
                    for (var i = 0; i < count; i += 4) for (var c = 0; c < 3; c++) if (!mixesChannels || c == k) run[i + c] = (byte)Math.Min(low[i + 3], low[i + c] + amount);
                    operation(run, source.Width, source.Height, source.Width * 4);
                }
                for (var i = 0; i < count; i += 4) for (var c = 0; c < 3; c++)
                {
                    var channel = mixesChannels ? k : c; var past = Math.Clamp(src[i + channel] * src[i + 3] * 255 - low[i + channel], 0, 2);
                    var delta = Fit(basis[i + c], runs[0][i + c], runs[1][i + c], runs[2][i + c], past, low[i + 3] - low[i + channel]);
                    output[i + c] += delta.Change; if (delta.Fit) { offsets[i + c] += delta.Offset; fitted[i + c]++; }
                }
            }
            for (var i = 0; i < count; i += 4)
            {
                for (var c = 0; c < 3; c++) output[i + c] = basis[i + 3] == 0 ? 0 : Math.Clamp((output[i + c] + (fitted[i + c] > 0 ? offsets[i + c] / fitted[i + c] : 0)) / basis[i + 3], 0, 1);
                output[i + 3] = basis[i + 3] == low[i + 3] ? src[i + 3] : basis[i + 3] / 255f;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
