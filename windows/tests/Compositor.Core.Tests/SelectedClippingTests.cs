using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Create / Release Clipping Mask for every selected layer, as one step (upstream d693a2a).</summary>
public sealed class SelectedClippingTests
{
    private static ImageLayer Layer(string name) =>
        new(Guid.NewGuid(), ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(4, 4)), name), new LayerTransform(0, 0, 4, 4), name);

    /// <summary>Base at the bottom, then three layers stacked on it.</summary>
    private static (CanvasDocument Document, ImageLayer Base, ImageLayer A, ImageLayer B, ImageLayer C) Stack()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 4, 4);
        var (bottom, a, b, c) = (Layer("Base"), Layer("A"), Layer("B"), Layer("C"));
        document.Layers.AddRange([bottom, a, b, c]);
        return (document, bottom, a, b, c);
    }

    [Fact]
    public void ARunOfSelectedLayersAllClipToTheLayerUnderTheLowest()
    {
        var (document, bottom, a, b, c) = Stack();
        using var _ = document;
        Guid[] chosen = [c.ID, a.ID, b.ID];
        Assert.False(LayerMaskEdits.ReleasesSelected(document, chosen));
        Assert.True(LayerMaskEdits.CanToggleSelected(document, chosen));
        // Asking does not change anything.
        Assert.All(new[] { a, b, c }, l => Assert.Null(l.MaskSourceID));

        Assert.True(LayerMaskEdits.ToggleSelected(document, chosen));
        Assert.All(new[] { a, b, c }, l => Assert.Equal(bottom.ID, l.MaskSourceID));
        Assert.True(LayerMaskEdits.ReleasesSelected(document, chosen));

        Assert.True(LayerMaskEdits.ToggleSelected(document, chosen));
        Assert.All(new[] { a, b, c }, l => Assert.Null(l.MaskSourceID));
    }

    [Fact]
    public void WhenOneCannotBeClippedNoneAre()
    {
        var (document, bottom, a, _, c) = Stack();
        using var _doc = document;
        // The bottom layer has nothing below it to clip to.
        Guid[] chosen = [bottom.ID, c.ID];
        Assert.False(LayerMaskEdits.CanToggleSelected(document, chosen));
        Assert.False(LayerMaskEdits.ToggleSelected(document, chosen));
        Assert.Null(c.MaskSourceID);
        Assert.Null(a.MaskSourceID);
    }

    [Fact]
    public void AMixClipsTheOnesNotYetClipped()
    {
        var (document, bottom, a, b, _) = Stack();
        using var _doc = document;
        a.MaskSourceID = bottom.ID;
        Guid[] chosen = [a.ID, b.ID];
        Assert.False(LayerMaskEdits.ReleasesSelected(document, chosen));
        Assert.True(LayerMaskEdits.ToggleSelected(document, chosen));
        Assert.Equal(bottom.ID, b.MaskSourceID);
        Assert.Equal(bottom.ID, a.MaskSourceID);
    }
}
