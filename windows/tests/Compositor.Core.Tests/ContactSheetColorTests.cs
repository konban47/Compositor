using System.Runtime.InteropServices;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class ContactSheetColorTests : ProjectTestBase
{
    private static ContactSheetOptions Options(ImageMode mode = ImageMode.RGB, int depth = 8) =>
        new(2, 2, 72, 1, 1, HorizontalSpacing: 0, VerticalSpacing: 0, Captions: false, Mode: mode, Depth: depth);

    [Theory]
    [InlineData(ImageMode.Grayscale, 8)]
    [InlineData(ImageMode.Grayscale, 16)]
    [InlineData(ImageMode.RGB, 16)]
    [InlineData(ImageMode.CMYK, 16)]
    [InlineData(ImageMode.Lab, 16)]
    public void SelectedModeDepthAndProfileReachTheGeneratedDocument(ImageMode mode, int depth)
    {
        using var source = ImageImporter.NewDocument(Asset(2, 2, SKColors.Red));
        using var output = Assert.Single(FileAutomation.ContactSheets([new("source", Document: source)], Options(mode, depth)));
        Assert.Equal(mode, output.Image.Mode); Assert.Equal(depth, output.Image.Depth); Assert.True(output.Image.IsValid);
        Assert.Equal(depth, output.Layers[0].Asset!.BitDepth);
        if (mode == ImageMode.CMYK) Assert.Equal("CMYK", IccProfiles.Model(output.Image.CmykProfile!));
        if (mode == ImageMode.Grayscale)
        {
            Assert.Equal("GRAY", IccProfiles.Model(output.Image.ColorProfile!));
            var color = output.Layers[0].Asset!.Image.GetPixel(0, 0);
            Assert.Equal(color.Red, color.Green); Assert.Equal(color.Red, color.Blue);
        }
        var snapshot = ProjectSnapshot.FromDocument(output); ProjectStore.Validate(snapshot.Manifest);
    }

    [Fact]
    public void SixteenBitContactSheetDoesNotPassThroughEightBitThumbnails()
    {
        var pixels = new SKBitmap(DeepPixels.Info(2, 2));
        var values = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
        for (var index = 0; index < values.Length; index += 4)
        { values[index] = .50002f; values[index + 1] = .20002f; values[index + 2] = .30002f; values[index + 3] = 1; }
        using var source = ImageImporter.NewDocument(ImportedImage.CreateDeep(pixels, "Precise", 32));
        using var output = Assert.Single(FileAutomation.ContactSheets([new("source", Document: source)], Options(depth: 16)));
        var actual = MemoryMarshal.Cast<byte, float>(output.Layers[0].Asset!.WorkingImage.GetPixelSpan())[0];
        Assert.InRange(actual, .50000f, .50004f);
        Assert.True(Math.Abs(actual - 128f / 255) > .001f);
    }

    [Fact]
    public void FolderGroupingStartsNewPagesWithoutMutatingSources()
    {
        using var source = ImageImporter.NewDocument(Asset(2, 2, SKColors.Red));
        var sources = new[] { new AutomationSource("a", PathIn("one/a.png"), source), new AutomationSource("b", PathIn("two/b.png"), source) };
        var pages = FileAutomation.ContactSheets(sources, Options() with { Width = 4, Columns = 2, GroupByFolder = true, Flatten = false });
        try
        {
            Assert.Equal(2, pages.Count);
            Assert.All(pages, page => Assert.Equal(2, page.Layers.Count));
            Assert.Single(source.Layers);
        }
        finally { foreach (var page in pages) page.Dispose(); }
    }

    [Fact]
    public void ProfileForAnotherColorModelIsRejected()
    {
        using var source = ImageImporter.NewDocument(Asset(2, 2, SKColors.Red));
        Assert.Throws<ArgumentException>(() => FileAutomation.ContactSheets([new("source", Document: source)],
            Options(ImageMode.CMYK) with { ColorProfile = "sRGB IEC61966-2.1" }));
    }
}
