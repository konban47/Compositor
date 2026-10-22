using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Format;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.IO;

public static class DeepImageCodec
{
    static DeepImageCodec()
    {
        ResourceLimits.Memory = (ulong)Math.Min(512L << 20, MemoryBudget.Shared.Limit / 8);
        ResourceLimits.MaxMemoryRequest = ResourceLimits.Memory;
        ResourceLimits.Disk = 8UL << 30;
        ResourceLimits.Thread = (ulong)Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    }
    public static MagickImage Magick(SKBitmap bitmap)
    {
        using var converted = bitmap.ColorType == SKColorType.RgbaF32 ? null : DeepPixels.From(bitmap);
        var pixels = converted ?? bitmap;
        using var input = new BitmapReadStream(pixels);
        var result = new MagickImage();
        result.ReadPixels(input, new PixelReadSettings((uint)bitmap.Width, (uint)bitmap.Height, StorageType.Float, PixelMapping.RGBA));
        result.ColorSpace = ColorSpace.sRGB; return result;
    }
    public static ImportedImage Decode(byte[] bytes, string name, int remainingPixels = 100_000_000)
    {
        var info = new MagickImageInfo(bytes);
        if (info.Width > DocumentLimits.MaxSide || info.Height > DocumentLimits.MaxSide || (long)info.Width * info.Height > remainingPixels)
            throw new ImportException(ImportError.TooLarge);
        MemoryBudget.Current.Check(checked((long)info.Width * info.Height * 36));
        using var image = new MagickImage(bytes); var mode = image.ColorSpace switch { ColorSpace.CMYK => ImageMode.CMYK, ColorSpace.Lab => ImageMode.Lab, ColorSpace.Gray => ImageMode.Grayscale, _ => ImageMode.RGB }; var depth = image.Depth > 16 ? 32 : image.Depth > 8 ? 16 : 8;
        var cmykProfile = mode == ImageMode.CMYK ? image.GetColorProfile()?.ToByteArray() ?? CmykColor.DefaultProfile : null;
        image.AutoOrient();
        var linear = info.Format is MagickFormat.Exr or MagickFormat.Hdr;
        if (cmykProfile is not null) image.TransformColorSpace(new ColorProfile(cmykProfile), ColorProfiles.SRGB);
        else if (!linear) image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = ColorType.TrueColorAlpha;
        var width = (int)image.Width; var height = (int)image.Height;
        var useDeep = depth > 8 || linear;
        var result = Bitmaps.Allocate(useDeep ? DeepPixels.Info(width, height) : Bitmaps.ColorInfo(width, height));
        try
        {
            using var pixels = image.GetPixelsUnsafe();
            var source = new float[checked(width * 4)];
            var hdr = false;
            for (var y = 0; y < height; y++)
            {
                var row = pixels.GetAreaPointer(0, y, image.Width, 1);
                if (row == IntPtr.Zero || image.ChannelCount != 4) throw new InvalidDataException("Unexpected image channel layout.");
                Marshal.Copy(row, source, 0, source.Length);
                if (useDeep)
                {
                    var target = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan().Slice(y * result.RowBytes, width * 16));
                    for (var i = 0; i < source.Length; i++)
                    {
                        var value = source[i] / 65535f;
                        target[i] = linear && i % 4 != 3 ? LinearToSrgb(value) : value;
                    }
                    if (linear) for (var i = 0; i < target.Length; i += 4)
                        if (target[i + 3] > 0 && Math.Max(target[i], Math.Max(target[i + 1], target[i + 2])) > 1.001f) hdr = true;
                }
                else
                {
                    var target = result.GetPixelSpan().Slice(y * result.RowBytes, width * 4);
                    for (var x = 0; x < width; x++)
                    {
                        uint hash = unchecked((uint)(x + y * width) * 1664525u + 1013904223u); hash ^= hash >> 13; hash *= 1274126177;
                        var noise = (hash & 65535) / 65535.0 - .5;
                        for (var c = 0; c < 4; c++) target[x * 4 + c] = (byte)Math.Clamp(Math.Round(source[x * 4 + c] / 65535f * 255 + (c == 3 ? 0 : noise)), 0, 255);
                    }
                }
            }
            if (linear) depth = hdr ? 32 : 16;
            var imported = useDeep ? ImportedImage.CreateDeep(result, name, depth) : ImportedImage.Create(result, name);
            imported.ColorMode = mode; imported.CmykProfile = cmykProfile; return imported;
        }
        catch { result.Dispose(); throw; }
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
        using var output = new MemoryStream(); EncodeExr(image, output); return output.ToArray();
    }
    public static void EncodeExr(SKBitmap image, Stream output)
    {
        using var encoded = Magick(image);
        // OpenEXR stores linear light. The decoder returns extended sRGB for the editor.
        encoded.ColorSpace = ColorSpace.RGB; encoded.Depth = 16;
        encoded.Write(output, MagickFormat.Exr);
    }
    public static byte[] EncodePng(SKBitmap image)
    {
        using var output = new MemoryStream(); EncodePng(image, output); return output.ToArray();
    }
    public static void EncodePng(SKBitmap image, Stream output)
    {
        using var encoded = Magick(image); encoded.Depth = 16;
        encoded.Settings.SetDefine(MagickFormat.Png, "bit-depth", "16"); encoded.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
        encoded.Write(output, MagickFormat.Png);
    }
}
