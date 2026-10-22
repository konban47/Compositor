using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;
namespace Compositor.Core.Tests;
public sealed class AdvancedRepairTests
{
    [Fact] public void RetouchCopiesPreserveHiddenChannelsAndFailBeforeChangingPixels()
    {
        using var doc = Scene(); var original = doc.Layers[0].Asset!;
        original.Image.SetPixel(0, 0, new SKColor(13, 27, 51, 0));
        using (var copy = Bitmaps.Copy(original.Image)) Assert.True(original.Image.GetPixelSpan().SequenceEqual(copy.GetPixelSpan()));
        using var builder = new SKPathBuilder(); builder.AddCircle(30, 30, 8); using var area = builder.Detach();
        var budget = new MemoryBudget(1024, () => 0); using var scope = budget.Enter();
        Assert.Throws<OutOfMemoryException>(() => RemoveEdits.Apply(doc, doc.Layers[0].ID, area, true, false));
        Assert.Same(original, doc.Layers[0].Asset); Assert.Equal(0, budget.ReservedBytes);
        Assert.Throws<OutOfMemoryException>(() => Bitmaps.Copy(original.Image));
    }
    [Theory] [InlineData(1)] [InlineData(7)] [InlineData(15)]
    public void SlidingNeighborhoodMeansMatchWeightedReferenceAtEdges(int radius)
    {
        using var image = Bitmaps.Allocate(Bitmaps.ColorInfo(23, 17)); var random = new Random(23);
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++) image.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        var bounds = new SKRectI(0, 3, 19, 17); var actual = new NeighborhoodMean(image, bounds, radius);
        for (var y = bounds.Top; y < bounds.Bottom; y++) for (var x = bounds.Left; x < bounds.Right; x++)
        {
            long r = 0, g = 0, b = 0, a = 0;
            for (var dy = -radius; dy <= radius; dy++) for (var dx = -radius; dx <= radius; dx++)
            { var c = image.GetPixel(Math.Clamp(x + dx, 0, image.Width - 1), Math.Clamp(y + dy, 0, image.Height - 1)); r += c.Red * c.Alpha; g += c.Green * c.Alpha; b += c.Blue * c.Alpha; a += c.Alpha; }
            Assert.Equal(new SKColor((byte)Math.Round((double)r / a), (byte)Math.Round((double)g / a), (byte)Math.Round((double)b / a)), actual.At(x, y));
        }
    }
    [Fact] public void SamplingUnavailableLayerNeverFallsBackToAllLayers()
    {
        using var doc = Scene();
        Assert.Equal(0, ColorSampling.Read(doc, Guid.NewGuid(), new(10, 10), scope: SampleScope.CurrentLayer).Alpha);
    }
    private static CanvasDocument Scene(int width = 80, int height = 60)
    {
        var doc = new CanvasDocument(Guid.NewGuid(), width, height);
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); pixels.Erase(new SKColor(70, 110, 150));
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Base"), new LayerTransform(0, 0, width, height), "Base")); return doc;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CloneMappingHonorsScaleAndFlipAndLeavesOutsideStrokeExact(bool flip)
    {
        using var doc = Scene(); var layer = doc.Layers[0];
        for (var y = 0; y < 30; y++) for (var x = 0; x < 35; x++) layer.Asset!.Image.SetPixel(x, y, new SKColor((byte)(x * 6), (byte)(y * 8), 9));
        var map = CloneSampling.Mapping(new(20, 20), new(55, 40), 200, 100, 0, flip, false);
        using var original = layer.Asset!.Image.Copy();
        Assert.True(BrushEdits.Paint(doc, layer.ID, [new(55, 40)], new BrushSettings(Mode: BrushMode.Clone, Diameter: 10, CloneMapping: map)));
        var from = map.MapPoint(57.5f, 40.5f);
        Assert.Equal(original.GetPixel((int)from.X, (int)from.Y), layer.Asset!.Image.GetPixel(57, 40));
        Assert.Equal(original.GetPixel(54, 34), layer.Asset.Image.GetPixel(54, 34));
    }
    [Fact] public void HealingScopesAndPatternReplaceHaveRealPixelEffects()
    {
        using var doc = Scene(); var target = doc.Layers[0]; var top = Bitmaps.Allocate(Bitmaps.ColorInfo(80, 60)); top.Erase(SKColors.Red);
        LayerPlacement.AddImage(doc, top, SKRectI.Create(80, 60), "Top", target.ID);
        Assert.True(BrushEdits.Paint(doc, target.ID, [new(30, 30)], new BrushSettings(Mode: BrushMode.HealingSample, Diameter: 8, CloneFrom: new(10, 0), CloneScope: 2, HealingBlend: HealingBlend.Replace)));
        Assert.Equal(SKColors.Red, target.Asset!.Image.GetPixel(30, 30));
        using var tile = Bitmaps.Allocate(Bitmaps.ColorInfo(2, 2)); tile.Erase(SKColors.Blue);
        Assert.True(BrushEdits.Paint(doc, target.ID, [new(50, 40)], new BrushSettings(Mode: BrushMode.HealingSample, Diameter: 8, HealingPattern: true, Pattern: tile, HealingBlend: HealingBlend.Replace)));
        Assert.Equal(SKColors.Blue, target.Asset!.Image.GetPixel(50, 40));
        Assert.Equal(new SKColor(70, 110, 150), target.Asset.Image.GetPixel(10, 10));
    }
    [Fact] public void BelowSamplingKeepsGroupAncestorsAndRejectsHigherSiblings()
    {
        using var doc = Scene(); var id = doc.Layers[0].ID;
        LayerWorkflow.MakeContainer(doc, [id], LayerContainer.Group, "Folder", SKRect.Create(80, 60));
        var top = Bitmaps.Allocate(Bitmaps.ColorInfo(80, 60)); top.Erase(SKColors.Red); LayerPlacement.AddImage(doc, top, SKRectI.Create(80, 60), "Above", doc.Layers[^1].ID);
        Assert.Equal(new SKColor(70, 110, 150), ColorSampling.Read(doc, id, new(10, 10), scope: SampleScope.CurrentAndBelow));
    }
    [Fact] public void PatchTransparencyAndSoftSelectionArePreserved()
    {
        using var doc = Scene(); var layer = doc.Layers[0]; layer.Asset!.Image.Erase(new SKColor(20, 70, 120, 90));
        for (var y = 0; y < 60; y++) for (var x = 40; x < 80; x++) layer.Asset.Image.SetPixel(x, y, SKColors.Lime);
        doc.Selection = DocumentSelection.Rectangular(new SKRectI(10, 10, 20, 20), 80, 60).WithFeather(2);
        Assert.True(RepairEdits.Patch(doc, layer.ID, new(40, 0), false, transparent: true));
        Assert.Equal(90, layer.Asset!.Image.GetPixel(15, 15).Alpha); Assert.True(layer.Asset.Image.GetPixel(15, 15).Green > 230);
        Assert.Equal(new SKColor(20, 70, 120, 90), layer.Asset.Image.GetPixel(2, 2));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ContentMoveTransformsAndFillsOnlyTheSource(bool extend)
    {
        using var doc = Scene(); var layer = doc.Layers[0];
        for (var y = 10; y < 20; y++) for (var x = 10; x < 20; x++) layer.Asset!.Image.SetPixel(x, y, SKColors.Lime);
        doc.Selection = DocumentSelection.Rectangular(new SKRectI(10, 10, 20, 20), 80, 60);
        var captured = ContentMoveEdits.Capture(doc, layer.ID, false)!.Value; using var pixels = captured.Pixels;
        Assert.True(ContentMoveEdits.Apply(doc, layer.ID, pixels, captured.Region, new LayerTransform(40, 25, 20, 20, 90), extend, false, 7, 0));
        Assert.Equal(SKColors.Lime, layer.Asset!.Image.GetPixel(50, 35));
        if (extend) Assert.Equal(SKColors.Lime, layer.Asset.Image.GetPixel(15, 15)); else Assert.NotEqual(SKColors.Lime, layer.Asset.Image.GetPixel(15, 15));
        Assert.Equal(new SKColor(70, 110, 150), layer.Asset.Image.GetPixel(2, 2)); Assert.True(doc.Selection.Contains(50, 35)); Assert.False(doc.Selection.Contains(15, 15));
    }
    [Fact] public void ContentMoveAllLayersAndColorAdaptationAndLocks()
    {
        using var doc = Scene(); var layer = doc.Layers[0];
        var top = Bitmaps.Allocate(Bitmaps.ColorInfo(10, 10)); top.Erase(SKColors.Red); LayerPlacement.AddImage(doc, top, new(10, 10, 20, 20), "Top", layer.ID);
        doc.Selection = DocumentSelection.Rectangular(new SKRectI(10, 10, 20, 20), 80, 60);
        var captured = ContentMoveEdits.Capture(doc, layer.ID, true)!.Value; using var pixels = captured.Pixels; Assert.Equal(SKColors.Red, pixels.GetPixel(5, 5));
        var destination = new LayerTransform(40, 30, 10, 10); layer.Locks = LayerLocks.Pixels;
        Assert.False(ContentMoveEdits.Apply(doc, layer.ID, pixels, captured.Region, destination, true, false, 4, 10));
        layer.Locks = LayerLocks.None; Assert.True(ContentMoveEdits.Apply(doc, layer.ID, pixels, captured.Region, destination, true, false, 7, 10));
        Assert.Equal(new SKColor(70, 110, 150), layer.Asset!.Image.GetPixel(45, 35));
    }
    [Fact] public void PdfPagesRenderWithNativePdfiumAndRespectRangesAndBudget()
    {
        var path = Path.Combine(Path.GetTempPath(), "compositor-pdf-" + Guid.NewGuid() + ".pdf");
        try
        {
            using (var stream = File.Create(path)) using (var pdf = SKDocument.CreatePdf(stream))
            { var first = pdf.BeginPage(72, 36); first.Clear(SKColors.Red); pdf.EndPage(); var second = pdf.BeginPage(36, 72); second.Clear(SKColors.Blue); pdf.EndPage(); pdf.Close(); }
            Assert.Equal(2, PdfImporter.Pages(path).Count); Assert.Equal([0, 1], PdfImporter.ParsePages("2,1-2", 2)); Assert.Throws<FormatException>(() => PdfImporter.ParsePages("3", 2));
            using var page = PdfImporter.Page(path, 1, 144); Assert.Equal(72, page.Width); Assert.Equal(144, page.Height); Assert.Equal(SKColors.Blue, page.Image.GetPixel(20, 20));
            using var budget = new MemoryBudget(1024, () => 0).Enter(); Assert.Throws<OutOfMemoryException>(() => PdfImporter.Page(path, 0));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void SpotHealingAllLayersAppliesOpacityOnlyOnceOnTransparentTarget()
    {
        using var doc = Scene(); var bottom = doc.Layers[0]; var clear = Bitmaps.Allocate(Bitmaps.ColorInfo(80, 60)); clear.Erase(SKColors.Transparent);
        var id = LayerPlacement.AddImage(doc, clear, SKRectI.Create(80, 60), "Repair", bottom.ID)!.Value;
        Assert.True(BrushEdits.Paint(doc, id, [new(40, 30)], new BrushSettings(Mode: BrushMode.Heal, CloneAllLayers: true, Diameter: 16, Hardness: 1, Opacity: .5, Seed: 23)));
        var result = doc.Layers.Single(l => l.ID == id).Asset!.Image;
        Assert.InRange(result.GetPixel(40, 30).Alpha, 127, 128); Assert.InRange(result.GetPixel(40, 30).Red, 69, 71); Assert.Equal(0, result.GetPixel(20, 30).Alpha);
    }
    [Fact] public void RgbSplitSpreadsIntoTransparentRoomAndPreservesMaskPosition()
    {
        using var doc = Scene(); var layer = doc.Layers[0]; layer.Transform = new LayerTransform(25, 15, 16, 16);
        var white = Bitmaps.Allocate(Bitmaps.ColorInfo(16, 16)); white.Erase(SKColors.White); layer.Asset = ImportedImage.Create(white, "Square");
        Assert.True(FilterEdits.Apply(doc, layer.ID, FilterKind.RgbSplit, new FilterSettings { RgbSplitDistance = 6 }));
        Assert.True(layer.Transform.Width > 16 && layer.Transform.X < 25); Assert.Contains(layer.Asset!.Image.Pixels, c => c.Red != c.Blue);
    }
    [Fact] public void CachedBudgetCountsOutstandingAllocationsAcrossReservations()
    {
        long committed = 0; var budget = new MemoryBudget(1000, () => committed, cacheMeasurements: true);
        using (budget.Reserve(600)) committed = 600;
        Assert.Throws<OutOfMemoryException>(() => budget.Reserve(500));
        using (budget.Reserve(300)) { committed = 900; Assert.Throws<OutOfMemoryException>(() => budget.Reserve(200)); }
        Assert.Equal(0, budget.ReservedBytes);
    }
}
