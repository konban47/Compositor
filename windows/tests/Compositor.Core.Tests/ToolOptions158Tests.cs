using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Core.Tests;

public class ToolOptions158Tests : ProjectTestBase
{
    private static CanvasDocument Document(SKColor color, int size = 64)
    {
        var doc = new CanvasDocument(Guid.NewGuid(), size, size);
        doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Picture(size, size, color), "Source"), new(0, 0, size, size), "Source")); return doc;
    }
    [Fact] public void EditableGradientRoundTripsAndRequiresVersion28()
    {
        using var doc = Document(SKColors.Transparent); var layer = doc.Layers[0];
        var gradient = new LiveGradientSettings(0, 0, 64, 0, new() { Shape = GradientShape.Reflected, Method = GradientMethod.Linear, Dither = false });
        Assert.True(GradientPaint.SetLive(doc, layer, gradient)); var snapshot = ProjectSnapshot.FromDocument(doc);
        Assert.Equal(28, snapshot.Manifest.Version); ProjectStore.Save(snapshot, PathIn("live.comp"));
        using var loaded = ProjectStore.Load(PathIn("live.comp")); using var restored = loaded.ToDocument();
        Assert.Equal(gradient.Paint.Shape, restored.Layers[0].LiveGradient!.Paint.Shape);
        Assert.Equal(layer.Asset!.Image.GetPixel(25, 12), restored.Layers[0].Asset!.Image.GetPixel(25, 12));
        snapshot.Manifest.Version = 27; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact] public void GradientMethodsAndShapesAreDistinctAndReverseIsSymmetric()
    {
        var settings = new GradientPaintSettings { Dither = false };
        var colors = Enum.GetValues<GradientMethod>().Select(m => (settings with { Method = m }).Sample(.25).Red).ToArray();
        Assert.True(colors.Distinct().Count() >= 4);
        Assert.Equal(settings.Sample(.25), (settings with { Reverse = true }).Sample(.75));
        var a = new SKPoint(0, 0); var b = new SKPoint(10, 0);
        Assert.Equal(.5, GradientEdits.Parameter(a, b, new(-5, 0), GradientShape.Reflected), 3);
        Assert.Equal(1, GradientEdits.Parameter(a, b, new(5, 5), GradientShape.Diamond), 3);
        Assert.InRange(GradientEdits.Parameter(a, b, new(0, 10), GradientShape.Angle), .24, .76);
    }
    [Fact] public void GradientPresetValidationRejectsNullAndUnsortedStops()
    {
        var path = PathIn("gradients.json"); File.WriteAllText(path, "[null]"); Assert.Throws<InvalidDataException>(() => GradientLibrary.Load(path));
        var invalid = new GradientPreset(Guid.NewGuid(), "Bad", "Custom", [new(1, 0xffffffff), new(0, 0xff000000)]);
        Assert.Throws<InvalidDataException>(() => GradientLibrary.Save(path, [invalid]));
        GradientLibrary.Save(path, GradientLibrary.Defaults()); Assert.Equal(9, GradientLibrary.Load(path).Count);
    }
    [Theory] [InlineData(BrushMode.Blur)] [InlineData(BrushMode.Sharpen)] [InlineData(BrushMode.Dodge)] [InlineData(BrushMode.Burn)] [InlineData(BrushMode.Sponge)]
    public void RetouchUsesStrengthSelectionAndPreservesAlpha(BrushMode mode)
    {
        using var doc = Document(new SKColor(110, 90, 70, 180)); var layer = doc.Layers[0];
        for (var y = 0; y < 64; y++) for (var x = 32; x < 64; x++) layer.Asset!.Image.SetPixel(x, y, new(180, 160, 120, 180));
        var source = layer.Asset; var brush = new BrushSettings(Diameter: 24, Hardness: 1, Strength: 0, Mode: mode, Painting: new());
        Assert.False(BrushEdits.Paint(doc, layer.ID, [new(31, 32)], brush)); Assert.Same(source, layer.Asset);
        using var builder = new SKPathBuilder(); builder.AddRect(new(0, 0, 32, 64)); doc.Selection = DocumentSelection.FromPath(builder.Detach());
        Assert.True(BrushEdits.Paint(doc, layer.ID, [new(31, 32)], brush with { Strength = 1 }));
        Assert.Equal(source!.Image.GetPixel(40, 32), layer.Asset!.Image.GetPixel(40, 32));
        Assert.Equal(180, layer.Asset.Image.GetPixel(30, 32).Alpha); Assert.NotEqual(source.Image.GetPixel(31, 32), layer.Asset.Image.GetPixel(31, 32));
    }
    [Fact] public void SampleAllLayersCanPaintAnEmptyLayerAndFingerPaintIntroducesForeground()
    {
        using var doc = Document(SKColors.Orange); var upper = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Picture(64,64,SKColors.Transparent), "Retouch"), new(0,0,64,64), "Retouch"); doc.Layers.Add(upper);
        Assert.True(BrushEdits.Paint(doc, upper.ID, [new(32,32)], new(Diameter:20,Hardness:1,Strength:1,Mode:BrushMode.Blur,SampleAll:true,Painting:new())));
        Assert.True(upper.Asset!.Image.GetPixel(32,32).Red > 200);
        using var finger = Document(SKColors.Blue); var layer = finger.Layers[0];
        BrushEdits.Paint(finger, layer.ID, [new(16,32),new(40,32)], new(Diameter:14,Hardness:1,Strength:1,Mode:BrushMode.Smudge,FingerPaint:true,Red:1,Painting:new()));
        Assert.True(layer.Asset!.Image.GetPixel(16,32).Red > 150);
    }
    [Fact] public void EveryAdjustmentPresetIsValidAndClarityRequires28()
    {
        for (var i=0;i<18;i++) Assert.True(AdjustmentBrushPresets.Create(i).IsValid, $"Preset {i} invalid");
        using var doc=Document(SKColors.Gray); doc.Layers.Add(new ImageLayer(Guid.NewGuid(),null,new(0,0,64,64),"Clarity") { Adjustment=AdjustmentBrushPresets.Create(1) });
        var snapshot=ProjectSnapshot.FromDocument(doc); Assert.Equal(28,snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version=27; Assert.Throws<ProjectException>(()=>ProjectStore.Validate(snapshot.Manifest));
        using var full=DocumentRenderer.Render(doc); using var region=DocumentRenderer.RenderRegion(doc,SKRectI.Create(20,20,10,10)); Assert.Equal(full.GetPixel(23,23),region.GetPixel(3,3));
    }
    [Fact] public void SegmentConstraintKeepsAdjacentHandleFixed()
    {
        var nodes=PathNodes.Parse("M0 0 C10 0 20 10 30 10 C40 10 50 0 60 0")!; var original=nodes.Subpaths[0].Nodes[1].Out;
        PathSegments.Move(nodes,0,0,.5f,new(0,10),true); Assert.Equal(original,nodes.Subpaths[0].Nodes[1].Out);
        Assert.NotEqual(new SKPoint(20,10),nodes.Subpaths[0].Nodes[1].In);
        PathSegments.Move(nodes,0,0,.5f,new(0,10),false); Assert.NotEqual(original,nodes.Subpaths[0].Nodes[1].Out);
    }
    [Theory] [InlineData(LensEffect.AnamorphicFlare)] [InlineData(LensEffect.Starburst)] [InlineData(LensEffect.Mist)] [InlineData(LensEffect.Prism)] [InlineData(LensEffect.Bokeh)]
    public void LensEffectsChangeLightWithoutChangingAlpha(LensEffect effect)
    {
        using var source=Picture(48,48,new(20,20,20,180)); for(var y=18;y<25;y++) for(var x=18;x<25;x++) source.SetPixel(x,y,new SKColor(255,240,220,180));
        using var output=LensEffects.Apply(source,new(){Effect=effect}); var data=MemoryMarshal.Cast<byte,float>(output.GetPixelSpan());
        Assert.All(data.ToArray(), v=>Assert.True(float.IsFinite(v))); Assert.InRange(data[3],.70f,.71f);
        using var before=DeepPixels.From(source); Assert.False(before.GetPixelSpan().SequenceEqual(output.GetPixelSpan()));
    }
    [Fact] public void LensIdentityAndCopyAreIndependent()
    {
        using var source=Picture(16,16,SKColors.Gray); using var result=LensEffects.Apply(source,new(){Effect=LensEffect.Mist,MistStrength=0}); Assert.Equal(source.GetPixel(3,3),result.GetPixel(3,3));
        var original=new FilterSettings(); var copy=original.Copy(); copy.Lens.PrismAmount=20; Assert.Equal(80,original.Lens.PrismAmount);
    }
    [Fact] public void PatternBucketCanTargetAMaskWithoutEditingLayerPixels()
    {
        using var doc=Document(SKColors.Blue); var layer=doc.Layers[0]; var pixels=layer.Asset;
        layer.Mask=Compositor.Core.Model.LayerMask.AssetFrom(Gray(64,64,0)); using var pattern=Picture(2,2,SKColors.White); pattern.SetPixel(0,0,SKColors.Black);
        Assert.True(BucketEdits.ApplyMask(doc,layer.ID,new(10,10),new WandOptions(),SKColors.White,1,pattern,PaintBlend.Normal,true));
        Assert.Same(pixels,layer.Asset); Assert.Equal(0,layer.Mask.Asset.Image.GetPixel(10,10).Red); Assert.Equal(255,layer.Mask.Asset.Image.GetPixel(11,10).Red);
    }
    [Fact] public void ThickPathStrokeExpandsAssetWithoutMovingTheOutline()
    {
        using var doc = Document(SKColors.Transparent);
        var style = new LayerShapeStyle { Kind = ShapeKind.Path, Path = "M0 0 L1 0 L1 1 L0 1 Z", Red = 1 };
        var id = ShapeEdits.Add(doc, style, SKRectI.Create(10, 10, 40, 40), null)!.Value;
        var layer = doc.Layers.Single(l => l.ID == id);
        var appearance = new LayerShapeStyle { Kind = ShapeKind.Path, Path = style.Path, Red = 1, StrokeColor = 0xff000000, StrokeWidth = 10 };
        Assert.True(ShapeEdits.SetPath(doc, id, style.Path, appearance));
        Assert.Equal(5, layer.Transform.X, 3); Assert.Equal(50, layer.Transform.Width, 3);
        using var outline = SKPath.ParseSvgPathData(layer.LiveShape!.Path!);
        outline.Transform(BrushEdits.PixelToDocument(layer.Transform, 1, 1));
        Assert.Equal(10, outline.TightBounds.Left, 3); Assert.Equal(50, outline.TightBounds.Right, 3);
        Assert.True(ShapeEdits.SetPath(doc, id, layer.LiveShape.Path!));
        Assert.Equal(5, layer.Transform.X, 3); Assert.Equal(50, layer.Transform.Width, 3);
    }
    [Fact] public void PrimitiveStrokeRetainsItsFullWidthAtAssetEdges()
    {
        using var image = ShapeEdits.Image(new() { Kind = ShapeKind.Rectangle, Red = 1, StrokeColor = 0xff000000, StrokeWidth = 6 }, 40, 40)!;
        Assert.Equal(SKColors.Black, image.GetPixel(4, 20));
        Assert.Equal(SKColors.Red, image.GetPixel(8, 20));
    }
}
