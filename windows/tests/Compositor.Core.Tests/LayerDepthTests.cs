using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>
/// A 16-bit layer stays 16-bit through 8-bit edits (upstream 5de396b): the history makes the result deep again,
/// keeping the old samples wherever the edit left the pixels alone. Only Convert Layer to 8-Bit makes it 8-bit.
/// </summary>
public sealed class LayerDepthTests
{
    /// <summary>A value between two 8-bit levels, so a layer that fell to 8 bits would lose it.</summary>
    private const float Between = 1000f / 65535;

    /// <summary>Opaque vertical stripes, each pixel's red nudged off the 8-bit grid.</summary>
    private static ImportedImage Striped(int width, int height)
    {
        var bitmap = Bitmaps.Allocate(DeepPixels.Info(width, height)); var p = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan());
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4; var tone = x / 4 % 2 == 0 ? .78f : .24f;
            p[i] = tone + Between; p[i + 1] = tone; p[i + 2] = tone; p[i + 3] = 1;
        }
        return ImportedImage.CreateDeep(bitmap, "Stripes");
    }

    private static float Red(ImportedImage image, int x, int y) =>
        MemoryMarshal.Cast<byte, float>(image.Deep!.GetPixelSpan())[y * image.Deep.RowBytes / 4 + x * 4];

    [Fact]
    public void ContentAwareFillGrowingALayerKeepsItsDepthAndUntouchedSamples()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 60, 40);
        var layer = new ImageLayer(Guid.NewGuid(), Striped(40, 40), new LayerTransform(0, 0, 40, 40), "Stripes");
        document.Layers.Add(layer);
        var before = layer.Asset!;
        var expected = Red(before, 5, 30);
        SelectionEdits.Select(document, SKRectI.Create(30, 5, 20, 10));
        var history = new DocumentHistory();
        history.Begin("Content-Aware Fill", document, layer.ID);
        Assert.True(ContentFillEdits.Apply(document, layer.ID));
        history.End(document, layer.ID);

        // The selection reached past the layer's right edge, so the layer grew; it is still 16-bit.
        Assert.True(layer.Asset!.Width > 40);
        Assert.Equal(16, layer.Asset.BitDepth);
        // Far from the fill, the old 16-bit sample is still there, not its 8-bit rounding.
        Assert.Equal(expected, Red(layer.Asset, 5, 30));
        Assert.NotEqual(MathF.Round(expected * 255) / 255, Red(layer.Asset, 5, 30));
    }

    [Fact]
    public void AShiftedResultKeepsOldSamplesWhereItsPixelsAreUnchanged()
    {
        using var before = Striped(4, 4);
        // The result is two columns and a row bigger, the old pixels one column and one row in, one of them repainted.
        using var grown = new SKBitmap(Bitmaps.ColorInfo(6, 5));
        grown.Erase(SKColors.Blue);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++) grown.SetPixel(x + 1, y + 1, before.Image.GetPixel(x, y));
        grown.SetPixel(3, 2, SKColors.Lime);
        using var after = ImportedImage.Create(grown.Copy(), "Stripes");

        using var kept = DeepPixels.Painted(before, after, (1, 1));
        Assert.Equal(16, kept.BitDepth);
        Assert.Equal(Red(before, 0, 0), Red(kept, 1, 1));
        Assert.Equal(Red(before, 3, 3), Red(kept, 4, 4));
        Assert.Equal(0, Red(kept, 3, 2));
        Assert.Equal(0, Red(kept, 0, 0));
        Assert.Equal(1, MemoryMarshal.Cast<byte, float>(kept.Deep!.GetPixelSpan())[2]);
    }

    [Fact]
    public void AResultThatNoLongerLinesUpIsTakenWholeAtTheLayersDepth()
    {
        using var before = Striped(4, 4);
        using var after = ImportedImage.Create(before.Image.Copy(), "Stripes");
        using var whole = DeepPixels.Painted(before, after, null);
        Assert.Equal(16, whole.BitDepth);
        Assert.Equal(before.Image.GetPixel(0, 0).Red / 255f, Red(whole, 0, 0), 4);
    }

    [Fact]
    public void PixelOffsetFollowsWholePixelShiftsOnly()
    {
        using var old = Striped(4, 4);
        using var grown = ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(6, 5)), "Grown");
        Assert.Equal((0, 0), ImageModeEdits.PixelOffset(new(2, 3, 4, 4), old, new(2, 3, 4, 4), old));
        Assert.Equal((1, 1), ImageModeEdits.PixelOffset(new(2, 3, 4, 4), old, new(1, 2, 6, 5), grown));
        Assert.Null(ImageModeEdits.PixelOffset(new(2, 3, 4, 4), old, new(1.5, 2, 6, 5), grown));
        Assert.Null(ImageModeEdits.PixelOffset(new(2, 3, 4, 4), old, new(1, 2, 12, 10), grown));
    }

    [Fact]
    public void ConvertingSelectedLayersReachesEveryOneAndThoseInSelectedGroups()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var group = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 8, 8), "Group") { IsGroup = true };
        var inside = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(8, 8)), "Inside"), new LayerTransform(0, 0, 8, 8), "Inside") { ParentID = group.ID };
        var flat = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(8, 8)), "Flat"), new LayerTransform(0, 0, 8, 8), "Flat");
        var deep = new ImageLayer(Guid.NewGuid(), Striped(8, 8), new LayerTransform(0, 0, 8, 8), "Deep");
        document.Layers.AddRange([group, inside, flat, deep]);

        // A mix: both directions would change something.
        Guid[] chosen = [group.ID, flat.ID, deep.ID];
        Assert.Equal([inside.ID, flat.ID, deep.ID], DeepPaint.DepthTargets(document, chosen).Select(l => l.ID));
        Assert.True(DeepPaint.CanConvertLayers(document, chosen, 16));
        Assert.True(DeepPaint.CanConvertLayers(document, chosen, 8));

        Assert.True(DeepPaint.ConvertLayers(document, chosen, 16));
        Assert.All(new[] { inside, flat, deep }, l => Assert.Equal(16, l.Asset!.BitDepth));
        Assert.False(DeepPaint.CanConvertLayers(document, chosen, 16));
        Assert.False(DeepPaint.ConvertLayers(document, chosen, 16));

        Assert.True(DeepPaint.ConvertLayers(document, chosen, 8));
        Assert.All(new[] { inside, flat, deep }, l => Assert.Equal(8, l.Asset!.BitDepth));
    }

    [Fact]
    public void ConvertLayerToEightBitStillMakesItEightBit()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var layer = new ImageLayer(Guid.NewGuid(), Striped(8, 8), new LayerTransform(0, 0, 8, 8), "Stripes");
        document.Layers.Add(layer);
        var history = new DocumentHistory();
        history.Begin("Convert Layer to 8-Bit", document, layer.ID);
        Assert.True(DeepPaint.ConvertLayer(document, layer.ID, 8));
        history.End(document, layer.ID);
        Assert.Equal(8, layer.Asset!.BitDepth);
        Assert.Null(layer.Asset.Deep);
    }
}
