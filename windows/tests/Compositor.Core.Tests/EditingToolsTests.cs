using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class EditingToolsTests
{
    private static ImageLayer Layer(SKColor color, int size = 24)
    { var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(size, size)); pixels.Erase(color); return new(Guid.NewGuid(), ImportedImage.Create(pixels, "图层"), new LayerTransform(0, 0, size, size), "图层"); }
    private static CanvasDocument Doc(params ImageLayer[] layers)
    { var doc = new CanvasDocument(Guid.NewGuid(), 32, 32); doc.Layers.AddRange(layers); return doc; }

    [Fact] public void ClipboardSurvivesClosedSourceAndKeepsGroupsMasksStyles()
    {
        var a = Layer(SKColors.Red); var b = Layer(SKColors.Blue); var group = new ImageLayer(Guid.NewGuid(), null, a.Transform, "组") { IsGroup = true };
        a.ParentID = b.ParentID = group.ID; b.MaskSourceID = a.ID;
        var link = Guid.NewGuid(); a.LinkID = b.LinkID = link;
        a.Mask = Compositor.Core.Model.LayerMask.Solid(false); a.Mask!.IsLinked = false; a.Mask.Placement = new LayerTransform(2, 3, 24, 24);
        a.Effects = new LayerEffects { Items = [StyleEffect.Default(StyleEffectKind.Stroke)] };
        var source = Doc(a, b, group); var clipboard = LayerClipboard.Copy(source, [group.ID, a.ID])!; source.Dispose();
        using var destination = Doc(); var first = clipboard.Paste(destination, null); var second = clipboard.Paste(destination, null);
        Assert.Single(first); Assert.Single(second); Assert.Equal(6, destination.Layers.Count);
        var copy = destination.Layers[0]; Assert.NotEqual(a.ID, copy.ID); Assert.Equal(first[0], copy.ParentID);
        Assert.Equal(copy.ID, destination.Layers[1].MaskSourceID); Assert.NotEqual(link, copy.LinkID); Assert.Equal(copy.LinkID, destination.Layers[1].LinkID);
        Assert.False(copy.Mask!.IsLinked); Assert.Equal(2, copy.Mask.Placement!.Value.X); Assert.Single(copy.Effects!.Items!);
        Assert.Equal(SKColors.Red, copy.Asset!.Image.GetPixel(4, 4)); Assert.NotSame(copy.Asset.Image, destination.Layers[3].Asset!.Image);
        ProjectStore.Validate(ProjectSnapshot.FromDocument(destination).Manifest);
    }
    [Fact] public void ClipboardDropsExternalReferencesAndRespectsLockedDestination()
    {
        var a = Layer(SKColors.Red); var b = Layer(SKColors.Blue); b.MaskSourceID = a.ID; b.LinkID = Guid.NewGuid();
        using var source = Doc(a, b); var clipboard = LayerClipboard.Copy(source, [b.ID])!;
        using var target = Doc(); var parent = LayerPlacement.AddFolder(target, null)!.Value; target.Layers[0].Locks = LayerLocks.All;
        Assert.Empty(clipboard.Paste(target, parent)); Assert.Single(target.Layers);
        target.Layers[0].Locks = LayerLocks.None; var ids = clipboard.Paste(target, parent);
        var pasted = target.Layers.Single(l => l.ID == ids[0]); Assert.Null(pasted.MaskSourceID); Assert.Null(pasted.LinkID); Assert.Equal(parent, pasted.ParentID);
    }
    [Fact] public void ClipboardPreservesEditableTextAndSupportsReturnTrip()
    {
        using var a = Doc(); var id = TextEdits.Add(a, new LayerTextStyle { Content = "中文 ABC", FontSize = 12, Vertical = true }, new SKPoint(5, 3))!.Value;
        using var b = Doc(); var clip = LayerClipboard.Copy(a, [id])!; var pasted = clip.Paste(b, null).Single();
        Assert.True(b.Layers[0].LiveText!.Vertical); b.Layers[0].Name = "changed";
        b.Layers[0].LiveText!.Vertical = false;
        var horizontal = ProjectSnapshot.FromDocument(b); Assert.Equal(16, horizontal.Manifest.Version); ProjectStore.Validate(horizontal.Manifest);
        var back = LayerClipboard.Copy(b, [pasted])!.Paste(a, null).Single(); Assert.Equal("changed", a.Layers.Single(l => l.ID == back).Name); Assert.Equal("中文 ABC", a.Layers[0].LiveText!.Content);
    }
    [Theory]
    [InlineData(BrushMode.Pattern)] [InlineData(BrushMode.ArtHistory)] [InlineData(BrushMode.BackgroundErase)] [InlineData(BrushMode.Sharpen)]
    [InlineData(BrushMode.Dodge)] [InlineData(BrushMode.Burn)] [InlineData(BrushMode.Sponge)]
    public void RetouchToolsChangeOnlySelectedPixels(BrushMode mode)
    {
        var layer = Layer(new SKColor(120, 80, 60)); using var doc = Doc(layer); layer.Asset!.Image.SetPixel(12, 12, new SKColor(170, 40, 20));
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(32, 32)); source.Erase(SKColors.Blue);
        using var path = new SKPathBuilder(); path.AddRect(new SKRect(8, 8, 17, 17)); doc.Selection = DocumentSelection.FromPath(path.Detach());
        var history = new DocumentHistory(); history.Begin("retouch", doc, layer.ID);
        Assert.True(BrushEdits.Paint(doc, layer.ID, [new SKPoint(12, 12)], new BrushSettings(Diameter: 30, Mode: mode, Tolerance: 255, History: source, Strength: 1)));
        history.End(doc, layer.ID); Assert.NotEqual(new SKColor(170, 40, 20), layer.Asset!.Image.GetPixel(12, 12));
        Assert.Equal(new SKColor(120, 80, 60), layer.Asset.Image.GetPixel(4, 4)); Assert.True(history.CanUndo);
        Assert.Equal(new SKColor(170, 40, 20), history.Undo()!.Value.Document!.Layers[0].Asset!.Image.GetPixel(12, 12));
    }
    [Fact] public void BackgroundEraserProtectsForegroundAndPatternHandlesNegativeCoordinates()
    {
        var layer = Layer(SKColors.Red); using var doc = Doc(layer);
        Assert.False(BrushEdits.Paint(doc, layer.ID, [new SKPoint(10, 10)], new BrushSettings(Mode: BrushMode.BackgroundErase, Red: 1, ProtectForeground: true)));
        layer.Transform = layer.Transform with { X = -10, Y = -10 };
        using var tile = Bitmaps.Allocate(Bitmaps.ColorInfo(2, 2)); tile.Erase(SKColors.Blue);
        Assert.True(BrushEdits.Paint(doc, layer.ID, [new SKPoint(0, 0)], new BrushSettings(Mode: BrushMode.Pattern, Pattern: tile)));
        Assert.Equal(SKColors.Blue, layer.Asset!.Image.GetPixel(10, 10));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void BucketPreservesSelectionAndContiguousRegions(bool erase)
    {
        var layer = Layer(SKColors.Red); using var doc = Doc(layer);
        for (var y = 0; y < 24; y++) layer.Asset!.Image.SetPixel(12, y, SKColors.Blue);
        using var path = new SKPathBuilder(); path.AddRect(new SKRect(2, 2, 20, 20)); var selection = DocumentSelection.FromPath(path.Detach()); doc.Selection = selection;
        Assert.True(BucketEdits.Apply(doc, layer.ID, new SKPoint(5, 5), new WandOptions { Tolerance = 0, Contiguous = true }, SKColors.Green, erase));
        Assert.Same(selection, doc.Selection); Assert.Equal(SKColors.Red, layer.Asset!.Image.GetPixel(18, 5)); Assert.Equal(SKColors.Red, layer.Asset.Image.GetPixel(0, 0));
        Assert.Equal(erase ? (byte)0 : (byte)255, layer.Asset.Image.GetPixel(5, 5).Alpha);
        if (!erase) Assert.Equal(SKColors.Green, layer.Asset.Image.GetPixel(5, 5));
    }
    [Fact] public void WarpSplitPreservesGeometryAndRenderHasNoSeams()
    {
        var mesh = new PlacementMesh(new LayerTransform(3, 4, 24, 24)); var before = mesh.Map(.25f, .7f);
        Assert.True(mesh.Split(true, .4f)); Assert.True(mesh.Split(false, .7f)); Assert.Equal(before.X, mesh.Map(.25f, .7f).X, 3); Assert.Equal(before.Y, mesh.Map(.25f, .7f).Y, 3);
        using var image = Bitmaps.Allocate(Bitmaps.ColorInfo(24, 24)); image.Erase(SKColors.Red);
        using var warped = mesh.Render(image)!.Image;
        for (var y = 1; y < 23; y++) for (var x = 1; x < 23; x++) Assert.Equal(SKColors.Red, warped.GetPixel(x, y));
        mesh.Points[4] += new SKPoint(6, 2); using var changed = mesh.Render(image)!.Image;
        Assert.True(mesh.RemoveSplit(1, 1)); Assert.Equal(4, mesh.Points.Count);
    }
    [Fact] public void VerticalTextAndOpenPenPathRequireVersion16AndRoundTrip()
    {
        using var doc = Doc(); var text = new LayerTextStyle { Content = "中文测试", FontSize = 16, Vertical = true };
        var id = TextEdits.Add(doc, text, new SKPoint(0, 0))!.Value;
        Assert.True(doc.Layers[0].Transform.Height > doc.Layers[0].Transform.Width);
        var session = TextSession.Editing(doc.Layers[0]); Assert.True(session.Type(doc, "五")); Assert.True(doc.Layers[0].LiveText!.Vertical);
        ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Path, Path = "M.1,.1 C.3,.1 .6,.8 .9,.9", LineWidth = 2, Red = 1 }, new SKRectI(0, 0, 20, 20), id);
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(16, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        var json = ManifestJson.Serialize(snapshot.Manifest); var parsed = ManifestJson.Deserialize(json); Assert.True(parsed.Layers[0].Text!.Vertical);
        parsed.Version = 15; Assert.Throws<ProjectException>(() => ProjectStore.Validate(parsed));
    }
    [Fact] public void AddingAnchorPreservesCubicGeometryIncludingClosingSegment()
    {
        var nodes = PathNodes.Parse("M0,0 C0,100 100,100 100,0 L0,0 Z")!;
        Assert.True(nodes.InsertNearest(new SKPoint(50, 75))); Assert.Equal(4, nodes.NodeCount);
        Assert.InRange(nodes.Subpaths[0].Nodes[1].Point.Y, 74.9f, 75.1f);
        Assert.NotNull(nodes.Subpaths[0].Nodes[1].In); Assert.NotNull(nodes.Subpaths[0].Nodes[1].Out);
        using var before = SKPath.ParseSvgPathData("M0,0 C0,100 100,100 100,0 L0,0 Z"); using var after = SKPath.ParseSvgPathData(nodes.ToSvg());
        Assert.Equal(before.TightBounds, after.TightBounds);
    }
    [Fact] public void MeshPreservesPreexistingFlips()
    {
        using var image = Bitmaps.Allocate(Bitmaps.ColorInfo(12, 8)); image.Erase(SKColors.Red);
        for (var y = 0; y < 8; y++) for (var x = 6; x < 12; x++) image.SetPixel(x, y, SKColors.Blue);
        var mesh = new PlacementMesh(new LayerTransform(0, 0, 12, 8, FlipX: true)); using var result = mesh.Render(image)!.Image;
        Assert.Equal(SKColors.Blue, result.GetPixel(2, 4)); Assert.Equal(SKColors.Red, result.GetPixel(10, 4));
    }
    [Fact] public void EditingOpenPathKeepsStrokeMarginsAndAcceptsHorizontalSegments()
    {
        using var doc = Doc();
        var id = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Path, Path = "M.1,.5 L.9,.5", LineWidth = 4, Red = 1 }, new SKRectI(0, 0, 20, 20), null)!.Value;
        Assert.True(ShapeEdits.SetPath(doc, id, "M.1,.5 L.9,.5"));
        var layer = doc.Layers[0]; Assert.Equal(20, layer.Transform.Width, 3); Assert.Equal(4, layer.Transform.Height, 3);
        Assert.Equal(0, layer.Transform.X, 3); Assert.Equal(8, layer.Transform.Y, 3);
        using var path = SKPath.ParseSvgPathData(layer.LiveShape!.Path!);
        Assert.InRange(path.TightBounds.Left, .099f, .101f); Assert.InRange(path.TightBounds.Right, .899f, .901f);
        Assert.True(layer.Asset!.Image.GetPixel(10, 2).Alpha > 240);
    }
}
