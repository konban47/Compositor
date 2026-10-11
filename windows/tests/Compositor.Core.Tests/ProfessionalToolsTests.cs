using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class ProfessionalToolsTests
{
    private static CanvasDocument Document()
    {
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(40, 30)); pixels.Erase(new SKColor(140, 70, 50));
        for (var y = 0; y < 30; y++) for (var x = 20; x < 40; x++) pixels.SetPixel(x, y, new SKColor(20, 150, 210));
        var doc = new CanvasDocument(Guid.NewGuid(), 40, 30);
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "image"), new LayerTransform(0, 0, 40, 30), "image")); return doc;
    }
    private static void Select(CanvasDocument doc, SKRect rect)
    { using var path = new SKPathBuilder(); path.AddRect(rect); doc.Selection = DocumentSelection.FromPath(path.Detach()); }
    [Fact] public void SelectionBrushCombinesSoftCoverageAndQuickSelectionStopsAtEdge()
    {
        using var doc = Document();
        Assert.True(SelectionBrushEdits.Paint(doc, [new(10, 10)], 12, .5, SelectionPaintMode.New));
        using (var mask = doc.Selection.Coverage(SKRectI.Create(40, 30))) { Assert.InRange(mask!.GetPixel(10, 10).Red, 126, 129); Assert.Equal(0, mask.GetPixel(1, 1).Red); }
        SelectionBrushEdits.Paint(doc, [new(10, 10)], 4, 1, SelectionPaintMode.Subtract);
        using (var mask = doc.Selection.Coverage(SKRectI.Create(40, 30))) { Assert.Equal(0, mask!.GetPixel(10, 10).Red); Assert.InRange(mask.GetPixel(14, 10).Red, 120, 135); }
        Assert.True(SelectionBrushEdits.Quick(doc, doc.Layers[0].ID, [new(4, 4), new(15, 20)], 10, 10, SelectionPaintMode.New));
        Assert.True(doc.Selection.Path!.Contains(10, 15)); Assert.False(doc.Selection.Path.Contains(30, 15));
        SelectionBrushEdits.Paint(doc, [new(10, 15)], 8, 1, SelectionPaintMode.Intersect);
        Assert.True(doc.Selection.Path!.Contains(10, 15)); Assert.False(doc.Selection.Path.Contains(2, 2));
    }
    [Fact] public void MagneticSearchFollowsHighContrastEdgeAndThresholdCanDisableIt()
    {
        using var doc = Document(); var image = doc.Layers[0].Asset!.Image;
        var edge = SelectionBrushEdits.SnapEdge(image, new(16, 12), 10, 10);
        Assert.InRange(edge.X, 19, 20); Assert.Equal(new SKPoint(16, 12), SelectionBrushEdits.SnapEdge(image, new(16, 12), 10, 255));
    }
    [Theory] [InlineData(BrushMode.ColorReplacement)] [InlineData(BrushMode.Mixer)] [InlineData(BrushMode.HealingSample)]
    public void NewBrushModesRespectSelectionAndUndo(BrushMode mode)
    {
        using var doc = Document(); var layer = doc.Layers[0]; layer.Asset!.Image.SetPixel(10, 10, new SKColor(200, 10, 10));
        Select(doc, new SKRect(8, 8, 15, 15)); var history = new DocumentHistory(); history.Begin("Paint", doc, layer.ID);
        Assert.True(BrushEdits.Paint(doc, layer.ID, [new(10, 10), new(13, 10)], new BrushSettings(Diameter: 18, Mode: mode, Red: 0, Green: 0, Blue: 1, Hardness: 1, Tolerance: 255, CloneFrom: new(20, 0))));
        history.End(doc, layer.ID); Assert.NotEqual(new SKColor(200, 10, 10), layer.Asset!.Image.GetPixel(10, 10));
        Assert.Equal(new SKColor(140, 70, 50), layer.Asset.Image.GetPixel(6, 10));
        Assert.Equal(new SKColor(200, 10, 10), history.Undo()!.Value.Document!.Layers[0].Asset!.Image.GetPixel(10, 10));
    }
    [Fact] public void CleanMixerDoesNotIntroduceForegroundPaint()
    {
        using var doc = Document(); var layer = doc.Layers[0];
        BrushEdits.Paint(doc, layer.ID, [new(5, 10), new(12, 10)], new BrushSettings(Mode: BrushMode.Mixer, Diameter: 6, MixerLoad: 0, MixerMix: 0, MixerWet: 1, Red: 0, Green: 0, Blue: 1));
        Assert.Equal(new SKColor(140, 70, 50), layer.Asset!.Image.GetPixel(10, 10));
    }
    [Fact] public void PatchAndRedEyeRespectLocksAndSelection()
    {
        using var doc = Document(); var layer = doc.Layers[0]; Select(doc, new SKRect(5, 5, 12, 12));
        Assert.True(RepairEdits.Patch(doc, layer.ID, new(20, 0), false)); Assert.Equal(new SKColor(20, 150, 210), layer.Asset!.Image.GetPixel(8, 8)); Assert.Equal(new SKColor(140, 70, 50), layer.Asset.Image.GetPixel(3, 8));
        Select(doc, new SKRect(0, 0, 20, 20)); Assert.True(RepairEdits.RedEye(doc, layer.ID, new SKRect(0, 0, 15, 15), 1, .8));
        Assert.True(layer.Asset!.Image.GetPixel(4, 7).Red < 70); Assert.Equal(new SKColor(20, 150, 210), layer.Asset.Image.GetPixel(30, 10));
        layer.Locks = LayerLocks.Pixels; Assert.False(RepairEdits.Patch(doc, layer.ID, new(20, 0), true)); Assert.False(RepairEdits.RedEye(doc, layer.ID, new SKRect(0, 0, 20, 20), 1, 1));
    }
    [Fact] public void ContentExtendPreservesSourceAndMovesSelection()
    {
        using var doc = Document(); var layer = doc.Layers[0]; Select(doc, new SKRect(4, 4, 10, 10));
        Assert.True(RepairEdits.MoveContent(doc, layer.ID, 20, 0, true));
        Assert.Equal(new SKColor(140, 70, 50), layer.Asset!.Image.GetPixel(7, 7)); Assert.Equal(new SKColor(140, 70, 50), layer.Asset.Image.GetPixel(27, 7));
        Assert.True(doc.Selection.Path!.Contains(27, 7)); Assert.False(doc.Selection.Path.Contains(7, 7));
    }
    [Fact] public void PerspectiveCropMapsCornersAndPreservesLayerMaskChannelsAndMarks()
    {
        using var doc = Document(); var layer = doc.Layers[0]; layer.Mask = Compositor.Core.Model.LayerMask.Solid(false);
        layer.Mask!.IsLinked = false; layer.Mask.Density = .7;
        ChannelEdits.Add(doc, "Alpha", false); doc.Marks.Add(new DocumentMark { Kind = MarkKind.Note, X = 10, Y = 8, Text = "中文" });
        SKPoint[] quad = [new(5, 3), new(35, 3), new(35, 27), new(5, 27)];
        var map = PerspectiveCropEdits.UnitToQuad(quad)!.Value; Assert.Equal(quad[2], map.MapPoint(1, 1));
        Assert.True(PerspectiveCropEdits.Apply(doc, quad, 30, 24)); Assert.Equal(30, doc.Width); Assert.Equal(24, doc.Height);
        Assert.Equal(new SKColor(140, 70, 50), layer.Asset!.Image.GetPixel(3, 3)); Assert.Equal(new SKColor(20, 150, 210), layer.Asset.Image.GetPixel(25, 3));
        Assert.False(layer.Mask!.IsLinked); Assert.Equal(.7, layer.Mask.Density); Assert.Equal(30, doc.Channels[0].Asset.Width);
        Assert.Equal(5, doc.Marks[0].X, 4); Assert.Equal(5, doc.Marks[0].Y, 4); ProjectStore.Validate(ProjectSnapshot.FromDocument(doc).Manifest);
    }
    [Fact] public void ContentMoveFillsSourceHoleAndMarksFollowRotationAndFlip()
    {
        using var doc = Document(); var layer = doc.Layers[0];
        for (var y = 5; y < 10; y++) for (var x = 5; x < 10; x++) layer.Asset!.Image.SetPixel(x, y, SKColors.Lime);
        Select(doc, new SKRect(5, 5, 10, 10)); Assert.True(RepairEdits.MoveContent(doc, layer.ID, 20, 0, false));
        Assert.Equal(SKColors.Lime, layer.Asset!.Image.GetPixel(27, 7)); Assert.Equal(255, layer.Asset.Image.GetPixel(7, 7).Alpha); Assert.NotEqual(SKColors.Lime, layer.Asset.Image.GetPixel(7, 7));
        doc.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 2, Y = 3, Width = 8, Height = 6 });
        doc.Marks.Add(new DocumentMark { Kind = MarkKind.Measure, X = 2, Y = 3, Width = 8, Height = 6 });
        Assert.True(CanvasRotation.Rotate(doc, true)); Assert.Equal(21, doc.Marks[0].X); Assert.Equal(2, doc.Marks[0].Y); Assert.Equal(6, doc.Marks[0].Width); Assert.Equal(8, doc.Marks[0].Height);
        Assert.Equal(-6, doc.Marks[1].Width); Assert.Equal(8, doc.Marks[1].Height);
        LayerEdits.FlipCanvas(doc, true); Assert.Equal(3, doc.Marks[0].X); Assert.Equal(6, doc.Marks[1].Width);
        using var copied = DocumentCopies.Independent(doc); Assert.Equal(doc.Marks, copied.Marks);
    }
    [Fact] public void PerspectiveCropRejectsCrossingOrOversizedQuadWithoutEdits()
    {
        using var doc = Document(); using var before = doc.Clone();
        Assert.False(PerspectiveCropEdits.Apply(doc, [new(0, 0), new(20, 20), new(20, 0), new(0, 20)], 20, 20));
        Assert.False(PerspectiveCropEdits.Apply(doc, [new(0, 0), new(40, 0), new(40, 30), new(0, 30)], 30000, 30000));
        Assert.True(doc.SameAs(before));
    }
    [Fact] public void MarksRoundTripCloneAndHistoryAreIndependent()
    {
        using var doc = Document(); doc.Marks.Add(new DocumentMark { Kind = MarkKind.Note, X = 2, Y = 4, Text = "标注", Color = 0xFF112233 });
        using var before = doc.Clone(); doc.Marks[0] = doc.Marks[0] with { Text = "修改" }; Assert.Equal("标注", before.Marks[0].Text); Assert.False(before.SameAs(doc));
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(17, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        var path = Path.Combine(Path.GetTempPath(), "compositor-marks-" + Guid.NewGuid() + ".comp");
        try { ProjectStore.Save(snapshot, path); using var loaded = ProjectStore.Load(path); using var restored = loaded.ToDocument(); Assert.Equal(doc.Marks, restored.Marks); }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        using var subset = LayerWorkflow.Subset(doc, [doc.Layers[0].ID]); Assert.Empty(subset.Marks);
    }
    [Theory] [InlineData(16)] [InlineData(17)]
    public void MarkValidationRejectsOldSchemaOrDuplicateIdentifiers(int version)
    {
        using var doc = Document(); var mark = new DocumentMark { Kind = MarkKind.Count }; doc.Marks.Add(mark);
        var manifest = ProjectSnapshot.FromDocument(doc).Manifest; manifest.Version = version;
        if (version == 17) manifest.Marks!.Add(mark);
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(manifest));
    }
    [Theory] [InlineData(.05)] [InlineData(1)] [InlineData(5)]
    public void WorkspaceGridCoversNegativeAndFarEdgesAtEveryZoom(double zoom)
    {
        var grid = new LayoutGrid(64, 8); var lines = grid.WorkspaceLines(-300, -250, 1600, 1100, zoom, -100, -90);
        var xs = lines.VerticalFine.Concat(lines.VerticalMajor).Order().ToArray(); var ys = lines.HorizontalFine.Concat(lines.HorizontalMajor).Order().ToArray();
        Assert.True(xs[0] <= -300 && xs[^1] >= 1600); Assert.True(ys[0] <= -250 && ys[^1] >= 1100); Assert.True(xs.Length < 2000 && ys.Length < 2000);
        Assert.Contains(lines.VerticalMajor, x => Math.Abs(x / zoom - 100) < .001);
    }
}
