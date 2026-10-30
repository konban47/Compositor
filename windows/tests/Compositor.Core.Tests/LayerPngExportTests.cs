using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Quick Export as PNG, one file a layer (upstream 0673a4a): what each file shows and what it is called.</summary>
public sealed class LayerPngExportTests
{
    private static ImageLayer Filled(string name, SKColor color, LayerTransform placed)
    {
        var bitmap = new SKBitmap(Bitmaps.ColorInfo((int)placed.Width, (int)placed.Height)); bitmap.Erase(color);
        return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, name), placed, name);
    }

    [Fact]
    public void IsolatingShowsTheLayerItsGroupsContentsAndClipsOnly()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 10, 10);
        var group = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 10, 10), "Group") { IsGroup = true, IsVisible = false };
        var target = Filled("Target", SKColors.Red, new(0, 0, 4, 4)); target.ParentID = group.ID; target.IsVisible = false;
        var clipped = Filled("Clipped", SKColors.Blue, new(0, 0, 4, 4)); clipped.ParentID = group.ID; clipped.MaskSourceID = target.ID;
        var sibling = Filled("Sibling", SKColors.Green, new(5, 5, 4, 4)); sibling.ParentID = group.ID;
        var outside = Filled("Outside", SKColors.White, new(0, 0, 10, 10));
        document.Layers.AddRange([outside, group, target, clipped, sibling]);

        using var isolated = LayerWorkflow.Isolated(document, target.ID);
        bool Shown(ImageLayer layer) => isolated.Layers.Single(l => l.ID == layer.ID).IsVisible;
        Assert.True(Shown(target));
        Assert.True(Shown(group));
        Assert.True(Shown(clipped));
        Assert.False(Shown(sibling));
        Assert.False(Shown(outside));
        // The document itself is left as it was.
        Assert.False(target.IsVisible);
        Assert.False(group.IsVisible);
        Assert.True(outside.IsVisible);
    }

    [Fact]
    public void ALayersPixelsAreTrimmedAndAnEmptyLayerHasNone()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 12, 12);
        var small = Filled("Small", SKColors.Red, new(3, 4, 2, 3));
        var clear = Filled("Clear", SKColors.Transparent, new(0, 0, 12, 12));
        var cover = Filled("Cover", SKColors.White, new(0, 0, 12, 12));
        document.Layers.AddRange([cover, small, clear]);

        using var pixels = LayerWorkflow.IsolatedPixels(document, small.ID);
        Assert.NotNull(pixels);
        Assert.Equal((2, 3), (pixels!.Width, pixels.Height));
        Assert.Equal(SKColors.Red, pixels.GetPixel(1, 2));
        Assert.Null(LayerWorkflow.IsolatedPixels(document, clear.ID));
    }

    [Fact]
    public void FileNamesAreWindowsSafeAndUniqueIgnoringCase()
    {
        Assert.Equal(["Sky.png", "sky 2.png", "a_b.png", "Layer.png", "con_.png", "Name.png", "Layer.hidden.png", "SKY 3.png"],
            LayerWorkflow.ExportFileNames(["Sky", "sky", "a/b", "  ", "con", "Name.", ".hidden", "SKY"], "png"));
    }
}
