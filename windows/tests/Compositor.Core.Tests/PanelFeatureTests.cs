using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public class PanelFeatureTests
{
    private static CanvasDocument Document()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 16, 12);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(16, 12)); pixels.Erase(new SKColor(50, 100, 150));
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "颜色"), new LayerTransform(0, 0, 16, 12), "颜色"));
        return document;
    }

    [Theory]
    [InlineData(LayerLocks.All)] [InlineData(LayerLocks.Pixels)]
    public void LockedPixelsSurviveFillAndDoNotAddUndo(LayerLocks locks)
    {
        using var document = Document(); var layer = document.Layers[0]; layer.Locks = locks;
        var history = new DocumentHistory(); var pixels = layer.Asset;
        history.Begin("Fill", document, layer.ID); FillEdits.Fill(document, layer.ID, SKColors.Red); history.End(document, layer.ID);
        Assert.Same(pixels, document.Layers[0].Asset); Assert.False(history.CanUndo);
    }

    [Fact]
    public void LockedGroupProtectsChildrenAndDeletionIsAtomic()
    {
        using var document = Document(); var layer = document.Layers[0];
        var group = LayerPlacement.GroupSelected(document, [layer.ID])!.Value;
        LayerProtection.Set(document, [group], LayerLocks.All);
        Assert.False(LayerProtection.CanPaint(document, layer.ID)); Assert.False(LayerProtection.CanMove(document, layer.ID));
        Assert.Empty(TransformEdits.GroupMembers(document, [group]));
        var history = new DocumentHistory(); history.Begin("Delete Layers", document, group);
        document.Layers.Clear(); history.End(document, group);
        Assert.Equal(2, document.Layers.Count); Assert.False(history.CanUndo);
    }

    [Fact]
    public void LockCanBeUndoneAndPositionLockStillAllowsPainting()
    {
        using var document = Document(); var id = document.Layers[0].ID; var history = new DocumentHistory();
        history.Begin("Layer Locks", document, id); LayerProtection.Set(document, [id], LayerLocks.Position); history.End(document, id);
        Assert.False(LayerEdits.Move(document, id, 5, 6)); Assert.True(LayerProtection.CanPaint(document, id));
        document.Adopt(history.Undo()!.Value.Document!); Assert.Equal(LayerLocks.None, document.Layers[0].Locks);
        document.Adopt(history.Redo()!.Value.Document!); Assert.Equal(LayerLocks.Position, document.Layers[0].Locks);
    }

    [Fact]
    public void TransparencyLockKeepsAlphaWhilePaintingColor()
    {
        using var document = Document(); var layer = document.Layers[0]; layer.Asset!.Image.SetPixel(5, 5, new SKColor(30, 40, 50, 80));
        layer.Locks = LayerLocks.Transparency; var history = new DocumentHistory();
        history.Begin("Fill", document, layer.ID); FillEdits.Fill(document, layer.ID, SKColors.Red); history.End(document, layer.ID);
        var pixel = document.Layers[0].Asset!.Image.GetPixel(5, 5);
        Assert.Equal(80, pixel.Alpha); Assert.True(pixel.Red > 240);
    }

    [Theory]
    [InlineData(ColorChannels.Red)] [InlineData(ColorChannels.Green)] [InlineData(ColorChannels.Blue)]
    public void SingleChannelEditsPreserveOtherComponents(ColorChannels channel)
    {
        using var document = Document(); var layer = document.Layers[0]; document.EditChannels = channel;
        var history = new DocumentHistory(); history.Begin("Fill", document, layer.ID);
        FillEdits.Fill(document, layer.ID, SKColors.White); history.End(document, layer.ID);
        var color = document.Layers[0].Asset!.Image.GetPixel(4, 4);
        Assert.Equal(channel == ColorChannels.Red ? 255 : 50, color.Red);
        Assert.Equal(channel == ColorChannels.Green ? 255 : 100, color.Green);
        Assert.Equal(channel == ColorChannels.Blue ? 255 : 150, color.Blue); Assert.Equal(255, color.Alpha);
    }

    [Fact]
    public void ChannelSelectionPreservesEveryGrayLevel()
    {
        using var document = Document();
        var gray = new SKBitmap(Bitmaps.MaskInfo(16, 12));
        for (var y = 0; y < 12; y++) for (var x = 0; x < 16; x++) gray.SetPixel(x, y, new SKColor((byte)(x * 17), (byte)(x * 17), (byte)(x * 17)));
        ChannelEdits.Load(document, gray);
        using var coverage = document.Selection.Coverage(SKRectI.Create(0, 0, 16, 12))!;
        for (var x = 0; x < 16; x++) Assert.Equal(x * 17, coverage.GetPixel(x, 5).Red);
        var id = ChannelEdits.Add(document, "保存选区", true)!.Value;
        Assert.Equal(85, document.Channels.Single(channel => channel.ID == id).Asset.Image.GetPixel(5, 5).Red);
    }

    [Fact]
    public void AlphaPaintingChangesChannelOnlyAndUndoes()
    {
        using var document = Document(); var original = document.Layers[0].Asset;
        var id = ChannelEdits.Add(document, "Alpha 1", false)!.Value; var history = new DocumentHistory();
        document.Selection = DocumentSelection.Rectangular(SKRectI.Create(2, 2, 4, 4), 16, 12);
        history.Begin("Paint Channel", document, null);
        Assert.True(ChannelEdits.Edit(document, id, (proxy, layer) => FillEdits.Fill(proxy, layer, SKColors.White)));
        history.End(document, null);
        Assert.Same(original, document.Layers[0].Asset);
        Assert.Equal(255, document.Channels[0].Asset.Image.GetPixel(3, 3).Red);
        Assert.Equal(0, document.Channels[0].Asset.Image.GetPixel(0, 0).Red);
        document.Adopt(history.Undo()!.Value.Document!); Assert.Equal(0, document.Channels[0].Asset.Image.GetPixel(3, 3).Red);
    }

    [Fact]
    public void ChannelsLocksFillAndLinksRoundTripAndOldProjectsStayVersion11()
    {
        using var document = Document(); Assert.Equal(11, ProjectSnapshot.FromDocument(document).Manifest.Version);
        var layer = document.Layers[0]; layer.Locks = LayerLocks.All; layer.FillOpacity = .4; layer.LinkID = Guid.NewGuid();
        ChannelEdits.Add(document, "羽化选区", true);
        var folder = Path.Combine(Path.GetTempPath(), "compositor-panel-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            var snapshot = ProjectSnapshot.FromDocument(document); Assert.Equal(12, snapshot.Manifest.Version); ProjectStore.Save(snapshot, folder);
            using var loaded = ProjectStore.Load(folder); var copy = loaded.ToDocument();
            Assert.Equal(layer.Locks, copy.Layers[0].Locks); Assert.Equal(.4, copy.Layers[0].FillOpacity); Assert.Equal(layer.LinkID, copy.Layers[0].LinkID);
            Assert.Equal("羽化选区", copy.Channels[0].Name); Assert.Equal(255, copy.Channels[0].Asset.Image.GetPixel(3, 3).Red);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void FillHidesPixelsButPreservesLayerEffects()
    {
        using var document = Document(); var layer = document.Layers[0]; layer.FillOpacity = 0;
        using var empty = DocumentRenderer.Render(document); Assert.Equal(0, empty.GetPixel(5, 5).Alpha);
        layer.Effects = new LayerEffects { ColorOverlay = new ColorOverlayEffect { Red = 1, Green = 0, Blue = 0, Opacity = 1 } };
        using var effects = DocumentRenderer.Render(document); Assert.Equal(SKColors.Red, effects.GetPixel(5, 5));
    }

    [Fact]
    public void CropResizeAndRotateCarryChannelPixels()
    {
        using var document = Document(); var id = ChannelEdits.Add(document, "Alpha 1", false)!.Value;
        ChannelEdits.Edit(document, id, (proxy, layer) => FillEdits.Fill(proxy, layer, SKColors.White));
        Assert.True(CanvasEdits.Crop(document, SKRectI.Create(2, 2, 8, 6)));
        Assert.Equal((8, 6), (document.Channels[0].Asset.Width, document.Channels[0].Asset.Height));
        Assert.True(CanvasRotation.Rotate(document, true));
        Assert.Equal((6, 8), (document.Channels[0].Asset.Width, document.Channels[0].Asset.Height));
        Assert.Equal(255, document.Channels[0].Asset.Image.GetPixel(3, 4).Red);
    }
}
