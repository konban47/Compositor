using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class LayerDeletionTests : ProjectTestBase
{
    [Fact]
    public void BakeUsesSourceTransformOpacityAndMaskWithoutSquaringTheTargetsOwnMask()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 4, 1);
        var source = new ImageLayer(Guid.NewGuid(), Asset(4, 1, SKColors.Blue), new LayerTransform(0, 0, 4, 1), "Source") { Opacity = .5 };
        source.Asset!.Image.SetPixel(2, 0, SKColors.Transparent);
        source.Mask = new LayerMask(MaskAsset(4, 1));
        source.Mask.Asset.Image.GetPixelSpan()[1] = 128;
        var target = new ImageLayer(Guid.NewGuid(), Asset(3, 1, new SKColor(255, 0, 0, 128)), new LayerTransform(1, 0, 3, 1), "Target")
        { MaskSourceID = source.ID, Mask = new LayerMask(MaskAsset(3, 1, 80)), Opacity = .7 };
        var original = target.Asset;
        var ownMask = target.Mask;
        document.Layers.AddRange([source, target]);
        var history = new DocumentHistory();
        history.Begin("Delete Layer", document, source.ID);
        Assert.True(LayerDeletion.Delete(document, [source.ID], ClippingDeleteChoice.Bake));
        history.End(document, target.ID);
        Assert.Single(document.Layers);
        Assert.Null(target.MaskSourceID);
        Assert.Same(ownMask, target.Mask);
        Assert.Equal(.7, target.Opacity);
        Assert.Equal(new byte[] { 32, 0, 64 }, Enumerable.Range(0, 3).Select(x => target.Asset!.Image.GetPixel(x, 0).Alpha));
        Assert.Equal(128, original!.Image.GetPixel(0, 0).Alpha);
        var undo = history.Undo()!.Value.Document!;
        Assert.Equal(2, undo.Layers.Count);
        Assert.Same(original, undo.Layers[1].Asset);
        Assert.Equal(source.ID, undo.Layers[1].MaskSourceID);
    }

    [Theory]
    [InlineData(ClippingDeleteChoice.Cancel, false)]
    [InlineData(ClippingDeleteChoice.Release, true)]
    public void CancelLeavesTheWholeDocumentAndReleaseLeavesTheOriginalPixels(ClippingDeleteChoice choice, bool changed)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 2, 2);
        var source = new ImageLayer(Guid.NewGuid(), Asset(2, 2, SKColors.Blue), new LayerTransform(0, 0, 2, 2), "Source");
        var target = new ImageLayer(Guid.NewGuid(), Asset(2, 2, SKColors.Red), source.Transform, "Target") { MaskSourceID = source.ID };
        document.Layers.AddRange([source, target]);
        var pixels = target.Asset;
        Assert.True(LayerDeletion.HasDependents(document, [source.ID]));
        Assert.Equal(changed, LayerDeletion.Delete(document, [source.ID], choice));
        Assert.Equal(changed ? 1 : 2, document.Layers.Count);
        Assert.Same(pixels, target.Asset);
        Assert.Equal(changed ? null : source.ID, target.MaskSourceID);
    }

    [Fact]
    public void DeletingBothSourceAndDependentIsOneAtomicOperation()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 2, 2);
        var source = new ImageLayer(Guid.NewGuid(), Asset(2, 2, SKColors.Blue), new LayerTransform(0, 0, 2, 2), "Source");
        var target = new ImageLayer(Guid.NewGuid(), Asset(2, 2, SKColors.Red), source.Transform, "Target") { MaskSourceID = source.ID };
        document.Layers.AddRange([source, target]);
        Assert.False(LayerDeletion.HasDependents(document, [source.ID, target.ID]));
        Assert.True(LayerDeletion.Delete(document, [source.ID, target.ID], ClippingDeleteChoice.Cancel));
        Assert.Empty(document.Layers);
    }
}
