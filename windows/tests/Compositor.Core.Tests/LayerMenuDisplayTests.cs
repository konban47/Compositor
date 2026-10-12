using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using Xunit;
using LayerMask = Compositor.Core.Model.LayerMask;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;
public sealed class LayerMenuDisplayTests
{
    [Theory]
    [InlineData(SKPathOp.Union, true, true, true)]
    [InlineData(SKPathOp.Difference, true, false, false)]
    [InlineData(SKPathOp.Intersect, false, true, false)]
    [InlineData(SKPathOp.Xor, true, false, true)]
    public void ShapeBooleanOperationsPreserveEditableGeometry(SKPathOp operation, bool left, bool middle, bool right)
    {
        using var doc = Doc();
        var first = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle, Red = 1 }, SKRectI.Create(0, 0, 20, 20), null)!.Value;
        var second = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle, Blue = 1 }, SKRectI.Create(10, 0, 20, 20), first)!.Value;
        Assert.True(LayerMenuEdits.CanCombineShapes(doc, [first, second]));
        Assert.NotNull(LayerMenuEdits.CombineShapes(doc, [first, second], operation));
        Assert.Single(doc.Layers); Assert.Equal(ShapeKind.Path, doc.Layers[0].LiveShape!.Kind);
        using var pixels = DocumentRenderer.Render(doc);
        Assert.Equal(left, pixels.GetPixel(5, 10).Alpha > 0);
        Assert.Equal(middle, pixels.GetPixel(15, 10).Alpha > 0);
        Assert.Equal(right, pixels.GetPixel(25, 10).Alpha > 0);
    }
    [Fact]
    public void ShapeCombinationDoesNotDropMasks()
    {
        using var doc = Doc();
        var a = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle }, SKRectI.Create(20, 20), null)!.Value;
        var b = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle }, SKRectI.Create(20, 20), a)!.Value;
        doc.Layers[0].Mask = WhiteMask();
        Assert.False(LayerMenuEdits.CanCombineShapes(doc, [a, b]));
        Assert.Null(LayerMenuEdits.CombineShapes(doc, [a, b], SKPathOp.Union)); Assert.Equal(2, doc.Layers.Count);
    }
    [Theory, InlineData(16), InlineData(32)]
    public void ApplyingMaskRetainsDeepPixels(int bits)
    {
        var deep = Bitmaps.Allocate(DeepPixels.Info(1, 1));
        var input = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan());
        input[0] = bits == 32 ? 2.75f : .654321f; input[1] = .123456f; input[2] = .987654f; input[3] = .712345f;
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.CreateDeep(deep, "Deep", bits), new LayerTransform(0, 0, 1, 1), "Deep");
        using var doc = Doc(layer); layer.Mask = WhiteMask(); layer.Mask.Density = .5;
        var before = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(layer.Asset!.WorkingImage.GetPixelSpan()).ToArray();
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(1, 1)); mask.Erase(SKColors.Black); layer.Mask = LayerMask.AssetFrom(mask); layer.Mask.Density = .5;
        Assert.True(MaskProperties.Apply(doc, layer.ID)); Assert.Equal(bits, layer.Asset.BitDepth); Assert.NotNull(layer.Asset.Deep);
        var after = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(layer.Asset.WorkingImage.GetPixelSpan());
        for (var i = 0; i < 3; i++) Assert.InRange(Math.Abs(after[i] - before[i]), 0, .00002f);
        Assert.InRange(Math.Abs(after[3] - before[3] * 128 / 255), 0, .00002f);
    }
    [Fact]
    public void SmartObjectUnpackKeepsBackgroundOpacityClippingAndUndo()
    {
        using var source = new CanvasDocument(Guid.NewGuid(), 24, 20);
        source.Image = source.Image with { Background = (uint)SKColors.White };
        source.Layers.Add(Solid(SKColors.Red, -5, 0, 20, 20)); source.Layers.Add(Solid(SKColors.Blue, 10, 0, 20, 20));
        using var render = DocumentRenderer.Render(source);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(render.Copy(), "Smart"), new LayerTransform(5, 5, 24, 20), "Smart")
            { SmartObject = SmartObjectData.FromDocument(source), Opacity = .5 };
        using var doc = Doc(layer); var history = new DocumentHistory();
        using var before = DocumentRenderer.Render(doc); history.Begin("Convert to Layers", doc, layer.ID);
        Assert.True(LayerMenuEdits.UnpackSmartObject(doc, layer.ID)); history.End(doc, layer.ID);
        Assert.True(layer.IsGroup); Assert.Null(layer.SmartObject); Assert.Equal(3, doc.Layers.Count(l => l.ParentID == layer.ID));
        using var after = DocumentRenderer.Render(doc);
        for (var y = 0; y < doc.Height; y++) for (var x = 0; x < doc.Width; x++) Assert.Equal(before.GetPixel(x, y), after.GetPixel(x, y));
        doc.Adopt(history.Undo()!.Value.Document!); Assert.Single(doc.Layers); Assert.NotNull(doc.Layers[0].SmartObject);
    }
    private static ImageLayer Solid(SKColor color, int x = 0, int y = 0, int w = 24, int h = 20)
    { var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(w, h)); pixels.Erase(color); return new(Guid.NewGuid(), ImportedImage.Create(pixels, "Layer"), new LayerTransform(x, y, w, h), "Layer"); }
    private static CanvasDocument Doc(params ImageLayer[] layers)
    { var doc = new CanvasDocument(Guid.NewGuid(), 64, 48); doc.Layers.AddRange(layers); return doc; }
    private static LayerMask WhiteMask() { var bitmap=Bitmaps.Allocate(Bitmaps.MaskInfo(1,1)); bitmap.Erase(SKColors.White); return LayerMask.AssetFrom(bitmap); }
    public static IEnumerable<object[]> Blends => Enum.GetValues<LayerBlendMode>().Select(b => new object[] { b });
    [Theory, MemberData(nameof(Blends))]
    public void CachedSceneMatchesReferenceBlend(LayerBlendMode mode)
    {
        var a = Solid(new SKColor(90, 130, 60), 0, 0, 64, 48); var b = Solid(new SKColor(180, 65, 120, 140), 12, 7); b.BlendMode = mode; b.Opacity = .7;
        using var doc = Doc(a, b); using var cache = new DisplaySceneCache(); using var scene = cache.Build(doc);
        if (scene is null) { Assert.False(DisplaySceneCache.Supports(doc)); return; }
        using var reference = DocumentRenderer.Render(doc); using var target = Bitmaps.Allocate(Bitmaps.ColorInfo(64, 48)); target.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(target)) scene.Draw(canvas);
        var before = reference.GetPixelSpan(); var after = target.GetPixelSpan();
        for (var i = 0; i < before.Length; i++) Assert.InRange(Math.Abs(before[i] - after[i]), 0, 2);
    }
    [Fact] public void SceneRetainsAssetsAcrossCacheRebuildAndMasksAncestors()
    {
        var layer = Solid(SKColors.Red); var group = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 64, 48), "Group") { IsGroup = true, Opacity = .5 };
        layer.ParentID = group.ID; layer.Mask = WhiteMask(); group.Mask = WhiteMask();
        using var doc = Doc(group, layer); using var cache = new DisplaySceneCache(); using var first = cache.Build(doc); Assert.NotNull(first);
        layer.Asset = Solid(SKColors.Blue).Asset; using var second = cache.Build(doc); Assert.NotNull(second); cache.Dispose();
        using var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(64, 48)); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent); first.Draw(canvas);
        Assert.InRange(bitmap.GetPixel(10, 10).Red, (byte)253, (byte)255); Assert.InRange(bitmap.GetPixel(10, 10).Alpha, (byte)126, (byte)129);
        canvas.Clear(SKColors.Transparent); second.Draw(canvas); Assert.Equal(255, bitmap.GetPixel(10, 10).Blue);
    }
    [Fact] public void SceneFallsBackForAdjustmentEffectsDeepDataAndFeather()
    {
        var layer = Solid(SKColors.Blue); using var doc = Doc(layer); using var cache = new DisplaySceneCache();
        layer.Effects = new LayerEffects(); Assert.Null(cache.Build(doc)); layer.Effects = null;
        layer.Mask = WhiteMask(); layer.Mask.Feather = 1; Assert.Null(cache.Build(doc)); layer.Mask = null;
        layer.Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Invert }; Assert.Null(cache.Build(doc));
    }
    [Fact] public void ArrangePreservesSelectedOrderParentsAndLocks()
    {
        var a = Solid(SKColors.Red); var b = Solid(SKColors.Green); var c = Solid(SKColors.Blue); var d = Solid(SKColors.White); using var doc = Doc(a,b,c,d);
        Assert.True(LayerMenuEdits.Arrange(doc, [a.ID,c.ID], "Forward")); Assert.Equal(new[] { b.ID,a.ID,d.ID,c.ID },doc.Layers.Select(l => l.ID));
        Assert.True(LayerMenuEdits.Arrange(doc, [a.ID,c.ID], "Back")); Assert.Equal(new[] { a.ID,c.ID,b.ID,d.ID },doc.Layers.Select(l => l.ID));
        c.Locks = LayerLocks.Position; Assert.False(LayerMenuEdits.Arrange(doc,[c.ID],"Front"));
    }
    [Fact] public void TransparencyMaskPreservesAppearanceAndCanBeApplied()
    {
        var layer = Solid(new SKColor(220,80,30,128)); using var doc = Doc(layer); using var before = DocumentRenderer.Render(doc);
        Assert.True(LayerMenuEdits.TransparencyMask(doc,layer.ID)); Assert.Equal(255,layer.Asset!.Image.GetPixel(0,0).Alpha);
        using var after = DocumentRenderer.Render(doc); Assert.Equal(before.GetPixel(10,10),after.GetPixel(10,10));
        Assert.True(MaskProperties.Apply(doc,layer.ID)); Assert.Null(layer.Mask); Assert.InRange(layer.Asset.Image.GetPixel(0,0).Alpha,(byte)127,(byte)128);
    }
    [Fact] public void DefringeCopiesOpaqueNeighborsWithoutChangingAlpha()
    {
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(3,1)); bitmap.SetPixel(0,0,SKColors.Red); bitmap.SetPixel(1,0,new SKColor(0,200,0,128)); bitmap.SetPixel(2,0,SKColors.Transparent);
        var layer = new ImageLayer(Guid.NewGuid(),ImportedImage.Create(bitmap,"edge"),new LayerTransform(0,0,3,1),"edge"); using var doc=Doc(layer);
        Assert.True(LayerMenuEdits.Defringe(doc,layer.ID,1)); var c=layer.Asset!.Image.GetPixel(1,0); Assert.Equal(255,c.Red); Assert.Equal(0,c.Green); Assert.Equal(128,c.Alpha);
        Assert.False(LayerMenuEdits.Defringe(doc,layer.ID,0));
    }
    [Theory]
    [InlineData(ImageAdjustmentKind.BrightnessContrast)] [InlineData(ImageAdjustmentKind.Vibrance)]
    [InlineData(ImageAdjustmentKind.PhotoFilter)] [InlineData(ImageAdjustmentKind.ChannelMixer)]
    [InlineData(ImageAdjustmentKind.ColorLookup)] [InlineData(ImageAdjustmentKind.Posterize)]
    [InlineData(ImageAdjustmentKind.Threshold)] [InlineData(ImageAdjustmentKind.SelectiveColor)]
    public void ExtendedAdjustmentsRoundTripAndRender(ImageAdjustmentKind kind)
    {
        using var doc = Doc(Solid(new SKColor(80,130,200))); var settings = new ImageAdjustmentSettings { Kind=kind, Values = new() { ["Brightness"]=25,["Saturation"]=30,["Density"]=40,["Levels"]=3,["Threshold"]=127 } };
        var id=LayerPlacement.AddAdjustment(doc,AdjustmentKind.Extended,doc.Layers[0].ID); doc.Layers.Single(l=>l.ID==id).Adjustment=new LayerAdjustment{Kind=AdjustmentKind.Extended,Extended=settings};
        var snapshot=ProjectSnapshot.FromDocument(doc); Assert.Equal(19,snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        var parsed=ManifestJson.Deserialize(ManifestJson.Serialize(snapshot.Manifest)); Assert.Equal(kind,parsed.Layers.Last().Adjustment!.Extended!.Kind);
        using var rendered=DocumentRenderer.Render(doc); Assert.True(rendered.GetPixel(4,4).Alpha>0);
        snapshot.Manifest.Version=18; Assert.ThrowsAny<Exception>(()=>ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact] public void ExtendedRejectsMalformedLutsAndGlobalStatisticsOperators()
    {
        var adjustment=new LayerAdjustment{Kind=AdjustmentKind.Extended,Extended=new(){Kind=ImageAdjustmentKind.Equalize}}; Assert.False(adjustment.IsValid);
        adjustment.Extended=new(){Kind=ImageAdjustmentKind.ColorLookup,Cube=new ColorCube{Size=65,Values=[]}}; Assert.False(adjustment.IsValid);
        adjustment.Extended=new(){Values=null!}; Assert.False(adjustment.IsValid);
    }
}
