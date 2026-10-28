using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class WebExportTests
{
    [Theory]
    [InlineData(WebFormat.Gif)]
    [InlineData(WebFormat.Png8)]
    public void PaletteTransparencyIsAppliedToEncodedPixels(WebFormat format)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var bitmap = new SKBitmap(8, 8);
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++) bitmap.SetPixel(x, y, x < 4 ? SKColors.Red : SKColors.Blue);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Palette"), new(0, 0, 8, 8), "Palette"));
        var options = new WebExportOptions(8, 8, format, Colors: 3, Palette: WebPalette.Custom,
            Dither: WebDither.None, CustomPalette: [(uint)SKColors.Red, (uint)SKColors.Blue],
            LockedColors: [(uint)SKColors.Red], TransparentColors: [(uint)SKColors.Red]);
        using var result = WebExport.Encode(document, options);
        using var decoded = SKBitmap.Decode(result.Bytes);
        Assert.Equal(0, decoded.GetPixel(1, 1).Alpha);
        Assert.Equal(SKColors.Blue, decoded.GetPixel(6, 1));
        Assert.Equal(32, result.PaletteUsage[(uint)SKColors.Transparent]);
        Assert.Equal(32, result.PaletteUsage[(uint)SKColors.Blue]);
        using var opaque = WebExport.Encode(document, options with { Transparency = false });
        Assert.Equal(SKColors.Red, opaque.Preview.GetPixel(1, 1));
        Assert.All(document.Layers[0].Asset!.Image.Pixels, color => Assert.Equal(255, color.Alpha));
    }

    [Fact]
    public void OptimizingRetainsLockedColorsAndPaletteBudget()
    {
        using var document = Document();
        uint[] locked = [(uint)SKColors.Red, (uint)SKColors.Blue, (uint)SKColors.Lime];
        var options = new WebExportOptions(32, 24, Colors: 16, LockedColors: locked);
        using var result = WebExport.Optimize(document, options, 1);
        Assert.Equal(4, result.Options.Colors);
        Assert.All(locked, color => Assert.Contains(color, result.Palette));
        Assert.Equal(32 * 24, result.PaletteUsage.Values.Sum());
        Assert.InRange(result.Palette.Length, 1, result.Options.Colors);
        Assert.Throws<ArgumentException>(() => WebExport.Encode(document, options with { Colors = 3 }));
    }

    private static CanvasDocument Document()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 32, 24);
        var bitmap = new SKBitmap(32, 24);
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 32; x++) bitmap.SetPixel(x, y, new SKColor((byte)(x * 8), (byte)(y * 10), 75, x < 8 ? (byte)0 : (byte)255));
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Test"), new(0, 0, 32, 24), "Test"));
        return document;
    }

    [Theory]
    [InlineData(WebFormat.Gif, MagickFormat.Gif)]
    [InlineData(WebFormat.Jpeg, MagickFormat.Jpeg)]
    [InlineData(WebFormat.Png8, MagickFormat.Png)]
    [InlineData(WebFormat.Png24, MagickFormat.Png)]
    [InlineData(WebFormat.Wbmp, MagickFormat.Wbmp)]
    public void EncodesRealFormatAtRequestedSize(WebFormat format, MagickFormat expected)
    {
        using var document = Document();
        using var result = WebExport.Encode(document, new(16, 12, format));
        using var decoded = new MagickImage(result.Bytes, format == WebFormat.Wbmp ? MagickFormat.Wbmp : MagickFormat.Unknown);
        Assert.Equal(expected, decoded.Format);
        Assert.Equal(16u, decoded.Width); Assert.Equal(12u, decoded.Height);
        Assert.Equal(16, result.Preview.Width); Assert.Equal(12, result.Preview.Height);
        Assert.Equal(32, document.Width);
    }

    [Theory]
    [InlineData(WebFormat.Gif)]
    [InlineData(WebFormat.Png8)]
    public void IndexedOutputHonorsColorBudgetIncludingTransparency(WebFormat format)
    {
        using var document = Document();
        using var result = WebExport.Encode(document, new(32, 24, format, Colors: 2, Dither: WebDither.None));
        using var rendered = Compositor.Core.Rendering.DocumentRenderer.Render(document);
        Assert.Equal(0, rendered.GetPixel(0, 0).Alpha);
        using var raw = DeepImageCodec.Magick(rendered);
        using var roundTrip = SKBitmap.Decode(raw.ToByteArray(MagickFormat.Png));
        Assert.Equal(0, roundTrip.GetPixel(0, 0).Alpha);
        using var decoded = new MagickImage(result.Bytes, format == WebFormat.Wbmp ? MagickFormat.Wbmp : MagickFormat.Unknown);
        Assert.InRange(decoded.TotalColors, 1u, 2u);
        using var direct = SKBitmap.Decode(result.Bytes);
        Assert.Equal(0, direct.GetPixel(0, 0).Alpha);
        Assert.Equal(0, result.Preview.GetPixel(0, 0).Alpha);
        Assert.Equal(255, result.Preview.GetPixel(31, 23).Alpha);
    }

    [Fact]
    public void OpaqueOutputCompositesAgainstRequestedMatte()
    {
        using var document = Document();
        using var result = WebExport.Encode(document, new(32, 24, WebFormat.Png24, Transparency: false, Matte: SKColors.Lime));
        Assert.Equal(SKColors.Lime, result.Preview.GetPixel(0, 0));
    }

    [Theory]
    [InlineData(WebDither.None)]
    [InlineData(WebDither.Diffusion)]
    [InlineData(WebDither.Pattern)]
    [InlineData(WebDither.Noise)]
    public void CustomPaletteRestrictsPixelsAndIsDeterministic(WebDither dither)
    {
        using var document = Document();
        uint[] palette = [(uint)SKColors.Black, (uint)SKColors.White, (uint)SKColors.Red];
        var options = new WebExportOptions(32, 24, WebFormat.Png8, Colors: 3, Palette: WebPalette.Custom,
            Dither: dither, Transparency: false, CustomPalette: palette);
        using var a = WebExport.Encode(document, options); using var b = WebExport.Encode(document, options);
        Assert.Equal(a.Bytes, b.Bytes);
        Assert.All(a.Preview.Pixels, p => Assert.Contains((uint)p, palette));
    }

    [Fact]
    public void OptimizeReturnsSmallestAttemptWhenTargetCannotBeReached()
    {
        using var document = Document();
        var options = new WebExportOptions(32, 24, WebFormat.Jpeg, Quality: 5);
        using var initial = WebExport.Encode(document, options);
        using var result = WebExport.Optimize(document, options, 1);
        Assert.True(result.Bytes.Length <= initial.Bytes.Length);
        Assert.InRange(result.Options.Quality, 1, 5);
        Assert.True(result.Bytes.Length > 1);
    }

    [Fact]
    public void CancellationAndInvalidOptionsDoNotAlterDocument()
    {
        using var document = Document(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WebExport.Encode(document, new(32, 24), cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebExport.Encode(document, new(32, 24, Colors: 257)));
        Assert.Single(document.Layers); Assert.Equal(32, document.Width);
    }
}
