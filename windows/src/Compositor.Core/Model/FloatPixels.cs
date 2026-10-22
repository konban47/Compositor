using System.IO.Compression;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>Lossless little-endian float sidecar for 32-bit layers; dimensions come from the checked PNG.</summary>
public static class FloatPixels
{
    public static string FileName(Guid id) => $"float-{id.ToString().ToUpperInvariant()}.rgba";
    public static byte[] Encode(SKBitmap image)
    {
        using var output = new MemoryStream();
        Encode(image, output);
        return output.ToArray();
    }
    public static void Encode(SKBitmap image, Stream output)
    {
        using var zip = new ZLibStream(output, CompressionLevel.Optimal, true);
        zip.Write(image.GetPixelSpan());
    }
    public static void ValidateSize(int width, int height)
    {
        if (MemoryBudget.PixelBytes(width, height, 16) > DocumentLimits.MaxAssetBytes)
            throw new Compositor.Core.IO.ProjectException(Compositor.Core.IO.ProjectError.TooLarge);
    }
    public static SKBitmap Decode(byte[] bytes, int width, int height)
    {
        ValidateSize(width, height);
        var result = Bitmaps.Allocate(DeepPixels.Info(width, height));
        try
        {
            using var input = new MemoryStream(bytes); using var zip = new ZLibStream(input, CompressionMode.Decompress);
            zip.ReadExactly(result.GetPixelSpan()); if (zip.ReadByte() != -1) throw new InvalidDataException("Invalid float image length.");
            var samples = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
            for (var i = 0; i < samples.Length; i++) if (!float.IsFinite(samples[i]) || Math.Abs(samples[i]) > 65536
                || i % 4 == 3 && samples[i] is < 0 or > 1) throw new InvalidDataException("Invalid float sample.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
