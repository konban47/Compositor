using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class SelectionRetouchTests
{
    private static ImageLayer Pattern(double angle = 0)
    {
        var bitmap = new SKBitmap(Bitmaps.ColorInfo(48, 48));
        for (var y = 0; y < 48; y++) for (var x = 0; x < 48; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x * 5), (byte)(y * 5), (byte)((x + y) % 3 * 100)));
        return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Pattern"), new LayerTransform(8, 8, 48, 48, angle), "Pattern");
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(3, 0)] [InlineData(4, 0)]
    [InlineData(0, 30)] [InlineData(1, 30)] [InlineData(2, 30)] [InlineData(3, 30)] [InlineData(4, 30)]
    public void RetouchChangesOnlySelectedPixelsOnTheActiveLayer(int tool, double angle)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var layer = Pattern(angle); var other = Pattern();
        document.Layers.Add(other); document.Layers.Add(layer);
        using var before = layer.Asset!.Image.Copy(); using var untouched = other.Asset!.Image.Copy();
        SelectionEdits.Select(document, SKRectI.Create(24, 24, 16, 16), antialiased: false);
        SKPoint[] stroke = [new(16, 30), new(46, 34)];
        var brush = new BrushSettings(Diameter: 22, Hardness: 1, Opacity: 1, CloneFrom: new SKPointI(8, 3));
        var changed = tool < 2 ? WarpEdits.Warp(document, layer.ID, stroke, (WarpMode)tool, brush)
            : BrushEdits.Paint(document, layer.ID, stroke, brush with
            { Mode = tool == 2 ? BrushMode.Clone : tool == 3 ? BrushMode.Heal : BrushMode.Blur });
        Assert.True(changed);
        var differences = 0;
        for (var y = 0; y < 48; y++) for (var x = 0; x < 48; x++)
        {
            var point = layer.Transform.Point((x + .5) / 48, (y + .5) / 48);
            var current = layer.Asset!.Image.GetPixel(x, y);
            if (!document.Selection.Contains((int)Math.Floor(point.X), (int)Math.Floor(point.Y)))
                Assert.Equal(before.GetPixel(x, y), current);
            if (before.GetPixel(x, y) != current) differences++;
            Assert.Equal(untouched.GetPixel(x, y), other.Asset!.Image.GetPixel(x, y));
        }
        Assert.True(differences > 0);
    }

    [Theory] [InlineData(WarpMode.Liquify)] [InlineData(WarpMode.Smudge)]
    public void EmptySelectionPreventsWarp(WarpMode mode)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var layer = Pattern(); document.Layers.Add(layer);
        var asset = layer.Asset;
        document.Selection = DocumentSelection.FromPath(new SKPath());
        Assert.False(WarpEdits.Warp(document, layer.ID, [new(15, 32), new(40, 32)], mode, new BrushSettings(Diameter: 20)));
        Assert.Same(asset, layer.Asset);
    }

    [Theory] [InlineData(WarpMode.Liquify)] [InlineData(WarpMode.Smudge)]
    public void FeatheredSelectionBlendsWarpAndPreservesZeroCoverage(WarpMode mode)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var layer = Pattern(); document.Layers.Add(layer);
        using var before = layer.Asset!.Image.Copy();
        SelectionEdits.Select(document, SKRectI.Create(25, 22, 12, 20));
        document.Selection = document.Selection.WithFeather(4);
        using var coverage = document.Selection.Coverage(SKRectI.Create(0, 0, 64, 64))!;
        Assert.True(WarpEdits.Warp(document, layer.ID, [new(15, 32), new(44, 32)], mode, new BrushSettings(Diameter: 26)));
        var featherChanged = false;
        for (var y = 0; y < 48; y++) for (var x = 0; x < 48; x++)
        {
            var value = coverage.GetPixel(x + 8, y + 8).Red;
            var changed = before.GetPixel(x, y) != layer.Asset!.Image.GetPixel(x, y);
            if (value == 0) Assert.False(changed);
            if (value is > 0 and < 255 && changed) featherChanged = true;
        }
        Assert.True(featherChanged);
    }

    [Theory] [InlineData(-100, 0)] [InlineData(100, 0)] [InlineData(0, -100)] [InlineData(0, 100)]
    public void CameraRawPreservesToneOrderAndEndpoints(double shadows, double highlights)
    {
        var bytes = new byte[256 * 4];
        for (var i = 0; i < 256; i++) { bytes[i * 4] = bytes[i * 4 + 1] = bytes[i * 4 + 2] = (byte)i; bytes[i * 4 + 3] = 255; }
        AdjustPixels.CameraRaw(bytes, 256, 1, 1024, 1, 1, 1, 0, 0, highlights, shadows, 0, 0, 0, 0, 0);
        Assert.Equal(0, bytes[0]); Assert.Equal(255, bytes[255 * 4]);
        for (var i = 1; i < 256; i++) Assert.True(bytes[i * 4] >= bytes[(i - 1) * 4], $"Tone reversed at {i}");
    }

    [Fact]
    public void DragReordersWithinAGroupAndRejectsItsOwnDescendants()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var folder = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 64, 64), "Folder") { IsGroup = true };
        var a = Pattern(); var b = Pattern(); var c = Pattern();
        a.ParentID = b.ParentID = folder.ID;
        document.Layers.AddRange([folder, a, b, c]);
        Assert.True(LayerEdits.MoveRelative(document, a.ID, b.ID, above: true));
        Assert.True(document.Layers.IndexOf(a) > document.Layers.IndexOf(b));
        Assert.False(LayerEdits.MoveRelative(document, folder.ID, a.ID, above: true));
        Assert.Null(folder.ParentID);
        Assert.True(LayerEdits.MoveRelative(document, a.ID, c.ID, above: false));
        Assert.Null(a.ParentID);
        Assert.Equal(folder.ID, b.ParentID);
    }
}
