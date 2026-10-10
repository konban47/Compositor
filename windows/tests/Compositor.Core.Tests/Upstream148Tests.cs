using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class Upstream148Tests
{
    private static CanvasDocument Document()
    {
        var doc = new CanvasDocument(Guid.NewGuid(), 32, 24);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(32, 24)); pixels.Erase(new SKColor(200, 120, 60, 128));
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Image"), new LayerTransform(0, 0, 32, 24), "Image"));
        return doc;
    }
    [Theory]
    [InlineData(ExportFormat.Png)] [InlineData(ExportFormat.Jpeg)] [InlineData(ExportFormat.Pdf)]
    public void ExportUsesRequestedSizeAndDoesNotChangeDocument(ExportFormat format)
    {
        using var doc = Document(); var before = doc.Clone();
        using var output = ExportImage.Encode(doc, new ExportOptions(format, 16, 12));
        Assert.True(doc.SameAs(before)); Assert.Equal(16, output.Preview.Width); Assert.Equal(12, output.Preview.Height);
        if (format == ExportFormat.Pdf) Assert.True(output.Bytes.AsSpan().StartsWith("%PDF-"u8));
        else
        {
            using var decoded = SKBitmap.Decode(output.Bytes); Assert.Equal(16, decoded.Width); Assert.Equal(12, decoded.Height);
            Assert.Equal(format == ExportFormat.Png ? 128 : 255, decoded.GetPixel(5, 5).Alpha);
            if (format == ExportFormat.Jpeg) Assert.True(decoded.GetPixel(5, 5).Blue > 140); // White flattened background.
        }
    }
    [Fact]
    public void PngBackgroundAndLimitsAreApplied()
    {
        using var doc = Document();
        using var output = ExportImage.Encode(doc, new ExportOptions(ExportFormat.Png, 32, 24, Transparency: false, Background: SKColors.Black));
        using var decoded = SKBitmap.Decode(output.Bytes); Assert.Equal(255, decoded.GetPixel(5, 5).Alpha); Assert.InRange(decoded.GetPixel(5, 5).Red, 98, 103);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExportImage.Encode(doc, new ExportOptions(ExportFormat.Png, 30000, 30000)));
    }
    [Fact]
    public void ScanlinesRespectSelectionAndAlphaAndCanBeUndone()
    {
        using var doc = Document(); var id = doc.Layers[0].ID; var original = doc.Layers[0].Asset!;
        SelectionEdits.Select(doc, SKRectI.Create(8, 6, 16, 12)); var history = new DocumentHistory();
        history.Begin("Scanlines", doc, id);
        Assert.True(FilterEdits.Apply(doc, id, FilterKind.Scanlines, new FilterSettings())); history.End(doc, id);
        var image = doc.Layers[0].Asset!.Image;
        var outside = image.GetPixel(2, 2); var expected = original.Image.GetPixel(2, 2);
        // Raster filters round-trip through premultiplied alpha, with at most one straight-color LSB here.
        Assert.InRange(Math.Abs(outside.Red - expected.Red), 0, 1);
        Assert.Equal(expected.Green, outside.Green); Assert.Equal(expected.Blue, outside.Blue); Assert.Equal(expected.Alpha, outside.Alpha);
        Assert.Equal(128, image.GetPixel(10, 10).Alpha); Assert.NotEqual(original.Image.GetPixel(10, 10), image.GetPixel(10, 10));
        doc.Adopt(history.Undo()!.Value.Document!); Assert.Same(original, doc.Layers[0].Asset);
    }
    [Fact]
    public void ScanlineSettingsAreIndependentAndSupportFullDisplacementRange()
    {
        var settings = new FilterSettings(); var copy = settings.Copy(); copy.Scanlines.Displace = -100; copy.Scanlines.BlackLevel = 100;
        Assert.True(copy.IsValid(FilterKind.Scanlines)); Assert.Equal(0, settings.Scanlines.Displace);
        using var doc = Document(); Assert.True(FilterEdits.Apply(doc, doc.Layers[0].ID, FilterKind.Scanlines, copy));
        copy.Scanlines.Displace = 101; Assert.False(copy.IsValid(FilterKind.Scanlines));
    }
    [Fact]
    public void MeasuredRawNeutralBalanceReducesCastAndKeepsAlpha()
    {
        var solution = CameraRawTables.Neutralize(.6, .5, .4); Assert.NotNull(solution);
        byte[] pixels = [153, 128, 102, 255, 30, 40, 50, 80];
        AdjustPixels.CameraRawMeasured(pixels, 2, 1, 8, new CameraRawSettings { Temperature = solution.Value.Temperature, Tint = solution.Value.Tint });
        Assert.True(pixels.Take(3).Max() - pixels.Take(3).Min() < 15);
        Assert.Equal(80, pixels[7]); Assert.Null(CameraRawTables.Neutralize(0, .5, .5));
    }

    [Theory]
    [InlineData(-100)] [InlineData(100)]
    public void LatestDehazeLeavesUniformColorUnchanged(double amount)
    {
        using var doc = Document(); var layer = doc.Layers[0]; var before = layer.Asset!.Image.GetPixel(5, 5);
        CameraRawEdits.Apply(doc, layer.ID, new CameraRawSettings { Dehaze = amount });
        var after = doc.Layers[0].Asset!.Image.GetPixel(5, 5);
        Assert.InRange(Math.Abs(before.Red - after.Red), 0, 2); Assert.InRange(Math.Abs(before.Green - after.Green), 0, 2);
        Assert.Equal(before.Alpha, after.Alpha);
    }

    [Fact]
    public void AdaptiveContrastChangesWithSurroundingBrightness()
    {
        static byte[] Row(byte background) => [background, background, background, 255, background, background, background, 255, 120, 100, 80, 255];
        var dark = Row(20); var light = Row(240);
        var darkStats = CameraRawTables.Statistics(dark, 3, 1, 12); var lightStats = CameraRawTables.Statistics(light, 3, 1, 12);
        Assert.True(darkStats.Brightest < lightStats.Brightest);
        AdjustPixels.CameraRawMeasured(dark, 3, 1, 12, new CameraRawSettings { Contrast = 100 });
        AdjustPixels.CameraRawMeasured(light, 3, 1, 12, new CameraRawSettings { Contrast = 100 });
        Assert.False(dark.AsSpan(8, 3).SequenceEqual(light.AsSpan(8, 3))); Assert.Equal(255, dark[11]);
    }
}
