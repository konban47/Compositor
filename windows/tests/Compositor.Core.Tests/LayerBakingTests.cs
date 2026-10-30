using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Flatten All Masks and Flatten All Layer Effects on groups and on an animated layer.</summary>
public sealed class LayerBakingTests
{
    private static ImageLayer Solid(string name, SKRectI rect, SKColor color)
    {
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(rect.Width, rect.Height)); bitmap.Erase(color);
        return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, name), new LayerTransform(rect.Left, rect.Top, rect.Width, rect.Height), name);
    }

    private static (CanvasDocument Document, ImageLayer Group) Grouped(Func<int, byte> maskAt)
    {
        var document = new CanvasDocument(Guid.NewGuid(), 40, 20);
        document.Layers.Add(Solid("Back", SKRectI.Create(40, 20), SKColors.White));
        var group = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 40, 20), "Group") { IsGroup = true };
        var a = Solid("A", SKRectI.Create(0, 0, 30, 20), new SKColor(200, 30, 30)); a.ParentID = group.ID;
        var b = Solid("B", SKRectI.Create(10, 5, 30, 10), new SKColor(30, 30, 200)); b.ParentID = group.ID; b.Opacity = .5;
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(40, 20));
        for (var y = 0; y < 20; y++) for (var x = 0; x < 40; x++) mask.SetPixel(x, y, new SKColor(maskAt(x), maskAt(x), maskAt(x)));
        group.Mask = new Model.LayerMask(ImportedImage.Create(mask, "Mask"));
        document.Layers.AddRange([a, b, group]);
        return (document, group);
    }

    private static void SameLook(SKBitmap before, SKBitmap after, int tolerance)
    {
        for (var y = 0; y < before.Height; y++) for (var x = 0; x < before.Width; x++)
        {
            var p = before.GetPixel(x, y); var q = after.GetPixel(x, y);
            Assert.True(Math.Abs(p.Red - q.Red) <= tolerance && Math.Abs(p.Green - q.Green) <= tolerance && Math.Abs(p.Blue - q.Blue) <= tolerance,
                $"({x},{y}): {p} → {q}");
        }
    }

    [Fact]
    public void AHardGroupMaskGoesIntoEachLayerAndKeepsThem()
    {
        var (document, group) = Grouped(x => x < 20 ? (byte)255 : (byte)0);
        using var _ = document;
        using var before = DocumentRenderer.Render(document);
        var notes = new List<string>();
        Assert.True(LayerBaking.FlattenGroupMask(document, group.ID, notes));
        Assert.Empty(notes);
        Assert.Null(group.Mask);
        Assert.Equal(4, document.Layers.Count);
        Assert.Equal(0, document.Layers.Single(l => l.Name == "A").Asset!.Image.GetPixel(25, 10).Alpha);
        using var after = DocumentRenderer.Render(document);
        SameLook(before, after, 1);
    }

    [Fact]
    public void ASoftGroupMaskMergesTheGroupAndKeepsItsLook()
    {
        var (document, group) = Grouped(x => (byte)(x * 255 / 39));
        using var _ = document;
        using var before = DocumentRenderer.Render(document);
        var notes = new List<string>();
        Assert.True(LayerBaking.FlattenGroupMask(document, group.ID, notes));
        Assert.Single(notes);
        Assert.Equal(["Back", "Group"], document.Layers.Select(l => l.Name));
        var merged = document.Layers[1];
        Assert.False(merged.IsGroup);
        Assert.Null(merged.Mask);
        using var after = DocumentRenderer.Render(document);
        SameLook(before, after, 2);
        // The group's own opacity stays on the layer it became.
        var (faded, fadedGroup) = Grouped(x => (byte)(x * 6));
        using var __ = faded;
        fadedGroup.Opacity = .8;
        Assert.True(LayerBaking.FlattenGroupMask(faded, fadedGroup.ID, notes));
        Assert.Equal(.8, faded.Layers.Single(l => l.Name == "Group").Opacity);
    }

    [Fact]
    public void GroupEffectsAreDrawnIntoTheMergedGroup()
    {
        var (document, group) = Grouped(_ => 255);
        using var _ = document;
        group.Mask = null;
        group.Effects = new LayerEffects { Items = [StyleEffect.Default(StyleEffectKind.Stroke)] };
        using var before = DocumentRenderer.Render(document);
        var notes = new List<string>();
        Assert.True(LayerBaking.FlattenGroupEffects(document, group.ID, notes));
        var merged = document.Layers.Single(l => l.Name == "Group");
        Assert.Null(merged.Effects);
        using var after = DocumentRenderer.Render(document);
        SameLook(before, after, 2);
        Assert.False(LayerBaking.FlattenGroupEffects(document, merged.ID, notes));
    }

    [Fact]
    public void AnAnimationsEffectsAreDrawnIntoEveryFrame()
    {
        var source = new GifSource(GifAnimationTests.Sample());
        var image = source.Decode(0, "Animation"); image.Animation = new GifAnimation(source);
        using var document = IO.ImageImporter.NewDocument(image);
        var layer = Assert.Single(document.Layers);
        layer.Effects = new LayerEffects { Items = [StyleEffect.Default(StyleEffectKind.ColorOverlay)] };
        var frames = layer.Gif!.Frames.Count;
        var notes = new List<string>();
        Assert.True(LayerBaking.FlattenGifEffects(document, layer.ID, notes));
        Assert.Null(layer.Effects);
        Assert.Equal(frames, layer.Gif!.Edited.Count);
        // The overlay's color is now in the pixels of every frame, which all keep the animation's size.
        var first = layer.Gif.Edited.Values.First().Image.GetPixel(0, 0);
        Assert.All(layer.Gif.Edited.Values, frame =>
        {
            Assert.Equal((layer.Asset!.Width, layer.Asset.Height), (frame.Width, frame.Height));
            Assert.Equal(first.Red, frame.Image.GetPixel(0, 0).Red);
        });
    }
}
