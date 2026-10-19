using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class PenPathTests : ProjectTestBase
{
    private const string Box = "M10 10 L30 10 L30 30 L10 30 Z";
    private const string Overlap = "M20 10 L40 10 L40 30 L20 30 Z";

    [Fact] public void WorkPathRoundTripsWithoutChangingRenderedPixels()
    {
        using var doc = LayerPlacement.NewDocument(60, 50)!;
        using var before = DocumentRenderer.Render(doc);
        doc.WorkPath = Box;
        using var after = DocumentRenderer.Render(doc);
        Assert.Equal(before.Bytes, after.Bytes);
        using var snapshot = ProjectSnapshot.FromDocument(doc);
        Assert.Equal(24, snapshot.Manifest.Version);
        ProjectStore.Save(snapshot, PathIn("Path.comp"));
        using var loaded = ProjectStore.Load(PathIn("Path.comp"));
        using var restored = loaded.ToDocument(); Assert.Equal(Box, restored.WorkPath);
        doc.WorkPath = null;
        using var legacy = ProjectSnapshot.FromDocument(doc); Assert.True(legacy.Manifest.Version < 24);
    }
    [Fact] public void WorkPathParticipatesInHistoryAndAdoption()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 60, 50);
        var history = new DocumentHistory(); history.Begin("Pen", doc, null); doc.WorkPath = Box; history.End(doc, null);
        using var copy = doc.Clone(); Assert.Equal(Box, copy.WorkPath);
        doc.WorkPath = Overlap; Assert.False(doc.SameAs(copy)); doc.Adopt(copy); Assert.Equal(Box, doc.WorkPath);
        Assert.Null(history.Undo()!.Value.Document!.WorkPath); Assert.Equal(Box, history.Redo()!.Value.Document!.WorkPath);
    }
    [Theory] [InlineData("garbage")] [InlineData("")] [InlineData("M0 0LNaN 1")]
    public void RejectsMalformedPersistedPaths(string svg)
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 60, 50) { WorkPath = svg };
        using var snapshot = ProjectSnapshot.FromDocument(doc);
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact] public void WorkPathRequiresVersion24()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 60, 50) { WorkPath = Box };
        using var snapshot = ProjectSnapshot.FromDocument(doc); snapshot.Manifest.Version = 23;
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Theory]
    [InlineData(PathOperation.New, false, true, true)]
    [InlineData(PathOperation.Combine, true, true, true)]
    [InlineData(PathOperation.Subtract, true, false, false)]
    [InlineData(PathOperation.Intersect, false, true, false)]
    [InlineData(PathOperation.Exclude, true, false, true)]
    public void BooleanOperationsProduceExpectedCoverage(PathOperation operation, bool left, bool middle, bool right)
    {
        using var path = SKPath.ParseSvgPathData(PenPaths.Combine(Box, Overlap, operation)!);
        Assert.Equal(left, path.Contains(15, 20)); Assert.Equal(middle, path.Contains(25, 20)); Assert.Equal(right, path.Contains(35, 20));
    }
    [Fact] public void CombineKeepsComponentsAndMergeRemovesInteriorEdges()
    {
        var combined = PenPaths.Combine(Box, Overlap, PathOperation.Combine)!;
        Assert.Equal(2, PathNodes.Parse(combined)!.Subpaths.Count);
        var merged = PenPaths.Merge(combined)!; Assert.Single(PathNodes.Parse(merged)!.Subpaths);
        using var path = SKPath.ParseSvgPathData(merged); Assert.True(path.Contains(25, 20));
    }
    [Fact] public void MergingDoesNotFillASubtractedHole()
    {
        var donut = PenPaths.Combine(Box, "M15 15L25 15L25 25L15 25Z", PathOperation.Subtract)!;
        using (var before = SKPath.ParseSvgPathData(donut)) Assert.False(before.Contains(20, 20));
        using var path = SKPath.ParseSvgPathData(PenPaths.Merge(donut)!);
        Assert.True(path.Contains(12, 20)); Assert.False(path.Contains(20, 20));
    }
    [Fact] public void FittingSimplifiesWithoutLosingEndpointsOrClosedContours()
    {
        var points = Enumerable.Range(0, 100).Select(i => new SKPoint(i, 20 + (float)Math.Sin(i / 9.0) * 10)).ToArray();
        var fine = PenPaths.Fit(points, .5, false); var coarse = PenPaths.Fit(points, 5, false);
        Assert.True(fine.NodeCount > coarse.NodeCount); Assert.Equal(points[0], coarse.Subpaths[0].Nodes[0].Point); Assert.Equal(points[^1], coarse.Subpaths[0].Nodes[^1].Point);
        var closed = PenPaths.Fit([new(0, 0), new(20, 0), new(20, 20), new(0, 20)], 50, true);
        Assert.True(closed.Subpaths[0].Closed); Assert.True(closed.NodeCount >= 3); Assert.All(closed.Subpaths[0].Nodes, n => Assert.NotNull(n.In));
    }
    [Fact] public void WorkPathFollowsResizeCropAndFlip()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 60, 50) { WorkPath = Box };
        Assert.True(ImageEdits.Resize(doc, 120, 100, 72));
        using (var path = SKPath.ParseSvgPathData(doc.WorkPath)) Assert.Equal(SKRect.Create(20, 20, 40, 40), path.Bounds);
        Assert.True(CanvasEdits.Crop(doc, SKRectI.Create(10, 10, 80, 70)));
        using (var path = SKPath.ParseSvgPathData(doc.WorkPath)) Assert.Equal(SKRect.Create(10, 10, 40, 40), path.Bounds);
        LayerEdits.FlipCanvas(doc, true);
        using (var path = SKPath.ParseSvgPathData(doc.WorkPath)) Assert.Equal(SKRect.Create(30, 10, 40, 40), path.Bounds);
    }
    [Fact] public void DistanceUsesTheCurveRatherThanItsBoundingBox()
    {
        var nodes = PathNodes.Parse("M0 0 C0 100 100 100 100 0")!;
        Assert.InRange(PenPaths.DistanceToPath(nodes, new(50, 75)), 0, .01);
        Assert.InRange(PenPaths.DistanceToPath(nodes, new(50, 0)), 49, 51);
    }
}
