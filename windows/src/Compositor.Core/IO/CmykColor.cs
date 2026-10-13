using System.Text;
using System.Globalization;
using Compositor.Core.Document;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.IO;

/// <summary>ICC conversion keeps the document in sRGB; inks are an output/proof transform.</summary>
public static class CmykColor
{
    public static byte[] DefaultProfile => ColorProfiles.USWebCoatedSWOP.ToByteArray();
    public static bool IsProfile(byte[]? bytes) => bytes is { Length: >= 128 and <= 4 * 1024 * 1024 }
        && Encoding.ASCII.GetString(bytes, 16, 4) == "CMYK" && Encoding.ASCII.GetString(bytes, 36, 4) == "acsp";
    public static SKBitmap Decode(int width, int height, byte[]? c, byte[]? m, byte[]? y, byte[]? k, byte[]? alpha, byte[]? profile)
    {
        var inks = new byte[checked(width * height * 4)];
        for (var i = 0; i < width * height; i++) { inks[i * 4] = (byte)(255 - (c?[i] ?? 255)); inks[i * 4 + 1] = (byte)(255 - (m?[i] ?? 255)); inks[i * 4 + 2] = (byte)(255 - (y?[i] ?? 255)); inks[i * 4 + 3] = (byte)(255 - (k?[i] ?? 255)); }
        using var image = new MagickImage(); image.ReadPixels(inks, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.CMYK));
        image.ColorSpace = ColorSpace.CMYK; image.TransformColorSpace(new ColorProfile(profile ?? DefaultProfile), ColorProfiles.SRGB);
        using var deep = AdvancedEdit.Pixels(image); var pixels = DeepPixels.Preview(deep);
        if (alpha is not null) { var data = pixels.GetPixelSpan(); for (var j = 0; j < height; j++) for (var i = 0; i < width; i++) data[j * pixels.RowBytes + i * 4 + 3] = alpha[j * width + i]; }
        return pixels;
    }
    private static MagickImage Convert(SKBitmap source, byte[] profile, SKColor matte)
    {
        if (!IsProfile(profile)) throw new InvalidDataException("Choose a CMYK ICC profile.");
        using var flat = DeepPixels.From(source); using (var canvas = new SKCanvas(flat)) using (var paint = new SKPaint { BlendMode = SKBlendMode.DstOver, Color = matte }) canvas.DrawPaint(paint);
        var image = DeepImageCodec.Magick(flat); image.Alpha(AlphaOption.Remove); image.RenderingIntent = RenderingIntent.Relative;
        image.TransformColorSpace(ColorProfiles.SRGB, new ColorProfile(profile)); return image;
    }
    public static SKBitmap Proof(SKBitmap source, byte[] profile, bool proof = true, bool warning = false)
    {
        using var image = Convert(source, profile, SKColors.White); image.TransformColorSpace(new ColorProfile(profile), ColorProfiles.SRGB);
        using var deep = AdvancedEdit.Pixels(image); var simulated = DeepPixels.Preview(deep);
        var src = source.GetPixelSpan(); var dst = simulated.GetPixelSpan();
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var a = y * source.RowBytes + x * 4; var b = y * simulated.RowBytes + x * 4;
            var outside = Math.Max(Math.Abs(src[a] - dst[b]), Math.Max(Math.Abs(src[a + 1] - dst[b + 1]), Math.Abs(src[a + 2] - dst[b + 2]))) > 12;
            if (warning && outside && src[a + 3] > 0) dst[b] = dst[b + 1] = dst[b + 2] = 128;
            else if (!proof) for (var channel = 0; channel < 3; channel++) dst[b + channel] = src[a + channel];
            dst[b + 3] = src[a + 3];
        }
        return simulated;
    }
    public static byte[] Encode(SKBitmap source, ExportOptions options)
    {
        var profile = options.CmykProfile ?? DefaultProfile; using var image = Convert(source, profile, options.Background ?? SKColors.White);
        image.Quality = (uint)options.Quality; image.Depth = options.Format == ExportFormat.Tiff ? 16u : 8u;
        image.Density = new Density(options.Resolution, options.Resolution, DensityUnit.PixelsPerInch);
        if (options.Format == ExportFormat.Tiff) return image.ToByteArray(MagickFormat.Tiff);
        var jpeg = image.ToByteArray(MagickFormat.Jpeg);
        return options.Format == ExportFormat.Pdf ? Pdf(jpeg, profile, source.Width, source.Height, options.Resolution) : jpeg;
    }
    // A single flattened ICCBased CMYK page, without requiring a Ghostscript installation.
    private static byte[] Pdf(byte[] jpeg, byte[] profile, int width, int height, double resolution)
    {
        using var file = new MemoryStream(); var offsets = new List<long> { 0 };
        void Write(string value) => file.Write(Encoding.ASCII.GetBytes(value));
        void Object(int id, string value, byte[]? stream = null) { offsets.Add(file.Position); Write($"{id} 0 obj\n{value}\n"); if (stream is not null) { Write("stream\n"); file.Write(stream); Write("\nendstream\n"); } Write("endobj\n"); }
        var w = (width * 72 / resolution).ToString("0.######", CultureInfo.InvariantCulture); var h = (height * 72 / resolution).ToString("0.######", CultureInfo.InvariantCulture);
        Write("%PDF-1.4\n"); Object(1, "<< /Type /Catalog /Pages 2 0 R >>"); Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 6 0 R >>");
        Object(4, $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace [/ICCBased 5 0 R] /BitsPerComponent 8 /Decode [1 0 1 0 1 0 1 0] /Filter /DCTDecode /Length {jpeg.Length} >>", jpeg);
        Object(5, $"<< /N 4 /Alternate /DeviceCMYK /Length {profile.Length} >>", profile);
        var commands = Encoding.ASCII.GetBytes($"q {w} 0 0 {h} 0 0 cm /Im0 Do Q"); Object(6, $"<< /Length {commands.Length} >>", commands);
        var xref = file.Position; Write("xref\n0 7\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return file.ToArray();
    }
}
