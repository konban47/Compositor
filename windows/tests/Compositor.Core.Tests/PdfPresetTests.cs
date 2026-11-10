using System.Text;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class PdfPresetTests
{
    private static CanvasDocument Picture()
    {
        var pixels = new SKBitmap(Bitmaps.ColorInfo(600, 400));
        for (var y = 0; y < 400; y++) for (var x = 0; x < 600; x++) pixels.SetPixel(x, y, new SKColor((byte)(x * 7 ^ y * 3), (byte)(y * 5), (byte)(x + y)));
        var doc = new CanvasDocument(Guid.NewGuid(), 600, 400);
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Noise"), new LayerTransform(0, 0, 600, 400), "Noise"));
        return doc;
    }

    [Fact]
    public void SmallestFileSizeDownsamplesAndCompressesHarderThanPrint()
    {
        using var doc = Picture();
        using var print = ExportImage.Encode(doc, new ExportOptions(ExportFormat.Pdf, 600, 400, Resolution: 600, Pdf: PdfPresets.BuiltIn[0]));
        using var small = ExportImage.Encode(doc, new ExportOptions(ExportFormat.Pdf, 600, 400, Resolution: 600, Pdf: PdfPresets.BuiltIn[2]));
        Assert.True(small.Bytes.Length * 3 < print.Bytes.Length, $"{small.Bytes.Length} vs {print.Bytes.Length}");
        // The page stays the same size: 600 pixels at 600 ppi is one inch, 72 points.
        var file = Path.GetTempFileName(); File.WriteAllBytes(file, small.Bytes);
        try { var page = Assert.Single(PdfImporter.Pages(file)); Assert.Equal(72, page.Width, 1); Assert.Equal(48, page.Height, 1); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void DownsamplingOnlyHappensAboveTheThreshold()
    {
        var preset = PdfPresets.BuiltIn[0];
        Assert.Equal((600, 400), preset.Downsampled(600, 400, 450));
        Assert.Equal((300, 200), preset.Downsampled(600, 400, 600));
        Assert.Equal((600, 400), (preset with { Downsample = PdfDownsample.None }).Downsampled(600, 400, 2400));
    }

    [Fact]
    public void PdfaPresetWritesArchivalMetadata()
    {
        using var doc = Picture();
        using var archive = ExportImage.Encode(doc, new ExportOptions(ExportFormat.Pdf, 600, 400, Resolution: 300, Pdf: PdfPresets.BuiltIn[3]));
        Assert.Contains("pdfaid", Encoding.ASCII.GetString(archive.Bytes));
    }

    [Fact]
    public void UserPresetsRoundTripAndInvalidFilesAreRefused()
    {
        var folder = Directory.CreateTempSubdirectory(); var path = Path.Combine(folder.FullName, "presets.json");
        try
        {
            var mine = PdfPresets.BuiltIn[1] with { Name = "Mine", BuiltIn = false, DownsampleTo = 200, DownsampleAbove = 250 };
            PdfPresets.Save([mine, PdfPresets.BuiltIn[0]], path);
            var read = Assert.Single(PdfPresets.Read(path));
            Assert.Equal(mine, read);
            File.WriteAllText(path, "[{\"Name\":\"Bad\",\"DownsampleTo\":600,\"DownsampleAbove\":100}]");
            Assert.Throws<InvalidDataException>(() => PdfPresets.Read(path));
        }
        finally { folder.Delete(true); }
    }
}
