using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.IO.PSD;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;
namespace Compositor.Core.Tests;
public class Upstream155Tests
{
    [Fact] public void ChannelPackPngPreservesColorUnderZeroAlphaAndHiddenNamedSources()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 8, 8);
        foreach (var (name, value) in new[] { ("Red", (byte)40), ("Green", (byte)90), ("Blue", (byte)150), ("Alpha", (byte)0) })
        { var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(8, 8)); pixels.Erase(new SKColor(value, value, value)); doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, name), new LayerTransform(0, 0, 8, 8), name) { IsVisible = false }); }
        using var packed = ChannelPacking.Pack(doc, Enumerable.Range(0, 4).Select(c => ChannelPacking.Default(doc, c)).ToArray(), 8, 8);
        var bytes = PngCodec.Encode(packed); using var decoded = PngCodec.DecodeImage(PngCodec.ReadHeader(bytes), bytes);
        Assert.Equal(new byte[] { 40, 90, 150, 0 }, decoded.GetPixelSpan()[..4].ToArray());
        var id = LayerPlacement.AddImage(doc, decoded.Copy(), SKRectI.Create(8, 8), "Packed", null)!.Value;
        Assert.NotNull(ChannelPacking.Split(doc, id));
        Assert.Equal(new[] { "Alpha", "Blue", "Green", "Red" }, doc.Layers.Where(l => l.ParentID == id).Select(l => l.Name));
    }
    [Theory] [InlineData(AdjustmentKind.ProgressiveBlur)] [InlineData(AdjustmentKind.BloomGlow)] [InlineData(AdjustmentKind.RgbSplit)]
    public void FilterLayerRegionsMatchFullImageAndUpstreamV16FieldsLoad(AdjustmentKind kind)
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 140, 110); var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(140, 110));
        for (var y = 0; y < 110; y++) for (var x = 0; x < 140; x++) pixels.SetPixel(x, y, x % 20 < 10 ? SKColors.White : new SKColor(10, 20, 30));
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Stripes"), new LayerTransform(0, 0, 140, 110), "Stripes"));
        var f = new FilterLayerSettings { ProgressiveFrom = 0, ProgressiveTo = 3, ProgressiveStart = new(20, 20), ProgressiveEnd = new(100, 80), BloomRadius = 3, RgbSplitDistance = 8, RgbSplitBlur = 3, RgbSplitRadial = true, RgbSplitReach = 40, RgbSplitCenter = new(20, 20) };
        var layer = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 140, 110), "Filter") { Adjustment = new LayerAdjustment { Kind = kind, FilterLayerSettings = f } }; doc.Layers.Add(layer);
        using var full = DocumentRenderer.Render(doc); var area = SKRectI.Create(45, 32, 43, 41); using var part = DocumentRenderer.RenderRegion(doc, area);
        var maximum = 0;
        for (var y = 0; y < area.Height; y++) for (var x = 0; x < area.Width; x++) for (var c = 0; c < 4; c++) maximum = Math.Max(maximum, Math.Abs(full.GetPixelSpan()[(y + area.Top) * full.RowBytes + (x + area.Left) * 4 + c] - part.GetPixelSpan()[y * part.RowBytes + x * 4 + c]));
        Assert.InRange(maximum, 0, 1);
        var manifest = ProjectSnapshot.FromDocument(doc).Manifest; manifest.Version = 16; ProjectStore.Validate(manifest);
        var encoded = ManifestJson.Serialize(manifest); var loaded = ManifestJson.Deserialize(encoded); Assert.Equal(f, loaded.Layers[^1].Adjustment!.FilterLayerSettings);
        manifest.Version = 15; Assert.Throws<ProjectException>(() => ProjectStore.Validate(manifest));
    }
    [Fact] public void SpectralDispersionIsContinuousAndIdentityIsExact()
    {
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(90, 24)); source.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(source)) { using var paint = new SKPaint { Color = SKColors.White }; canvas.DrawRect(40, 0, 10, 24, paint); }
        using var split = SpectralDispersion.Apply(source, new FilterSettings { RgbSplitDistance = 20 });
        Assert.True(Enumerable.Range(20, 50).Select(x => split.GetPixel(x, 10)).Distinct().Count() > 12);
        Assert.Contains(Enumerable.Range(20, 50), x => { var c = split.GetPixel(x, 10); return c.Red != c.Blue; });
        using var identity = SpectralDispersion.Apply(source, new FilterSettings { RgbSplitDistance = 0 }); Assert.True(source.GetPixelSpan().SequenceEqual(identity.GetPixelSpan()));
    }
    [Fact] public void PsdArtboardsKeepNestedChildrenAndShiftTextMasksAndOffCanvasPixels()
    {
        var source = new PsdDocument { Width = 100, Height = 100 };
        var a = new PsdRecord { IsGroup = true, Name = "A", Artboard = new PsdRect(-50, 20, 50, 100) };
        var b = new PsdRecord { IsGroup = true, Name = "B", Artboard = new PsdRect(200, 20, 260, 80) };
        var folder = new PsdRecord { IsGroup = true, ParentId = a.Id }; var child = new PsdRecord { ParentId = folder.Id, Bounds = new(-40, 30, 10, 60), MaskBounds = new(-35, 32, 5, 55), Text = new(new LayerTextStyle(), -40, 30, 0, false, false, []) };
        source.Layers.AddRange([child, folder, a, new PsdRecord { ParentId = b.Id, Bounds = new(210, 25, 250, 60) }, b, new PsdRecord()]);
        var result = PsdArtboards.Split(source); Assert.Equal(new[] { "A", "B" }, result.Select(i => i.Name));
        Assert.Equal(2, result[0].Document.Layers.Count); Assert.Single(result[1].Document.Layers);
        Assert.Null(result[0].Document.Layers.Single(l => l.Id == folder.Id).ParentId);
        var moved = result[0].Document.Layers.Single(l => l.Id == child.Id); Assert.Equal(new PsdRect(10, 10, 60, 40), moved.Bounds); Assert.Equal(10, moved.Text!.X); Assert.Equal(12, moved.MaskBounds.Top);
    }
}
