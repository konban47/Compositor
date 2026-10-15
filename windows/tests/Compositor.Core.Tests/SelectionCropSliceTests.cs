using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class SelectionCropSliceTests
{
    private static CanvasDocument Image(int width = 40, int height = 30)
    {
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); image.Erase(SKColors.CornflowerBlue);
        var doc = new CanvasDocument(Guid.NewGuid(), width, height);
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Source"), new LayerTransform(0, 0, width, height), "Source")); return doc;
    }
    [Fact] public void IntersectKeepsOnlyTheSharedMarquee()
    {
        using var doc = Image(); SelectionEdits.Select(doc, SKRectI.Create(0, 0, 20, 20));
        SelectionEdits.Apply(doc, SelectionEdits.Shape(SKRectI.Create(10, 10, 20, 20), false), SelectionMode.Intersect);
        Assert.True(doc.Selection.Contains(15, 15)); Assert.False(doc.Selection.Contains(5, 5)); Assert.False(doc.Selection.Contains(25, 25));
        SelectionEdits.Deselect(doc); SelectionEdits.Apply(doc, SelectionEdits.Shape(SKRectI.Create(5, 5), false), SelectionMode.Intersect); Assert.True(doc.Selection.IsEmpty);
    }
    [Fact] public void SoftBrushHasAFalloffAndDoesNotEraseTheEarlierStamp()
    {
        using var doc = Image(); SelectionBrushEdits.Paint(doc, [new(10, 15), new(25, 15)], 16, .7, SelectionPaintMode.New, .25);
        using var mask = doc.Selection.Coverage(SKRectI.Create(40, 30));
        Assert.InRange(mask!.GetPixel(10, 15).Red, 175, 180); Assert.InRange(mask.GetPixel(10, 21).Red, 1, 140); Assert.Equal(0, mask.GetPixel(10, 25).Red);
    }
    [Theory] [InlineData(false)] [InlineData(true)] public void DeleteCropPixelsChangesDiscardedRasterAndUndoRestoresIt(bool delete)
    {
        using var doc = Image(); var history = new DocumentHistory(); var id = doc.Layers[0].ID;
        history.Begin("Crop", doc, id); Assert.True(AdvancedCropEdits.Apply(doc, SKRectI.Create(10, 5, 20, 20), delete)); history.End(doc, id);
        Assert.Equal(20, doc.Width); Assert.Equal(delete ? 0 : 255, doc.Layers[0].Asset!.Image.GetPixel(0, 0).Alpha); Assert.Equal(255, doc.Layers[0].Asset!.Image.GetPixel(15, 10).Alpha);
        var old = history.Undo()!.Value.Document!; Assert.Equal(40, old.Width); Assert.Equal(255, old.Layers[0].Asset!.Image.GetPixel(0, 0).Alpha);
    }
    [Fact] public void CropResamplesAndStoresResolution()
    {
        using var doc = Image(); Assert.True(AdvancedCropEdits.Apply(doc, SKRectI.Create(0, 0, 20, 20), false, width: 60, height: 60, resolution: 300)); Assert.Equal(60, doc.Width); Assert.Equal(300, doc.Resolution);
    }
    [Fact] public void CombiningAPathWithASoftSelectionKeepsItsCoverage()
    {
        using var doc = Image(); SelectionBrushEdits.Paint(doc, [new(15, 15)], 20, .5, SelectionPaintMode.New, .5);
        using var path = SelectionEdits.Shape(SKRectI.Create(10, 0, 20, 30), false); SelectionEdits.Apply(doc, path, SelectionMode.Intersect);
        using var coverage = doc.Selection.Coverage(SKRectI.Create(40, 30)); Assert.InRange(coverage!.GetPixel(15, 15).Red, 126, 129); Assert.Equal(0, coverage.GetPixel(8, 15).Red);
    }
    [Fact] public void SmoothRectangleSettingSurvivesProjectSerialization()
    {
        using var doc = Image(); var style = new LayerShapeStyle { Kind = ShapeKind.Rectangle, CornerRadius = 8, Smoothing = true }; var id = ShapeEdits.Add(doc, style, SKRectI.Create(40, 30), null);
        using var saved = ProjectSnapshot.FromDocument(doc); Assert.Equal(23, saved.Manifest.Version);
        var json = System.Text.Json.JsonSerializer.Serialize(saved.Manifest, ManifestJson.Options); var copy = System.Text.Json.JsonSerializer.Deserialize<ProjectManifest>(json, ManifestJson.Options)!;
        Assert.True(copy.Layers.Single(l => l.ID == id).Shape!.Smoothing);
    }
    [Fact] public void ContentAwareExpansionLeavesOriginalPixelsIntact()
    {
        using var doc = Image(8, 8); Assert.True(AdvancedCropEdits.Apply(doc, SKRectI.Create(-2, -2, 12, 12), false, true));
        using var rendered = Compositor.Core.Rendering.DocumentRenderer.Render(doc); Assert.Equal(SKColors.CornflowerBlue, rendered.GetPixel(6, 6)); Assert.Equal(255, rendered.GetPixel(0, 0).Alpha);
    }
    [Fact] public void ObjectRangeSelectsWholeTouchedComponents()
    {
        using var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(30, 20)); mask.Erase(SKColors.Black); using (var canvas = new SKCanvas(mask)) { using var paint = new SKPaint { Color = SKColors.White }; canvas.DrawRect(2, 2, 8, 12, paint); canvas.DrawRect(20, 2, 8, 12, paint); }
        using var range = SelectionEdits.Shape(SKRectI.Create(4, 4, 2, 2), false); using var found = SubjectEdits.InRegion(mask, range);
        Assert.True(found.Contains(8, 12)); Assert.False(found.Contains(24, 12));
    }
    [Fact] public void PixelSliceDivisionKeepsTheRemainderAndMetadata()
    {
        var slice = new DocumentMark { Kind = MarkKind.Slice, Width = 100, Height = 50, Target = "_blank", AltText = "preview" };
        var pieces = SliceEdits.Divide(slice, 4, 2, 30, 30); Assert.Equal(8, pieces.Count); Assert.Equal(10, pieces[^1].Width); Assert.Equal(20, pieces[^1].Height); Assert.All(pieces, p => Assert.Equal("preview", p.AltText)); Assert.Equal(8, pieces.Select(p => p.ID).Distinct().Count());
    }
    [Fact] public void AutomaticSlicesCoverOnlyUnclaimedCanvasAndHaveStableIds()
    {
        using var doc = Image(100, 80); doc.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 20, Y = 10, Width = 40, Height = 30 });
        var auto = SliceEdits.Automatic(doc); Assert.Equal(6800, auto.Sum(s => s.Width * s.Height)); Assert.Equal(auto.Select(s => s.ID), SliceEdits.Automatic(doc).Select(s => s.ID)); Assert.DoesNotContain(auto, m => m.X < 60 && m.X + m.Width > 20 && m.Y < 40 && m.Y + m.Height > 10);
    }
    [Fact] public void SliceAlignmentAndOrderAffectOnlySelectedSlices()
    {
        using var doc = Image(100, 80); var a = new DocumentMark { Kind = MarkKind.Slice, X = 10, Width = 10, Height = 10 }; var b = a with { ID = Guid.NewGuid(), X = 30 }; var c = a with { ID = Guid.NewGuid(), X = 70 }; doc.Marks.AddRange([a, b, c]);
        Assert.True(SliceEdits.Arrange(doc, new HashSet<Guid> { a.ID, b.ID, c.ID }, "Distribute Horizontal Spacing")); Assert.Equal(40, doc.Marks[1].X);
        Assert.True(SliceEdits.Arrange(doc, new HashSet<Guid> { a.ID }, "Bring Slice to Front")); Assert.Equal(a.ID, doc.Marks[^1].ID);
        Assert.True(SliceEdits.Arrange(doc, new HashSet<Guid> { a.ID }, "Align Slice Right", true)); Assert.Equal(90, doc.Marks[^1].X);
        Assert.True(SliceEdits.Arrange(doc, new HashSet<Guid> { a.ID, b.ID, c.ID }, "Distribute H Center", true)); Assert.Equal(new double[] { 0, 45, 90 }, doc.Marks.OrderBy(m => m.X).Select(m => m.X));
    }
    [Fact] public void NewSliceMetadataAndSmoothCornersUseVersion23()
    {
        using var doc = Image(); doc.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, Target = "_blank", AltText = "Image", Background = 0xff123456, NoImage = true });
        using var saved = ProjectSnapshot.FromDocument(doc); Assert.Equal(23, saved.Manifest.Version);
        var json = System.Text.Json.JsonSerializer.Serialize(saved.Manifest, ManifestJson.Options); var copy = System.Text.Json.JsonSerializer.Deserialize<ProjectManifest>(json, ManifestJson.Options)!; Assert.Equal(doc.Marks[0], copy.Marks![0]);
    }
    [Fact] public void SmoothedCornersKeepBoundsAndFallBackAtZeroOrMaximumRadius()
    {
        var box = SKRect.Create(100, 70); using var smooth = SmoothCorners.Rectangle(box, 15, true); using var normal = SmoothCorners.Rectangle(box, 15, false);
        Assert.Equal(box, smooth.TightBounds); Assert.NotEqual(normal.ToSvgPathData(), smooth.ToSvgPathData());
        using var square = SmoothCorners.Rectangle(box, 0, true); Assert.True(square.Contains(.5f, .5f));
        using var pill = SmoothCorners.Rectangle(box, 35, true); using var circular = SmoothCorners.Rectangle(box, 35, false); Assert.Equal(circular.ToSvgPathData(), pill.ToSvgPathData());
    }
    [Theory] [InlineData(2000, 2000, 4)] [InlineData(2001, 2000, 8)] public void RawScopeSamplingMatchesUpstream(int w, int h, int step) => Assert.Equal(step, CameraRawScope.SamplingStep(w, h));
}
