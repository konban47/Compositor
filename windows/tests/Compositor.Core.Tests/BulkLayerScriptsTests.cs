using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class BulkLayerScriptsTests : ProjectTestBase
{
    [Fact]
    public void CleaningEmptyLayersKeepsClippingBasesAndTheirSourceChain()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 2, 2);
        var source = Layer(SKColors.Transparent);
        var middle = Layer(SKColors.Transparent);
        var painted = Layer(SKColors.Red);
        middle.MaskSourceID = source.ID;
        painted.MaskSourceID = middle.ID;
        document.Layers.AddRange([source, middle, painted, Layer(SKColors.Transparent)]);

        Assert.Equal(1, LayerWorkflow.Clean(document, "Empty"));
        Assert.Equal(3, document.Layers.Count);
        Assert.Equal(source.ID, middle.MaskSourceID);
        Assert.Equal(middle.ID, painted.MaskSourceID);
    }

    [Fact]
    public void CleaningEmptyLayersPreservesLockedLayers()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 2, 2);
        var layer = Layer(SKColors.Transparent);
        layer.Locks = LayerLocks.All;
        document.Layers.Add(layer);
        Assert.Equal(0, LayerWorkflow.Clean(document, "Empty"));
        Assert.Single(document.Layers);
    }

    [Fact]
    public void ApplyingDisabledMaskKeepsVisiblePixelsAndAsset()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 2, 2);
        var layer = Layer(SKColors.Red);
        var original = layer.Asset;
        layer.Mask = new LayerMask(MaskAsset(2, 2, 0), isEnabled: false);
        document.Layers.Add(layer);
        Assert.True(MaskProperties.Apply(document, layer.ID));
        Assert.Null(layer.Mask);
        Assert.Same(original, layer.Asset);
        Assert.Equal(SKColors.Red, layer.Asset!.Image.GetPixel(0, 0));
    }

    [Fact]
    public void ApplyingMaskToLockedLayerDoesNotChangeIt()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 2, 2);
        var layer = Layer(SKColors.Red);
        var original = layer.Asset;
        var mask = new LayerMask(MaskAsset(2, 2, 0));
        layer.Mask = mask;
        layer.Locks = LayerLocks.All;
        document.Layers.Add(layer);
        Assert.False(MaskProperties.Apply(document, layer.ID));
        Assert.Same(mask, layer.Mask);
        Assert.Same(original, layer.Asset);
    }

    [Fact]
    public void ApplyingMaskBakesEveryGifFrameAndPreservesTimingAndSelection()
    {
        var source = new GifSource(GifAnimationTests.Sample());
        var image = source.Decode(0, "Animation"); image.Animation = new GifAnimation(source);
        using var document = Compositor.Core.IO.ImageImporter.NewDocument(image);
        var layer = Assert.Single(document.Layers); layer.ShowGifFrame(1);
        var delays = layer.Gif!.Frames.Select(frame => layer.Gif.Delay(frame.Index)).ToArray();
        layer.Mask = new LayerMask(MaskAsset(12, 10, 128));
        Assert.True(MaskProperties.Apply(document, layer.ID));
        Assert.Null(layer.Mask); Assert.Equal(1, layer.GifFrameIndex);
        Assert.Equal(delays, layer.Gif!.Frames.Select(frame => layer.Gif.Delay(frame.Index)));
        foreach (var frame in layer.Gif.Frames)
        {
            layer.ShowGifFrame(frame.Index);
            Assert.InRange(layer.Asset!.Image.GetPixel(4, 4).Alpha, (byte)127, (byte)129);
        }
    }

    private static ImageLayer Layer(SKColor color) => new(Guid.NewGuid(), Asset(2, 2, color), new LayerTransform(0, 0, 2, 2), "Layer");
}
