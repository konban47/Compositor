using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class ArtboardBackgroundTests : ProjectTestBase
{
    [Theory]
    [InlineData(0xff123456u)]
    [InlineData(0u)]
    [InlineData(0x80123456u)]
    public void BackgroundSurvivesCopiesAndProjectRoundTrip(uint color)
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 100, 80);
        doc.Layers.Add(new(Guid.NewGuid(), null, new(10, 10, 40, 40), "Board") { IsGroup = true, Container = LayerContainer.Artboard, ArtboardBackground = color });
        var snapshot = ProjectSnapshot.FromDocument(doc);
        Assert.Equal(22, snapshot.Manifest.Version);
        var path = PathIn("colored.comp"); ProjectStore.Save(snapshot, path);
        using var loaded = ProjectStore.Load(path).ToDocument();
        using var copy = DocumentCopies.Independent(loaded);
        Assert.Equal(color, copy.Layers[0].ArtboardBackground);
        using var image = DocumentRenderer.Render(copy);
        Assert.Equal(new SKColor(color).Alpha, image.GetPixel(25, 25).Alpha);
        Assert.Equal(0, image.GetPixel(75, 65).Alpha);
        if (new SKColor(color).Alpha == 255) Assert.Equal(new SKColor(color), image.GetPixel(25, 25));
    }

    [Fact]
    public void DefaultBackgroundRetainsOlderFormatAndLoadsWhite()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 60, 60);
        doc.Layers.Add(new(Guid.NewGuid(), null, new(0, 0, 60, 60), "Board") { IsGroup = true, Container = LayerContainer.Artboard });
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(15, snapshot.Manifest.Version);
        Assert.Null(snapshot.Manifest.Layers[0].ArtboardBackground);
        var path = PathIn("old.comp"); ProjectStore.Save(snapshot, path);
        using var loaded = ProjectStore.Load(path).ToDocument();
        Assert.Equal(0xffffffffu, loaded.Layers[0].ArtboardBackground);
        using var image = DocumentRenderer.Render(loaded); Assert.Equal(SKColors.White, image.GetPixel(20, 20));
    }
}
