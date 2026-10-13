using System.Runtime.InteropServices;
using System.Text;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class Upstream15Tests
{
    private static void Fill(SKBitmap image, float r, float g, float b)
    {
        var span = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan());
        for (var i = 0; i < span.Length; i += 4) { span[i] = r; span[i + 1] = g; span[i + 2] = b; span[i + 3] = 1; }
    }
    [Theory]
    [InlineData(ExportFormat.Jpeg)] [InlineData(ExportFormat.Tiff)] [InlineData(ExportFormat.Pdf)]
    public void CmykExportEmbedsItsProfileWithoutChangingTheDocument(ExportFormat format)
    {
        using var bitmap = new SKBitmap(Bitmaps.ColorInfo(32, 16)); bitmap.Erase(new SKColor(20, 190, 240));
        using var doc = ImageImporter.NewDocument(ImportedImage.Create(bitmap.Copy(), "Cyan"));
        var original = doc.Layers.Single().Asset; var profile = CmykColor.DefaultProfile;
        using var encoded = ExportImage.Encode(doc, new(format, 32, 16, CmykProfile: profile));
        if (format == ExportFormat.Pdf)
        { var pdf = Encoding.Latin1.GetString(encoded.Bytes); Assert.Contains("/ICCBased", pdf); Assert.Contains("/N 4", pdf); Assert.Contains("/MediaBox [0 0 32 16]", pdf); }
        else
        { using var image = new MagickImage(encoded.Bytes); Assert.Equal(ColorSpace.CMYK, image.ColorSpace); Assert.NotNull(image.GetColorProfile()); }
        Assert.Same(original, doc.Layers.Single().Asset); Assert.Equal(ImageMode.RGB, doc.Image.Mode);
        using var proof = CmykColor.Proof(bitmap, profile); Assert.Equal(32, proof.Width); Assert.Equal((byte)255, proof.GetPixel(0, 0).Alpha);
    }
    [Fact]
    public void ExportDefaultsToRgbEvenWhenDocumentHasAnInkProfile()
    {
        using var pixels = new SKBitmap(Bitmaps.ColorInfo(2, 2)); pixels.Erase(SKColors.Cyan);
        using var doc = ImageImporter.NewDocument(ImportedImage.Create(pixels.Copy(), "Ink")); doc.Image = new() { Mode = ImageMode.CMYK, CmykProfile = CmykColor.DefaultProfile };
        using var result = ExportImage.Encode(doc, new(ExportFormat.Tiff, 2, 2)); using var image = new MagickImage(result.Bytes);
        Assert.NotEqual(ColorSpace.CMYK, image.ColorSpace);
        using var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(20, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version = 19; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact]
    public void ProgressiveBlurInterpolatesRadiusAndKeepsFloatSamples()
    {
        using var pixels = new SKBitmap(DeepPixels.Info(64, 64)); var values = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) { var at = (y * 64 + x) * 4; values[at] = values[at + 1] = values[at + 2] = x % 2 == 0 ? .12345f : .98765f; values[at + 3] = 1; }
        var settings = new FilterSettings { ProgressiveFrom = 0, ProgressiveTo = 8, ProgressiveStartY = 20, ProgressiveEndY = 80 };
        using var blurred = ProgressiveBlur.Apply(pixels, settings); var result = MemoryMarshal.Cast<byte, float>(blurred.GetPixelSpan());
        Assert.Equal(values[4 * 10], result[4 * 10]); Assert.True(Math.Abs(result[(60 * 64 + 10) * 4] - result[(60 * 64 + 11) * 4]) < .05);
        settings.ProgressiveInverted = true; using var inverted = ProgressiveBlur.Apply(pixels, settings); Assert.Equal(SKColorType.RgbaF32, inverted.ColorType);
        var inverse = MemoryMarshal.Cast<byte, float>(inverted.GetPixelSpan()); Assert.Equal(values[(60 * 64 + 10) * 4], inverse[(60 * 64 + 10) * 4]);
    }
    [Fact]
    public void DeepRegionMatchesItsFullPrecisionComposite()
    {
        using var pixels = new SKBitmap(DeepPixels.Info(64, 32)); Fill(pixels, .33333f, .45678f, .98765f);
        using var doc = ImageImporter.NewDocument(ImportedImage.CreateDeep(pixels.Copy(), "Deep"));
        using var full = DocumentRenderer.RenderDeep(doc); using var piece = DocumentRenderer.RenderDeep(doc, new SKRectI(12, 10, 28, 20));
        Assert.Equal(16, piece.Width); Assert.Equal(10, piece.Height);
        var a = MemoryMarshal.Cast<byte, float>(full.GetPixelSpan()); var b = MemoryMarshal.Cast<byte, float>(piece.GetPixelSpan()); Assert.Equal(a[10 * full.RowBytes / 4 + 12 * 4], b[0]);
    }
    [Fact]
    public void MaskRefinementKeepsTheOriginalAndDeepColorPrecision()
    {
        using var pixels = new SKBitmap(DeepPixels.Info(16, 8)); Fill(pixels, .333333f, .456789f, .678912f);
        using var doc = ImageImporter.NewDocument(ImportedImage.CreateDeep(pixels.Copy(), "Deep", 32)); var layer = doc.Layers[0];
        var mask = new SKBitmap(Bitmaps.MaskInfo(16, 8)); mask.Erase(new SKColor(128,128,128)); layer.Mask = Compositor.Core.Model.LayerMask.AssetFrom(mask); var original = layer.Mask.Asset;
        Assert.True(MaskRefinement.Apply(doc, layer.ID, new(Contrast: 20, Decontaminate: true)));
        Assert.Equal(32, layer.Asset!.BitDepth); Assert.NotSame(original, layer.Mask.Asset); Assert.Equal((byte)128, original.Image.GetPixel(0, 0).Red);
        Assert.Equal(.333333f, MemoryMarshal.Cast<byte, float>(layer.Asset.Deep!.GetPixelSpan())[0], 5);
        Assert.False(MaskRefinement.Apply(doc, layer.ID, new(Radius: double.NaN)));
    }
}
