using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;
namespace Compositor.Core.Tests;
public sealed class UtilityRetouchTests
{
    private static CanvasDocument Scene(int width = 64, int height = 64)
    {
        var doc = new CanvasDocument(Guid.NewGuid(), width, height);
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); pixels.Erase(new SKColor(70, 120, 180));
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Base"), new LayerTransform(0, 0, width, height), "Base")); return doc;
    }
    [Fact] public void SampleScopesAverageAndAdjustmentsUseTheComposite()
    {
        using var doc = Scene(9, 9); var baseLayer = doc.Layers[0];
        var top = Bitmaps.Allocate(Bitmaps.ColorInfo(9, 9)); top.Erase(SKColors.Red);
        var id = LayerPlacement.AddImage(doc, top, SKRectI.Create(9, 9), "Top", baseLayer.ID)!.Value;
        Assert.Equal(SKColors.Red, ColorSampling.Read(doc, baseLayer.ID, new(4, 4)));
        Assert.Equal(new SKColor(70, 120, 180), ColorSampling.Read(doc, baseLayer.ID, new(4, 4), scope: SampleScope.CurrentLayer));
        Assert.Equal(new SKColor(70, 120, 180), ColorSampling.Read(doc, baseLayer.ID, new(4, 4), scope: SampleScope.CurrentAndBelow));
        doc.Layers[^1].Asset!.Image.SetPixel(4, 4, SKColors.Blue);
        var average = ColorSampling.Read(doc, id, new(4, 4), 3); Assert.InRange(average.Red, 226, 227); Assert.InRange(average.Blue, 28, 29);
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 9, 9), "Invert") { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Invert } });
        Assert.Equal(SKColors.Cyan, ColorSampling.Read(doc, id, new(2, 2)));
        Assert.Equal(SKColors.Red, ColorSampling.Read(doc, id, new(2, 2), scope: SampleScope.AllWithoutAdjustments));
        Assert.Equal(SKColors.Red, ColorSampling.Read(doc, id, new(2, 2), scope: SampleScope.CurrentAndBelowWithoutAdjustments));
        Assert.Equal(SKColors.Transparent, ColorSampling.Read(doc, id, new(-1, 2)));
    }
    [Fact] public void UtilityMetadataRoundTripsAndMeasureSecondLineTransforms()
    {
        using var doc = Scene(); var id = LayerWorkflow.MakeContainer(doc, [doc.Layers[0].ID], LayerContainer.Frame, "Frame", new SKRect(10, 10, 50, 50))!.Value;
        var frame = doc.Layers.Single(l => l.ID == id);
        Assert.True(FrameEdits.Apply(frame, new FrameStyle { Shape = 2, Stroke = 0xffff0000, StrokeWidth = 3, StrokeAlignment = 2 }));
        doc.CountGroups.Add(new CountGroup("Count", 0xff0000ff, false, 4, 15));
        doc.Marks.Add(new DocumentMark { Kind = MarkKind.Note, X = 4, Y = 5, Author = "Author", Text = "Note" });
        doc.Marks.Add(new DocumentMark { Kind = MarkKind.Measure, X = 10, Y = 10, Width = 20, Height = 0, SecondX = 10, SecondY = 30 });
        DocumentMarks.Transform(doc, SKMatrix.CreateTranslation(5, 7)); Assert.Equal(15, doc.Marks[1].SecondX); Assert.Equal(37, doc.Marks[1].SecondY);
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(26, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        var path = Path.Combine(Path.GetTempPath(), "compositor-utility-" + Guid.NewGuid() + ".comp");
        try { ProjectStore.Save(snapshot, path); using var loaded = ProjectStore.Load(path); using var restored = loaded.ToDocument(); Assert.Equal(doc.CountGroups, restored.CountGroups); Assert.Equal(doc.Marks, restored.Marks); Assert.Equal(frame.FrameStyle, restored.Layers.Single(l => l.ID == id).FrameStyle); }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        snapshot.Manifest.Version = 25; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void FrameStrokeAlignmentAffectsTheCorrectSide(int alignment)
    {
        using var doc = Scene(); doc.Layers.Clear();
        var id = LayerWorkflow.MakeContainer(doc, [], LayerContainer.Frame, "Frame", new SKRect(20, 20, 44, 44))!.Value;
        var frame = doc.Layers.Single(l => l.ID == id); FrameEdits.Apply(frame, new FrameStyle { Stroke = 0xffff0000, StrokeWidth = 4, StrokeAlignment = alignment });
        using var rendered = DocumentRenderer.Render(doc);
        Assert.Equal(alignment == 0 ? 0 : 255, rendered.GetPixel(18, 30).Alpha);
        Assert.Equal(alignment == 2 ? 0 : 255, rendered.GetPixel(21, 30).Alpha);
    }
    [Theory] [InlineData(HealingMode.ContentAware)] [InlineData(HealingMode.CreateTexture)] [InlineData(HealingMode.ProximityMatch)]
    public void SpotHealingAllLayerSamplingDoesNotFlattenOutsideTheBrush(HealingMode mode)
    {
        using var doc = Scene(); var id = LayerPlacement.AddBlank(doc, doc.Layers[0].ID)!.Value; BrushEdits.EnsurePixels(doc, id);
        Assert.True(BrushEdits.Paint(doc, id, [new(32, 32)], new BrushSettings(Diameter: 8, Mode: BrushMode.Heal, Healing: mode, CloneAllLayers: true, Seed: 1)));
        var pixels = doc.Layers.Single(l => l.ID == id).Asset!.Image;
        Assert.Equal(0, pixels.GetPixel(5, 5).Alpha); Assert.Equal(255, pixels.GetPixel(32, 32).Alpha);
        Assert.InRange(pixels.GetPixel(32, 32).Blue, 170, 190);
    }
    [Fact] public void BrushAngleRotatesAnEllipticalTip()
    {
        using var doc = Scene(); var id = doc.Layers[0].ID;
        BrushEdits.Paint(doc, id, [new(32, 32)], new BrushSettings(Diameter: 20, Roundness: .2, Angle: 90, Red: 1));
        var pixels = doc.Layers[0].Asset!.Image; Assert.Equal(SKColors.Red, pixels.GetPixel(32, 39)); Assert.Equal(new SKColor(70, 120, 180), pixels.GetPixel(39, 32));
    }
    [Fact] public void RemovePreservesUnpaintedPixelsSelectionAndOriginalLayerWhenCreatingPatch()
    {
        using var doc = Scene(); var layer = doc.Layers[0]; var original = layer.Asset;
        using var builder = new SKPathBuilder(); builder.AddCircle(32, 32, 10); using var area = builder.Detach();
        doc.Selection = DocumentSelection.Rectangular(SKRectI.Create(32, 20, 20, 24), 64, 64);
        using var generated = Bitmaps.Allocate(Bitmaps.ColorInfo(64, 64)); generated.Erase(SKColors.Gold);
        var made = RemoveEdits.Apply(doc, layer.ID, area, true, true, generated); Assert.NotNull(made); Assert.Same(original, layer.Asset);
        using var rendered = DocumentRenderer.Render(doc); Assert.Equal(SKColors.Gold, rendered.GetPixel(36, 32)); Assert.Equal(new SKColor(70, 120, 180), rendered.GetPixel(28, 32)); Assert.Equal(new SKColor(70, 120, 180), rendered.GetPixel(1, 1));
        doc.Layers.RemoveAt(doc.Layers.Count - 1);
        Assert.Equal(layer.ID, RemoveEdits.Apply(doc, layer.ID, area, false, false, generated));
        Assert.Equal(SKColors.Gold, layer.Asset!.Image.GetPixel(36, 32)); Assert.Equal(original!.Image.GetPixel(28, 32), layer.Asset.Image.GetPixel(28, 32));
        layer.Locks = LayerLocks.Pixels; Assert.Null(RemoveEdits.Apply(doc, layer.ID, area, false, false));
    }
}
