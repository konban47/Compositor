using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerMask = Compositor.Core.Model.LayerMask;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public class InspectorTests
{
    private static CanvasDocument Document()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 80, 60);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(40, 30)); pixels.Erase(SKColors.Red);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Pixels"), new LayerTransform(10, 10, 40, 30), "Pixels"));
        SelectionEdits.Select(document, SKRectI.Create(10, 10, 20, 30));
        MaskProperties.FromSelection(document, document.Layers[0].ID);
        return document;
    }

    [Fact]
    public void LinkAndEnableAreIndependentSnapshotsAndUndoable()
    {
        using var document = Document(); var id = document.Layers[0].ID; var history = new DocumentHistory();
        history.Begin("Unlink Layer Mask", document, id); LayerMaskEdits.SetLinked(document, id, false); history.End(document, id);
        Assert.True(history.CanUndo); document.Adopt(history.Undo()!.Value.Document!); Assert.True(document.Layers[0].Mask!.IsLinked);
        document.Adopt(history.Redo()!.Value.Document!); Assert.False(document.Layers[0].Mask!.IsLinked);
        history.Begin("Disable Layer Mask", document, id); LayerMaskEdits.SetEnabled(document, id, false); history.End(document, id);
        document.Adopt(history.Undo()!.Value.Document!); Assert.True(document.Layers[0].Mask!.IsEnabled);
    }

    [Fact]
    public void UnlinkedMaskStaysBehindButRelinkCarriesItsOffset()
    {
        using var document = Document(); var layer = document.Layers[0]; var original = layer.Transform;
        layer.Mask!.IsLinked = false; LayerEdits.Move(document, layer.ID, 10, 0);
        Assert.Equal(original, layer.MaskTransform); Assert.Equal(20, layer.Transform.X);
        using (var rendered = DocumentRenderer.Render(document)) { Assert.Equal(255, rendered.GetPixel(25, 20).Alpha); Assert.Equal(0, rendered.GetPixel(35, 20).Alpha); }
        layer.Mask.IsLinked = true; LayerEdits.Move(document, layer.ID, 10, 0);
        Assert.Equal(20, layer.MaskTransform.X); Assert.Equal(30, layer.Transform.X);
        using var linked = DocumentRenderer.Render(document); Assert.Equal(255, linked.GetPixel(35, 20).Alpha); Assert.Equal(0, linked.GetPixel(45, 20).Alpha);
    }

    [Fact]
    public void DragCanReturnToItsStartingPoint()
    {
        using var document = Document(); var layer = document.Layers[0]; layer.Mask!.IsLinked = false;
        var original = layer.Transform; var originals = new Dictionary<Guid, LayerTransform> { [layer.ID] = original };
        TransformEdits.Carry(document, originals, original, original with { X = original.X + 10 });
        TransformEdits.Carry(document, originals, original, original);
        Assert.Equal(original, layer.Transform); Assert.Equal(original, layer.MaskTransform);
    }

    [Fact]
    public void DensityAndFeatherAreNonDestructiveAndUndoable()
    {
        using var document = Document(); var layer = document.Layers[0]; var original = layer.Mask!.Asset;
        var history = new DocumentHistory(); history.Begin("Mask Properties", document, layer.ID);
        Assert.True(MaskProperties.Set(document, layer.ID, .5, 3)); history.End(document, layer.ID);
        Assert.Same(original, layer.Mask.Asset);
        using (var result = DocumentRenderer.Render(document))
        {
            Assert.InRange(result.GetPixel(44, 24).Alpha, (byte)126, (byte)130);
            Assert.InRange(result.GetPixel(29, 24).Alpha, (byte)129, (byte)254);
        }
        document.Adopt(history.Undo()!.Value.Document!); Assert.Equal(1, document.Layers[0].Mask!.Density); Assert.Equal(0, document.Layers[0].Mask!.Feather);
    }

    [Fact]
    public void FeatheredMaskIsIdenticalAcrossTileBoundaries()
    {
        using var document = Document(); MaskProperties.Set(document, document.Layers[0].ID, .8, 8);
        using var whole = DocumentRenderer.Render(document);
        foreach (var region in new[] { SKRectI.Create(0, 0, 30, 60), SKRectI.Create(30, 0, 50, 60) })
        {
            using var tile = DocumentRenderer.RenderRegion(document, region);
            for (var y = 0; y < tile.Height; y++) for (var x = 0; x < tile.Width; x++)
                Assert.Equal(whole.GetPixel(x + region.Left, y + region.Top), tile.GetPixel(x, y));
        }
    }

    [Fact]
    public void ApplyMaskMatchesRenderingAndCanBeUndone()
    {
        using var document = Document(); var layer = document.Layers[0]; layer.Mask!.Placement = layer.Transform with { X = 17, Rotation = 30 };
        MaskProperties.Set(document, layer.ID, .7, 2); using var before = DocumentRenderer.Render(document);
        var history = new DocumentHistory(); history.Begin("Apply Layer Mask", document, layer.ID);
        Assert.True(MaskProperties.Apply(document, layer.ID)); history.End(document, layer.ID);
        Assert.Null(layer.Mask); using var after = DocumentRenderer.Render(document);
        var maxDifference = 0; var differingAt = "";
        for (var y = 0; y < before.Height; y++) for (var x = 0; x < before.Width; x++)
        {
            var difference = Math.Abs(before.GetPixel(x, y).Alpha - after.GetPixel(x, y).Alpha);
            if (difference > maxDifference) { maxDifference = difference; differingAt = $"{x},{y}: {before.GetPixel(x,y).Alpha}/{after.GetPixel(x,y).Alpha}"; }
        }
        // Skia quantizes a rotated edge differently after changing raster origin; allow two 8-bit coverage levels.
        Assert.True(maxDifference <= 2, $"Mask application differs by {maxDifference} at {differingAt}");
        document.Adopt(history.Undo()!.Value.Document!); Assert.NotNull(document.Layers[0].Mask);
    }

    [Fact]
    public void VectorMaskRetainsAPathAndRasterEditsConvertIt()
    {
        using var document = Document(); var layer = document.Layers[0]; MaskProperties.FromSelection(document, layer.ID, true);
        Assert.NotNull(layer.Mask!.VectorPath);
        Assert.Equal(0, layer.Mask.Asset.Thumbnail.GetPixel(30, 15).Red);
        using var result = DocumentRenderer.Render(document); Assert.Equal(255, result.GetPixel(20, 20).Alpha); Assert.Equal(0, result.GetPixel(40, 20).Alpha);
        Assert.True(MaskProperties.Invert(document, layer.ID)); Assert.Null(layer.Mask.VectorPath);
        using var inverted = DocumentRenderer.Render(document); Assert.Equal(0, inverted.GetPixel(20, 20).Alpha); Assert.Equal(255, inverted.GetPixel(40, 20).Alpha);
    }

    [Fact]
    public void MaskLoadPreservesSoftCoverageAndDeletePreservesPixels()
    {
        using var document = Document(); var layer = document.Layers[0]; var pixels = layer.Asset;
        MaskProperties.Set(document, layer.ID, .5, 0); MaskProperties.LoadSelection(document, layer.ID);
        using var selection = document.Selection.Coverage(SKRectI.Create(0, 0, 80, 60));
        Assert.InRange(selection!.GetPixel(40, 20).Red, (byte)127, (byte)128);
        LayerMaskEdits.Remove(document, layer.ID); Assert.Same(pixels, layer.Asset);
    }

    [Fact]
    public void MaskAndTypographyRoundTripWithVersion13()
    {
        using var document = Document(); var id = document.Layers[0].ID;
        MaskProperties.FromSelection(document, id, true); MaskProperties.Set(document, id, .75, 3.5);
        var textID = TextEdits.Add(document, new LayerTextStyle { Content = "中文 Text", FontSize = 14, Bold = true, Italic = true, Underline = true, Strikethrough = true }, new SKPoint(2, 2));
        var snapshot = ProjectSnapshot.FromDocument(document); Assert.Equal(13, snapshot.Manifest.Version);
        var folder = Path.Combine(Path.GetTempPath(), "inspector-" + Guid.NewGuid() + ".comp");
        try
        {
            ProjectStore.Save(snapshot, folder); using var loaded = ProjectStore.Load(folder); using var reopened = loaded.ToDocument();
            var mask = reopened.Layers[0].Mask!; Assert.Equal(.75, mask.Density); Assert.Equal(3.5, mask.Feather); Assert.NotNull(mask.VectorPath);
            var text = reopened.Layers.Single(layer => layer.ID == textID).LiveText!;
            Assert.True(text.Bold); Assert.True(text.Italic); Assert.True(text.Underline); Assert.True(text.Strikethrough);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void InvalidOrOlderVersionMaskMetadataIsRejected()
    {
        using var document = Document(); var snapshot = ProjectSnapshot.FromDocument(document); var record = snapshot.Manifest.Layers[0];
        record.MaskDensity = .5; snapshot.Manifest.Version = 12; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        snapshot.Manifest.Version = 13; record.MaskDensity = double.NaN; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        record.MaskDensity = .5; record.MaskFeather = -1; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        record.MaskFeather = 0; record.MaskVectorPath = "not a path"; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }

    [Fact]
    public void HistoryNavigationBranchesWithoutMutatingSnapshots()
    {
        using var document = Document(); var id = document.Layers[0].ID; var history = new DocumentHistory();
        var initial = history.CurrentRevision;
        for (var i = 0; i < 3; i++) { history.Begin("Move", document, id); LayerEdits.Move(document, id, 5, 0); history.End(document, id); }
        var states = history.States(document, id); Assert.Equal(4, states.Count);
        var snapshot = history.CreateSnapshot("Position", document, id)!;
        document.Adopt(history.GoTo(states[1].Snapshot.Revision)!.Value.Document!); Assert.Equal(15, document.Layers[0].Transform.X); Assert.True(history.CanRedo);
        history.Begin("Move", document, id); LayerEdits.Move(document, id, 1, 0); history.End(document, id);
        Assert.False(history.CanRedo); Assert.Equal(25, snapshot.State.Document!.Layers[0].Transform.X);
        document.Adopt(history.GoTo(initial)!.Value.Document!); Assert.Equal(10, document.Layers[0].Transform.X);
    }

    [Fact]
    public void DeleteHistoryStateKeepsEarlierStatesAndSavedRevision()
    {
        using var document = Document(); var id = document.Layers[0].ID; var history = new DocumentHistory();
        history.Begin("Move", document, id); LayerEdits.Move(document, id, 5, 0); history.End(document, id); history.MarkSaved();
        history.Begin("Move", document, id); LayerEdits.Move(document, id, 5, 0); history.End(document, id);
        document.Adopt(history.DeleteCurrentAndLater()!.Value.Document!); Assert.Equal(15, document.Layers[0].Transform.X);
        Assert.False(history.CanRedo); Assert.False(history.IsModified); Assert.True(history.CanUndo);
        history.ClearSteps(); Assert.False(history.CanUndo); Assert.False(history.IsModified);
    }

    [Fact]
    public void IndependentSnapshotDocumentSurvivesOriginalClosing()
    {
        var original = Document(); var independent = DocumentCopies.Independent(original); var before = original.Layers[0].Asset!.Image.GetPixel(10, 10);
        Assert.NotEqual(original.ID, independent.ID); Assert.NotSame(original.Layers[0].Asset!.Image, independent.Layers[0].Asset!.Image);
        original.Dispose(); Assert.Equal(before, independent.Layers[0].Asset!.Image.GetPixel(10, 10));
        using var rendering = DocumentRenderer.Render(independent); Assert.Equal(255, rendering.GetPixel(20, 20).Alpha); independent.Dispose();
    }

    [Fact]
    public void SingleAlignmentUsesCanvasAndMultipleDistributionUsesCenters()
    {
        using var document = Document(); var first = document.Layers[0];
        LayerAlignment.Apply(document, [first.ID], "Align Right"); Assert.Equal(40, first.Transform.X);
        var second = first.Copy(Guid.NewGuid(), "Second", null, null); second.Transform = first.Transform with { X = 10 }; document.Layers.Add(second);
        var third = first.Copy(Guid.NewGuid(), "Third", null, null); third.Transform = first.Transform with { X = 70 }; document.Layers.Add(third);
        LayerAlignment.Apply(document, [first.ID, second.ID, third.ID], "Distribute Horizontally"); Assert.Equal(40, first.Transform.X);
        LayerAlignment.Apply(document, [first.ID, second.ID, third.ID], "Align Top"); Assert.All(document.Layers, layer => Assert.Equal(first.Transform.Y, layer.Transform.Y));
    }

    [Fact]
    public void GuidesSpanWorkspaceIndependentOfCanvasSize()
    {
        var guide = new CanvasGuide { Axis = GuideAxis.Vertical, Position = 10 };
        Assert.Equal((20d, 0d, 20d, 720d), GuideEdits.WorkspaceLine(guide, 1000, 720, 2, 0, 90));
        guide.Axis = GuideAxis.Horizontal; Assert.Equal((0d, 30d, 1000d, 30d), GuideEdits.WorkspaceLine(guide, 1000, 720, 3, 90, 0));
    }

    [Fact]
    public void RefineExpandsAndContractsGrayscaleCoverage()
    {
        using var document = Document(); var layer = document.Layers[0]; var initial = document.Clone();
        MaskProperties.Refine(document, layer.ID, 1, 0, 3, 0);
        Assert.Equal(255, layer.Mask!.Asset.Image.GetPixel(22, 15).Red);
        document.Adopt(initial); layer = document.Layers[0]; MaskProperties.Refine(document, layer.ID, 1, 0, -3, 0);
        Assert.Equal(0, layer.Mask!.Asset.Image.GetPixel(18, 15).Red); Assert.Equal(255, layer.Mask.Asset.Image.GetPixel(10, 15).Red);
    }

    [Fact]
    public void GroupMaskOnlyMovesWhenItsGroupIsSelected()
    {
        using var document = Document(); var child = document.Layers[0]; child.Mask = null;
        var group = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 80, 60), "Group") { IsGroup = true, Mask = LayerMask.Solid(true) };
        document.Layers.Insert(0, group); child.ParentID = group.ID;
        var groupBox = TransformEdits.GroupBox(document, [group.ID])!.Value;
        TransformEdits.Carry(document, TransformEdits.Originals(document, [group.ID]), groupBox, groupBox with { X = groupBox.X + 10 });
        Assert.Equal(10, group.MaskTransform.X); Assert.Equal(20, child.Transform.X);
        var childBox = child.Transform;
        TransformEdits.Carry(document, TransformEdits.Originals(document, [child.ID]), childBox, childBox with { X = childBox.X + 5 });
        Assert.Equal(10, group.MaskTransform.X); Assert.Equal(25, child.Transform.X);
    }

    [Fact]
    public void SnapshotCountAndMemoryBudgetAreBounded()
    {
        using var document = Document(); var history = new DocumentHistory(100, 1024);
        for (var i = 0; i < 25; i++) history.CreateSnapshot("Snapshot", document, document.Layers[0].ID);
        Assert.Equal(20, history.Snapshots.Count);
        history.Begin("Replace Pixels", document, document.Layers[0].ID);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(40, 30)); pixels.Erase(SKColors.Blue);
        document.Layers[0].Asset = ImportedImage.Create(pixels, "New"); history.End(document, document.Layers[0].ID);
        Assert.True(history.RetainedBytes(document) <= 1024);
    }

    [Fact]
    public void VectorMaskSurvivesImageResizeWithPathCoordinatesRescaled()
    {
        using var document = Document(); var layer = document.Layers[0]; MaskProperties.FromSelection(document, layer.ID, true);
        Assert.True(ImageEdits.Resize(document, 160, 120, 72));
        Assert.NotNull(layer.Mask!.VectorPath);
        using var result = DocumentRenderer.Render(document); Assert.Equal(255, result.GetPixel(40, 40).Alpha); Assert.Equal(0, result.GetPixel(85, 40).Alpha);
    }
}
