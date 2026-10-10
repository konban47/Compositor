using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.IO;

public enum ExportFormat { Png, Jpeg, Pdf }
public sealed record ExportOptions(ExportFormat Format, int Width, int Height, int Quality = 90,
    bool Transparency = true, SKColor? Background = null, double Resolution = 72)
{
    public string Extension => Format switch { ExportFormat.Jpeg => "jpg", ExportFormat.Pdf => "pdf", _ => "png" };
    public bool IsValid => Enum.IsDefined(Format) && Width > 0 && Height > 0
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
        // Resize the complete composite, so rotated layers, masks and effects scale together.
        using var original = DocumentRenderer.Render(document);
        using var pixels = original.Resize(Bitmaps.ColorInfo(options.Width, options.Height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? throw new IOException("The image could not be resized.");
        if (options.Format != ExportFormat.Png || !options.Transparency)
        {
            using var canvas = new SKCanvas(pixels);
            using var paint = new SKPaint { Color = options.Background ?? SKColors.White, BlendMode = SKBlendMode.DstOver };
            canvas.DrawPaint(paint);
        }
        byte[] bytes;
        using var image = SKImage.FromBitmap(pixels);
        if (options.Format == ExportFormat.Pdf)
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
        using var decoded = options.Format == ExportFormat.Jpeg ? SKBitmap.Decode(bytes) : null;
        var source = decoded ?? pixels;
        var factor = Math.Min(1, 520.0 / Math.Max(source.Width, source.Height));
        var preview = source.Resize(Bitmaps.ColorInfo(Math.Max(1, (int)(source.Width * factor)), Math.Max(1, (int)(source.Height * factor))),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? throw new IOException("The preview could not be resized.");
        return new ExportResult(bytes, preview);
    }
}
