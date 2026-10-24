using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.IO;

public enum ExportFormat { Png, Jpeg, Pdf, Tiff, Exr, Gif }
public sealed record ExportOptions(ExportFormat Format, int Width, int Height, int Quality = 90,
    bool Transparency = true, SKColor? Background = null, double Resolution = 72, byte[]? CmykProfile = null, bool NativeColorMode = false)
{
    public string Extension => Format switch { ExportFormat.Jpeg => "jpg", ExportFormat.Pdf => "pdf", ExportFormat.Tiff => "tif", ExportFormat.Exr => "exr", ExportFormat.Gif => "gif", _ => "png" };
    public bool IsValid => (CmykProfile is null || CmykColor.IsProfile(CmykProfile) && Format is ExportFormat.Jpeg or ExportFormat.Tiff or ExportFormat.Pdf) && Enum.IsDefined(Format) && Width > 0 && Height > 0
        && Width <= DocumentLimits.MaxSide && Height <= DocumentLimits.MaxSide
        && (long)Width * Height <= DocumentLimits.MaxSurfacePixels && Quality is >= 1 and <= 100
        && double.IsFinite(Resolution) && Resolution is >= 1 and <= 9600;
}

public sealed class ExportResult(byte[] bytes, SKBitmap preview) : IDisposable
{
    public byte[] Bytes { get; } = bytes;
    public SKBitmap Preview { get; } = preview;
    public void Dispose() => Preview.Dispose();
}

/// <summary>One encoding supplies both the saved bytes and the accurate file-size preview.</summary>
public static class ExportImage
{
    public static ExportResult Encode(CanvasDocument document, ExportOptions options)
    {
        if (!options.IsValid) throw new ArgumentOutOfRangeException(nameof(options));
        if ((long)document.Width * document.Height > DocumentLimits.MaxSurfacePixels)
            throw new InvalidOperationException("This canvas is too large for an export preview. Use Export PNG for tiled export.");
        if (options.Format == ExportFormat.Gif) return GifExport.Encode(document, options);
        // Resize the complete composite, so rotated layers, masks and effects scale together.
        using var original = document.Image.Depth > 8 || document.Layers.Any(l => l.Asset?.Deep is not null) ? DocumentRenderer.RenderDeep(document) : DocumentRenderer.Render(document);
        using var pixels = original.Resize(original.ColorType == SKColorType.RgbaF32 ? DeepPixels.Info(options.Width, options.Height) : Bitmaps.ColorInfo(options.Width, options.Height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? throw new IOException("The image could not be resized.");
        if (options.Format is ExportFormat.Jpeg or ExportFormat.Pdf || !options.Transparency)
        {
            using var canvas = new SKCanvas(pixels);
            using var paint = new SKPaint { Color = options.Background ?? SKColors.White, BlendMode = SKBlendMode.DstOver };
            canvas.DrawPaint(paint);
        }
        byte[] bytes;
        using var image = SKImage.FromBitmap(pixels);
        if (options.CmykProfile is not null) bytes = CmykColor.Encode(pixels, options);
        else if (options.Format == ExportFormat.Exr) bytes = DeepImageCodec.EncodeExr(pixels);
        else if (options.Format == ExportFormat.Tiff || options.Format == ExportFormat.Png && (document.Image.ColorProfile is not null || document.Image.Depth > 8 || document.Image.Mode != Compositor.Core.Format.ImageMode.RGB || original.ColorType == SKColorType.RgbaF32))
            bytes = DeepImageCodec.EncodeMode(pixels, document.Image with { Mode = options.NativeColorMode && options.Format == ExportFormat.Tiff ? document.Image.Mode : Compositor.Core.Format.ImageMode.RGB, Depth = Math.Max(document.Image.Depth, document.Layers.Select(l => l.Asset?.BitDepth ?? 8).DefaultIfEmpty(8).Max()) }, options.Format == ExportFormat.Tiff, options.Resolution);
        else if (options.Format == ExportFormat.Jpeg && document.Image.ColorProfile is { } profile && IccProfiles.Model(profile) == "RGB")
        {
            using var raw = DeepImageCodec.Magick(pixels); using var converted = ColorTransforms.Convert(raw, ImageMagick.ColorProfiles.SRGB.ToByteArray(), profile); converted.Depth = 8; converted.Quality = (uint)options.Quality;
            converted.Density = new ImageMagick.Density(options.Resolution, options.Resolution, ImageMagick.DensityUnit.PixelsPerInch); bytes = converted.ToByteArray(ImageMagick.MagickFormat.Jpeg);
        }
        else if (options.Format == ExportFormat.Pdf)
        {
            using var stream = new MemoryStream();
            using (var pdf = SKDocument.CreatePdf(stream))
            {
                var width = (float)(options.Width * 72 / options.Resolution);
                var height = (float)(options.Height * 72 / options.Resolution);
                var page = pdf.BeginPage(width, height);
                page.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear)); pdf.EndPage(); pdf.Close();
            }
            bytes = stream.ToArray();
        }
        else
        {
            using var data = image.Encode(options.Format == ExportFormat.Jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, options.Quality)
                ?? throw new IOException("The image encoder failed.");
            bytes = data.ToArray();
        }
        // JPEG preview includes actual compression artifacts. PDF is a single flattened image page.
        using var decoded = options.CmykProfile is not null ? CmykColor.Proof(pixels, options.CmykProfile) : options.Format == ExportFormat.Jpeg ? SKBitmap.Decode(bytes) : null;
        var source = decoded ?? pixels;
        var factor = Math.Min(1, 520.0 / Math.Max(source.Width, source.Height));
        var preview = source.Resize(Bitmaps.ColorInfo(Math.Max(1, (int)(source.Width * factor)), Math.Max(1, (int)(source.Height * factor))),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? throw new IOException("The preview could not be resized.");
        return new ExportResult(bytes, preview);
    }
}
