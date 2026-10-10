using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class LayerStyleWorkflowTests
{
    private static ImageLayer Solid(SKColor color, int x = 16, int y = 16, int w = 32, int h = 32)
    { var image = Bitmaps.Allocate(Bitmaps.ColorInfo(w, h)); image.Erase(color); return new(Guid.NewGuid(), ImportedImage.Create(image, "图层"), new LayerTransform(x, y, w, h), "图层"); }
    private static CanvasDocument Document(params ImageLayer[] layers)
    { var doc = new CanvasDocument(Guid.NewGuid(), 64, 64); doc.Layers.AddRange(layers); return doc; }
    public static IEnumerable<object[]> Kinds => Enum.GetValues<StyleEffectKind>().Select(k => new object[] { k });
    [Theory, MemberData(nameof(Kinds))]
    public void EveryEffectChangesPixelsAndRoundTrips(StyleEffectKind kind)
    {
        var layer = Solid(new SKColor(100, 130, 160)); using var doc = Document(layer);
        using var before = DocumentRenderer.Render(doc); var effect = StyleEffect.Default(kind); effect.Red = 1; effect.Green = .15; effect.Blue = .05; effect.Size = 6;
        layer.Effects = new LayerEffects { Items = [effect] };
        using var after = DocumentRenderer.Render(doc); Assert.False(before.GetPixelSpan().SequenceEqual(after.GetPixelSpan()));
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(15, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        var parsed = ManifestJson.Deserialize(ManifestJson.Serialize(snapshot.Manifest)); Assert.Equal(kind, parsed.Layers[0].Effects!.Items!.Single().Kind);
    }
    [Fact]
    public void MultipleEffectsPreservePremultipliedEdgesAndFillZero()
    {
        var layer = Solid(new SKColor(80, 120, 160, 128)); using var doc = Document(layer); layer.FillOpacity = 0;
        layer.Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.ColorOverlay, Red = 1, Opacity = .5 }, new() { Kind = StyleEffectKind.DropShadow, Size = 0, Distance = 8, Angle = 0, Blue = 1 }] };
        using var image = DocumentRenderer.Render(doc); var middle = image.GetPixel(32, 32);
        Assert.InRange(middle.Red, (byte)101, (byte)103); Assert.InRange(middle.Blue, (byte)151, (byte)154); Assert.InRange(middle.Alpha, (byte)159, (byte)161);
        layer.Effects = layer.Effects.Scaled(1); layer.Effects.Enabled = false;
        using var hidden = DocumentRenderer.Render(doc); Assert.Equal(0, hidden.GetPixel(32, 32).Alpha);
    }
    [Fact]
    public void ChannelRestrictionPreservesExcludedBackdropColor()
    {
        var top = Solid(new SKColor(240, 200, 20)); top.Blending = new LayerBlending { Red = false };
        using var doc = Document(Solid(new SKColor(80, 10, 70), 0, 0, 64, 64), top); using var image = DocumentRenderer.Render(doc);
        Assert.Equal(new SKColor(80, 200, 20), image.GetPixel(32, 32));
    }
    [Fact]
    public void BlendIfSplitsFadeInsteadOfHardClipping()
    {
        var range = new BlendRange { Black = 20, BlackSplit = 80, WhiteSplit = 200, White = 240 };
        Assert.Equal(0, range.Coverage(10)); Assert.Equal(.5, range.Coverage(50), 5); Assert.Equal(.5, range.Coverage(220), 5);
        var top = Solid(new SKColor(50, 0, 0)); top.Blending = new LayerBlending { Ranges = [new() { Channel = BlendIfChannel.Red, Source = range }] };
        using var doc = Document(top); using var image = DocumentRenderer.Render(doc); Assert.InRange(image.GetPixel(32, 32).Alpha, (byte)126, (byte)129);
    }
    [Fact]
    public void InteriorEffectsCanBlendSeparatelyFromLayer()
    {
        var top = Solid(SKColors.Blue); top.BlendMode = LayerBlendMode.Multiply;
        top.Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.ColorOverlay, Red = 1 }] }; top.Blending = new();
        using var doc = Document(Solid(SKColors.Green, 0, 0, 64, 64), top);
        using var separate = DocumentRenderer.Render(doc); Assert.Equal(SKColors.Red, separate.GetPixel(32, 32));
        top.Blending = new LayerBlending { BlendInteriorEffectsAsGroup = true }; using var grouped = DocumentRenderer.Render(doc);
        Assert.Equal(SKColors.Black, grouped.GetPixel(32, 32));
    }
    [Fact]
    public void LayerMaskCanHideEffectsAfterTheyAreDrawn()
    {
        var top = Solid(SKColors.Red); top.Mask = Compositor.Core.Model.LayerMask.AssetFrom(Bitmaps.SolidMask(true));
        top.Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.Stroke, Size = 4, Green = 1 }] };
        using var doc = Document(top); using var extends = DocumentRenderer.Render(doc); Assert.True(extends.GetPixel(13, 30).Alpha > 0);
        top.Blending = new LayerBlending { LayerMaskHidesEffects = true }; using var clipped = DocumentRenderer.Render(doc); Assert.Equal(0, clipped.GetPixel(13, 30).Alpha);
    }
    [Theory]
    [InlineData(KnockoutKind.Shallow, false)]
    [InlineData(KnockoutKind.Deep, true)]
    public void KnockoutsRespectGroupDepth(KnockoutKind kind, bool transparent)
    {
        var group = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 64, 64), "组") { IsGroup = true };
        var fill = Solid(SKColors.Red, 0, 0, 64, 64); fill.ParentID = group.ID;
        var hole = Solid(SKColors.White); hole.ParentID = group.ID; hole.FillOpacity = 0; hole.Blending = new LayerBlending { Knockout = kind };
        using var doc = Document(Solid(SKColors.Blue, 0, 0, 64, 64), group, fill, hole); using var image = DocumentRenderer.Render(doc);
        if (transparent) Assert.Equal(0, image.GetPixel(32, 32).Alpha); else Assert.Equal(SKColors.Blue, image.GetPixel(32, 32));
        Assert.Equal(SKColors.Red, image.GetPixel(5, 5));
    }
    [Fact]
    public void FrameClipsChildrenAndArtboardOwnsAnEmptyTransform()
    {
        var image = Solid(SKColors.Red, 0, 0, 64, 64); using var doc = Document(image);
        var frame = LayerWorkflow.MakeContainer(doc, [image.ID], LayerContainer.Frame, "画框", SKRect.Create(16, 16, 32, 32));
        using var render = DocumentRenderer.Render(doc); Assert.Equal(0, render.GetPixel(5, 5).Alpha); Assert.Equal(SKColors.Red, render.GetPixel(30, 30));
        Assert.Equal(32, TransformEdits.GroupBox(doc, [frame!.Value])!.Value.Width);
        var board = LayerWorkflow.MakeContainer(doc, [], LayerContainer.Artboard, "画板", SKRect.Create(4, 4, 8, 8));
        var originals = TransformEdits.Originals(doc, [board!.Value]); Assert.Contains(board.Value, originals.Keys);
        var box = TransformEdits.GroupBox(doc, [board.Value])!.Value; Assert.True(TransformEdits.Carry(doc, originals, box, box with { X = 0 }));
        using var shown = DocumentRenderer.Render(doc); Assert.Equal(SKColors.White, shown.GetPixel(1, 5));
    }
    [Fact]
    public void SmartObjectPreservesEditableSourceSaveReloadAndUpdate()
    {
        var shape = new LayerShapeStyle { Kind = ShapeKind.Rectangle, Red = 1 };
        using var doc = Document(); var pixels = ShapeEdits.Image(shape, 32, 32)!;
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "形状"), new LayerTransform(12, 14, 32, 32), "形状") { Shape = new LayerShape(shape, pixels) }; doc.Layers.Add(layer);
        var id = LayerWorkflow.ConvertToSmartObject(doc, [layer.ID]); Assert.NotNull(id); var smart = doc.Layers.Single(); Assert.NotNull(smart.SmartObject);
        using var embedded = smart.SmartObject!.Open(); var source = embedded.ToDocument(); Assert.NotNull(source.Layers[0].LiveShape); Assert.Equal(0, source.Layers[0].Transform.X);
        source.Layers[0].Asset!.Image.Erase(SKColors.Blue);
        var history = new DocumentHistory(); history.Begin("Update Smart Object", doc, smart.ID);
        Assert.True(LayerWorkflow.UpdateSmartObject(doc, smart.SmartObject.ID, source)); history.End(doc, smart.ID);
        Assert.Equal(SKColors.Blue, smart.Asset!.Image.GetPixel(10, 10)); Assert.True(history.CanUndo);
        var path = Path.Combine(Path.GetTempPath(), "compositor-style-" + Guid.NewGuid() + ".comp");
        try { ProjectStore.Save(ProjectSnapshot.FromDocument(doc), path); using var loaded = ProjectStore.Load(path); Assert.NotNull(loaded.RuntimeLayers().Single().SmartObject); using var contents = loaded.RuntimeLayers().Single().SmartObject!.Open(); Assert.Equal(32, contents.Manifest.Width); }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public void MaskAllObjectsCreatesSeparateMasksAndPreservesSource()
    {
        var layer = Solid(SKColors.Red, 0, 0, 64, 64); using var doc = Document(layer);
        using var matte = Bitmaps.Allocate(Bitmaps.MaskInfo(64, 64)); matte.Erase(SKColors.Black);
        for (var y = 4; y < 14; y++) for (var x = 4; x < 14; x++) { matte.SetPixel(x, y, SKColors.White); matte.SetPixel(x + 30, y + 30, SKColors.White); }
        var groups = ObjectMaskGroups.Create(doc, layer.ID, matte); Assert.Equal(2, groups.Count); Assert.Same(layer, doc.Layers[0]);
        var group = doc.Layers.Single(l => l.ID == groups[0]); Assert.NotNull(group.Mask);
        var adjustment = new ImageLayer(Guid.NewGuid(), null, layer.Transform, "反相") { ParentID = group.ID, Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Invert } }; doc.Layers.Add(adjustment);
        using var rendered = DocumentRenderer.Render(doc); Assert.Equal(SKColors.Cyan, rendered.GetPixel(8, 8)); Assert.Equal(SKColors.Red, rendered.GetPixel(20, 20));
    }
    [Fact]
    public void MergeVisibleRetainsHiddenLayersAndFlattenIsOpaque()
    {
        var visible = Solid(SKColors.Red); var hidden = Solid(SKColors.Blue); hidden.IsVisible = false;
        using var doc = Document(visible, hidden); var made = LayerWorkflow.MergeVisible(doc, false); Assert.NotNull(made); Assert.Contains(hidden, doc.Layers);
        LayerWorkflow.MergeVisible(doc, true); Assert.Single(doc.Layers); using var image = DocumentRenderer.Render(doc); Assert.Equal(SKColors.White, image.GetPixel(0, 0));
    }
    [Fact]
    public void NewStyleVersionCannotBeSilentlySavedAsLegacy()
    {
        var layer = Solid(SKColors.Red); layer.Blending = new LayerBlending { Green = false }; using var doc = Document(layer);
        var manifest = ProjectSnapshot.FromDocument(doc).Manifest; manifest.Version = 14;
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(manifest));
        manifest.Version = 15; layer.Blending.Ranges.Add(new BlendIfRange { Source = new BlendRange { Black = 200, BlackSplit = 0 } });
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(ProjectSnapshot.FromDocument(doc).Manifest));
    }
    [Fact]
    public void InteriorOverlayPreservesTranslucentCoverage()
    {
        var layer = Solid(new SKColor(80, 120, 160, 128)); layer.Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.ColorOverlay, Red = 1, Opacity = .5 }] };
        using var doc = Document(layer); using var rendered = DocumentRenderer.Render(doc);
        var pixel = rendered.GetPixel(32, 32); Assert.Equal(128, pixel.Alpha); Assert.InRange(pixel.Red, (byte)166, (byte)170);
    }
    [Fact]
    public void SmartConversionRetainsEffectOutsideOriginalBounds()
    {
        var layer = Solid(SKColors.Red); layer.Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.Stroke, Green = 1, Size = 5 }] };
        using var doc = Document(layer); using var before = DocumentRenderer.Render(doc);
        LayerWorkflow.ConvertToSmartObject(doc, [layer.ID]); using var after = DocumentRenderer.Render(doc);
        Assert.Equal(before.GetPixel(12, 30), after.GetPixel(12, 30)); Assert.True(after.GetPixel(12, 30).Alpha > 0);
    }
    [Fact]
    public void GroupEffectsRenderTheSameInTiles()
    {
        var layer = Solid(SKColors.Red); var group = new ImageLayer(Guid.NewGuid(), null, layer.Transform, "Group") { IsGroup = true, Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.OuterGlow, Green = 1, Size = 5 }] } };
        layer.ParentID = group.ID; using var doc = Document(group, layer);
        foreach (var kind in new[] { StyleEffectKind.OuterGlow, StyleEffectKind.GradientOverlay, StyleEffectKind.PatternOverlay })
        {
            group.Effects = new LayerEffects { Items = [new() { Kind = kind, Green = 1, Size = 5 }] };
            using var full = DocumentRenderer.Render(doc); using var region = DocumentRenderer.RenderRegion(doc, new SKRectI(8, 8, 24, 24));
            for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) Assert.Equal(full.GetPixel(x + 8, y + 8), region.GetPixel(x, y));
        }
    }
    [Fact]
    public void SmartHistoryCountsEmbeddedPackagesAndRestoresSource()
    {
        using var doc = Document(Solid(SKColors.Red)); LayerWorkflow.ConvertToSmartObject(doc, [doc.Layers[0].ID]);
        var smart = doc.Layers[0]; var original = smart.SmartObject!; var history = new DocumentHistory();
        history.Begin("Rasterize Layer", doc, smart.ID); LayerWorkflow.Rasterize(doc, [smart.ID]); history.End(doc, smart.ID);
        Assert.True(history.RetainedBytes(doc) >= original.Package.Length); Assert.Null(smart.SmartObject);
        var state = history.Undo(); Assert.Same(original, state!.Value.Document!.Layers[0].SmartObject);
    }
    [Fact]
    public void InvalidPatternAndSplitRangesAreRejected()
    {
        var effect = new StyleEffect { Kind = StyleEffectKind.PatternOverlay, Pattern = StylePattern.Image, PatternPng = "bad png" };
        Assert.False(effect.IsValid); Assert.False(new BlendRange { White = 256 }.IsValid);
        using var pattern = Bitmaps.Allocate(Bitmaps.ColorInfo(1, 1)); pattern.Erase(SKColors.Magenta);
        var layer = Solid(SKColors.White); layer.Effects = new LayerEffects { Items = [new() { Kind = StyleEffectKind.PatternOverlay, Pattern = StylePattern.Checkerboard, PatternPng = Convert.ToBase64String(PngCodec.Encode(pattern)), Red = 1, Green2 = 1, Blue2 = 0 }] };
        using var doc = Document(layer); using var image = DocumentRenderer.Render(doc); Assert.NotEqual(SKColors.Magenta, image.GetPixel(32, 32));
    }
    [Fact]
    public void SimpleSvgHasVectorGeometryAndEscapedNames()
    {
        var style = new LayerShapeStyle { Kind = ShapeKind.Rectangle, Red = 1 }; var image = ShapeEdits.Image(style, 32, 32)!;
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Vector"), new LayerTransform(0, 0, 32, 32), "<script>") { Shape = new LayerShape(style, image) };
        using var doc = Document(layer); var xml = LayerWorkflow.Svg(doc, [layer.ID]); Assert.Contains("<rect", xml); Assert.Contains("&lt;script&gt;", xml); Assert.DoesNotContain("data:image", xml);
    }

}
