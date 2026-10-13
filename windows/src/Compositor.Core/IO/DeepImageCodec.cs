using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Format;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.IO;

public static class DeepImageCodec
{
    public static MagickImage Magick(SKBitmap bitmap)
    {
        using var pixels = DeepPixels.From(bitmap);
        var result = new MagickImage();
        result.ReadPixels(pixels.GetPixelSpan().ToArray(), new PixelReadSettings((uint)bitmap.Width, (uint)bitmap.Height, StorageType.Float, PixelMapping.RGBA));
        result.ColorSpace = ColorSpace.sRGB; return result;
    }
    public static ImportedImage Decode(byte[] bytes, string name, int remainingPixels = 100_000_000)
    {
        var info = new MagickImageInfo(bytes);
        if (info.Width > DocumentLimits.MaxSide || info.Height > DocumentLimits.MaxSide || (long)info.Width * info.Height > remainingPixels)
            throw new ImportException(ImportError.TooLarge);
        using var image = new MagickImage(bytes); var mode = image.ColorSpace switch { ColorSpace.CMYK => ImageMode.CMYK, ColorSpace.Lab => ImageMode.Lab, ColorSpace.Gray => ImageMode.Grayscale, _ => ImageMode.RGB }; var depth = image.Depth > 16 ? 32 : image.Depth > 8 ? 16 : 8;
        var cmykProfile = mode == ImageMode.CMYK ? image.GetColorProfile()?.ToByteArray() ?? CmykColor.DefaultProfile : null;
        image.AutoOrient();
        var linear = info.Format is MagickFormat.Exr or MagickFormat.Hdr;
        if (cmykProfile is not null) image.TransformColorSpace(new ColorProfile(cmykProfile), ColorProfiles.SRGB);
        else if (!linear) image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = ColorType.TrueColorAlpha;
        using var pixels = image.GetPixels(); var source = pixels.ToArray() ?? throw new InvalidDataException("Empty decoded image.");
        var result = Bitmaps.Allocate(DeepPixels.Info((int)image.Width, (int)image.Height));
        var target = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        if (source.Length != target.Length) { result.Dispose(); throw new InvalidDataException("Unexpected image channel layout."); }
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i] / 65535f;
            target[i] = linear && i % 4 != 3 ? LinearToSrgb(value) : value;
        }
        if (info.Format is MagickFormat.Exr or MagickFormat.Hdr)
        {
            // Upstream treats only a genuine above-white highlight as HDR, allowing decoding roundoff.
            depth = 16;
            for (var i = 0; i < target.Length; i += 4)
                if (target[i + 3] > 0 && Math.Max(target[i], Math.Max(target[i + 1], target[i + 2])) > 1.001f) { depth = 32; break; }
        }
        ImportedImage imported;
        if (depth > 8) imported = ImportedImage.CreateDeep(result, name, depth);
        else { using (result) imported = ImportedImage.Create(DeepPixels.Preview(result), name); }
        imported.ColorMode = mode; imported.CmykProfile = cmykProfile; return imported;
    }
    public static byte[] EncodeMode(SKBitmap pixels, ImageSettings settings, bool tiff, double resolution)
    {
        using var encoded = Magick(pixels); encoded.Depth = (uint)settings.Depth;
        encoded.Density = new Density(resolution, resolution, DensityUnit.PixelsPerInch);
        switch (settings.Mode)
        {
            case ImageMode.CMYK when tiff: encoded.ColorSpace = ColorSpace.CMYK; break;
            case ImageMode.Lab when tiff: encoded.ColorSpace = ColorSpace.Lab; break;
            case ImageMode.Grayscale: encoded.ColorSpace = ColorSpace.Gray; break;
            case ImageMode.Bitmap: encoded.ColorSpace = ColorSpace.Gray; encoded.ColorType = ColorType.Bilevel; encoded.Depth = 1; break;
            case ImageMode.Indexed: encoded.Quantize(new QuantizeSettings { Colors = (uint)(settings.Palette?.Length ?? 256), DitherMethod = DitherMethod.No }); encoded.Depth = 8; encoded.ColorType = ColorType.PaletteAlpha; break;
        }
        if (tiff && settings.Depth == 32) encoded.Settings.SetDefine(MagickFormat.Tiff, "floating-point", "true");
        if (!tiff && settings.Depth > 8) { encoded.Depth = 16; encoded.Settings.SetDefine(MagickFormat.Png, "bit-depth", "16"); }
        return encoded.ToByteArray(tiff ? MagickFormat.Tiff : MagickFormat.Png);
    }
    private static float LinearToSrgb(float value)
    { var sign = MathF.Sign(value); var v = MathF.Abs(value); return sign * (v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f); }
    public static byte[] EncodeExr(SKBitmap image)
    {
        using var encoded = Magick(image);
        // OpenEXR stores linear light. The decoder returns extended sRGB for the editor.
        encoded.ColorSpace = ColorSpace.RGB; encoded.Depth = 16;
        return encoded.ToByteArray(MagickFormat.Exr);
    }
    public static byte[] EncodePng(SKBitmap image)
    {
        using var encoded = Magick(image); encoded.Depth = 16;
        encoded.Settings.SetDefine(MagickFormat.Png, "bit-depth", "16"); encoded.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
        return encoded.ToByteArray(MagickFormat.Png);
    }
}
