using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class FileAutomationTests
{
    private static CanvasDocument Pattern(int offsetX = 0, int offsetY = 0)
    {
        var document = Solid(SKColors.Transparent, 64, 48);
        var bitmap = document.Layers[0].Asset!.Image;
        var random = new Random(714);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++)
            {
                var color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                if (x + offsetX < 64 && y + offsetY < 48) bitmap.SetPixel(x + offsetX, y + offsetY, color);
            }
        return document;
    }

    [Fact]
    public void AlignedLayersRetainEdgesAndCanBeEmbeddedAsSmartObject()
    {
        using var first = Pattern(); using var second = Pattern(4, 2);
        AutomationSource[] sources = [new("First", Document: first), new("Second", Document: second)];
        using var aligned = FileAutomation.LoadLayers(sources, true, false);
        Assert.Equal(68, aligned.Width); Assert.Equal(50, aligned.Height);
        Assert.Equal(-4, aligned.Layers[1].Transform.X - aligned.Layers[0].Transform.X);
        Assert.Equal(-2, aligned.Layers[1].Transform.Y - aligned.Layers[0].Transform.Y);
        using var smart = FileAutomation.LoadLayers(sources, true, true);
        Assert.NotNull(Assert.Single(smart.Layers).SmartObject);
        Assert.Null(first.Layers[0].SmartObject);
        Assert.Equal(0, first.Layers[0].Transform.X);
    }

    [Fact]
    public void AlignedStatisticsUsesCommonAreaAndHonorsCancellation()
    {
        using var first = Pattern(); using var second = Pattern(4, 2);
        AutomationSource[] sources = [new("First", Document: first), new("Second", Document: second)];
        using var result = FileAutomation.Statistics(sources, StackStatistic.Mean, true);
        Assert.Equal(60, result.Width); Assert.Equal(46, result.Height);
        using var rendered = DocumentRenderer.Render(result);
        Assert.Equal(first.Layers[0].Asset!.Image.GetPixel(20, 15), rendered.GetPixel(20, 15));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileAutomation.Statistics(sources, StackStatistic.Mean, true, canceled.Token));
        Assert.Throws<OperationCanceledException>(() => FileAutomation.LoadLayers(sources, true, true, canceled.Token));
    }

    private static CanvasDocument Solid(SKColor color, int width = 8, int height = 6)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        var document = new CanvasDocument(Guid.NewGuid(), width, height);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Source"),
            new LayerTransform(0, 0, width, height), "Source"));
        return document;
    }

    [Theory]
    [InlineData(StackStatistic.Mean, 3)]
    [InlineData(StackStatistic.Sum, 12)]
    [InlineData(StackStatistic.Minimum, 1)]
    [InlineData(StackStatistic.Maximum, 5)]
    [InlineData(StackStatistic.Median, 3)]
    [InlineData(StackStatistic.Variance, 2)]
    [InlineData(StackStatistic.Skewness, 0)]
    [InlineData(StackStatistic.Kurtosis, 2)]
    [InlineData(StackStatistic.Range, 4)]
    [InlineData(StackStatistic.Entropy, 1.5)]
    public void StatisticsUsePopulationMomentsAndDoNotMutateSources(StackStatistic mode, double expected)
    {
        double[] input = [5, 3, 1, 3];
        Assert.Equal(expected, FileAutomation.Statistic(input, mode), 9);
        Assert.Equal(new double[] { 5, 3, 1, 3 }, input);
    }

    [Fact]
    public void FitPreservesAspectAndCanPreventEnlargement()
    {
        Assert.Equal((200, 100), FileAutomation.Fit(400, 200, 200, 200, false));
        Assert.Equal((40, 20), FileAutomation.Fit(40, 20, 200, 200, true));
        Assert.Throws<ArgumentException>(() => FileAutomation.Fit(0, 1, 20, 20, false));
    }

    [Fact]
    public void StackCentersDifferentSizesAndRetainsPixelsAfterSourcesClose()
    {
        using var red = Solid(SKColors.Red);
        var blue = Solid(SKColors.Blue, 4, 2);
        using var result = FileAutomation.LoadLayers([new("Red", Document: red), new("Blue", Document: blue)]);
        blue.Dispose();
        Assert.Equal((8, 6), (result.Width, result.Height));
        Assert.Equal(2, result.Layers.Count);
        Assert.Equal(2, result.Layers[1].Transform.X);
        Assert.Equal(2, result.Layers[1].Transform.Y);
        using var rendered = DocumentRenderer.Render(result);
        Assert.Equal(SKColors.Red, rendered.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, rendered.GetPixel(3, 3));
    }

    [Fact]
    public void ContactSheetsPaginateAndHonorColumnFirstOrder()
    {
        using var red = Solid(SKColors.Red, 10, 10);
        using var blue = Solid(SKColors.Blue, 10, 10);
        using var green = Solid(SKColors.Lime, 10, 10);
        var sources = new AutomationSource[] { new("Red", Document: red), new("Blue", Document: blue), new("Green", Document: green) };
        var pages = FileAutomation.ContactSheets(sources, new(20, 20, 72, 2, 2, true, 0, 0, Captions: false, Flatten: false));
        try
        {
            using var rendered = DocumentRenderer.Render(pages[0]);
            Assert.Equal(SKColors.Red, rendered.GetPixel(5, 5));
            Assert.Equal(SKColors.Blue, rendered.GetPixel(5, 15));
            Assert.Equal(SKColors.Lime, rendered.GetPixel(15, 5));
            Assert.Equal(SKColors.White, rendered.GetPixel(15, 15));
            Assert.Equal(4, pages[0].Layers.Count);
        }
        finally { foreach (var page in pages) page.Dispose(); }
        pages = FileAutomation.ContactSheets(sources, new(20, 20, 72, 1, 2, HorizontalSpacing: 0, VerticalSpacing: 0, Captions: false));
        try { Assert.Equal(2, pages.Count); Assert.All(pages, p => Assert.Single(p.Layers)); }
        finally { foreach (var page in pages) page.Dispose(); }
    }

    [Fact]
    public void ImageStatisticsComputePixelsAndRejectDifferentSizes()
    {
        using var dark = Solid(new SKColor(20, 40, 60));
        using var light = Solid(new SKColor(100, 120, 140));
        using var result = FileAutomation.Statistics([new("Dark", Document: dark), new("Light", Document: light)], StackStatistic.Mean);
        using var rendered = DocumentRenderer.Render(result);
        Assert.Equal(new SKColor(60, 80, 100), rendered.GetPixel(3, 3));
        using var wrongSize = Solid(SKColors.Black, 1, 1);
        Assert.Throws<ArgumentException>(() => FileAutomation.Statistics([new("Dark", Document: dark), new("Small", Document: wrongSize)], StackStatistic.Mean));
    }

    [Fact]
    public void CancellationDoesNotPublishPartialPdf()
    {
        string directory = Path.Combine(Path.GetTempPath(), "compositor-file-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "presentation.pdf");
        try
        {
            File.WriteAllText(path, "Existing document");
            using var source = Solid(SKColors.Red);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => FileAutomation.PdfPresentation(path,
                [new("Red", Document: source)], new(SKColors.White), cancellation.Token));
            Assert.Equal("Existing document", File.ReadAllText(path));
            FileAutomation.PdfPresentation(path, [new("Red", Document: source)], new(SKColors.White));
            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 8));
        }
        finally { Directory.Delete(directory, true); }
    }
}
