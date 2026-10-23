using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Tests;
public sealed class PaintBrushEngineTests
{
    private static CanvasDocument Make(SKColor color, out ImageLayer layer, int width = 64, int height = 64)
    {
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); bitmap.Erase(color);
        var document = new CanvasDocument(Guid.NewGuid(), width, height);
        layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Paint"), new LayerTransform(0, 0, width, height), "Paint"); document.Layers.Add(layer); return document;
    }
    private static BrushSettings Brush(PaintBrushOptions? p = null) => new(Diameter: 12, Red: 1, Painting: p ?? new PaintBrushOptions(), Seed: 43);
    private static SKColor At(ImageLayer layer, int x = 32, int y = 32) => layer.Asset!.Image.GetPixel(x, y);
    private static bool Paint(CanvasDocument doc, ImageLayer layer, BrushSettings brush, params SKPoint[] points) => BrushEdits.Paint(doc, layer.ID, points.Length == 0 ? [new SKPoint(32.5f, 32.5f)] : points, brush);
    public static IEnumerable<object[]> Blends() => Enum.GetValues<PaintBlend>().Select(m => new object[] { m });
    [Theory, MemberData(nameof(Blends))]
    public void AllBlendsAreFiniteAndZeroOpacityPreservesOriginal(PaintBlend mode)
    {
        var b = new SKColor(87, 144, 229, 128); var s = new SKColor(201, 32, 61, 175);
        Assert.Equal(b, BrushBlending.Composite(b, s, 0, mode));
        var c = BrushBlending.Composite(new SKColorF(.3f, .5f, .9f, .5f), new SKColorF(.7f, .1f, .2f, .7f), .6, mode);
        Assert.True(float.IsFinite(c.Red + c.Green + c.Blue + c.Alpha)); Assert.InRange(c.Alpha, 0, 1);
    }
    [Theory]
    [InlineData(PaintBlend.Multiply, 64)] [InlineData(PaintBlend.Screen, 192)] [InlineData(PaintBlend.LinearBurn, 1)]
    [InlineData(PaintBlend.Difference, 0)] [InlineData(PaintBlend.Exclusion, 127)] [InlineData(PaintBlend.LinearDodgeAdd, 255)]
    public void BlendKnownMidGrayResults(PaintBlend mode, int expected)
    { Assert.InRange((int)BrushBlending.Composite(new SKColor(128,128,128), new SKColor(128,128,128), 1, mode).Red, expected - 1, expected + 1); }
    [Fact] public void SinglePixelPencilAndAutoEraseUseBackground()
    {
        using var doc = Make(SKColors.Transparent, out var layer);
        var b = Brush(new() { Pencil = true }) with { Diameter = 1 };
        Assert.True(Paint(doc, layer, b, new SKPoint(20,20))); Assert.Equal(SKColors.Red, At(layer,20,20)); Assert.Equal((byte)0, At(layer,21,20).Alpha);
        Assert.True(Paint(doc, layer, b with { Painting = b.Painting! with { AutoErase = true, Background = (uint)SKColors.Blue } }, new SKPoint(20,20)));
        Assert.Equal(SKColors.Blue, At(layer,20,20));
    }
    [Fact] public void FlowBuildsWithinOpacityCapAndPressureReducesCoverage()
    {
        using var doc = Make(SKColors.Transparent, out var layer);
        var b = Brush(new() { Flow = .2, Airbrush = true }) with { Opacity = .5 };
        Paint(doc,layer,b,Enumerable.Repeat(new SKPoint(32.5f,32.5f),20).ToArray()); Assert.InRange((int)At(layer).Alpha,120,128);
        using var other = Make(SKColors.Transparent, out var otherLayer);
        Paint(other,otherLayer,b with { PressureOpacity = true, Painting = b.Painting! with { Samples = [new PenSample(.25f)] } }); Assert.InRange((int)At(otherLayer).Alpha,5,8);
    }
    [Fact] public void BadPenDataIsSanitizedAndInvalidSettingsDoNotPublish()
    {
        using var doc = Make(SKColors.Transparent,out var layer); var asset = layer.Asset;
        Assert.False(Paint(doc,layer,Brush(new() { Flow = double.NaN }))); Assert.Same(asset,layer.Asset);
        Assert.True(Paint(doc,layer,Brush(new() { Samples = [new PenSample(float.NaN,float.PositiveInfinity)] })));
    }
    [Fact] public void CloneUsesMappedSourceWithoutFeedback()
    {
        using var doc = Make(SKColors.Blue,out var layer); var bitmap = Bitmaps.Copy(layer.Asset!.Image); for(var y=0;y<64;y++) for(var x=0;x<24;x++) bitmap.SetPixel(x,y,SKColors.Red); layer.Asset = ImportedImage.Create(bitmap,"Source");
        Assert.True(Paint(doc,layer,Brush() with { Mode=BrushMode.Clone, CloneFrom=new(-20,0) })); Assert.Equal(SKColors.Red,At(layer)); Assert.Equal(SKColors.Blue,At(layer,45,32));
    }
    [Fact] public void HistoryRestoresTransparentPixelsAndRejectsWrongDimensions()
    {
        using var doc = Make(SKColors.Red,out var layer); using var history = Bitmaps.Allocate(Bitmaps.ColorInfo(64,64)); history.Erase(SKColors.Transparent);
        Assert.True(Paint(doc,layer,Brush() with { Mode=BrushMode.History,History=history })); Assert.Equal((byte)0,At(layer).Alpha); Assert.Equal(SKColors.Red,At(layer,5,5));
        using var small = Bitmaps.Allocate(Bitmaps.ColorInfo(8,8)); var before=layer.Asset; Assert.False(Paint(doc,layer,Brush() with { Mode=BrushMode.History,History=small })); Assert.Same(before,layer.Asset);
    }
    [Fact] public void PatternAlignmentChangesPhaseAndImpressionistSamplesDabColor()
    {
        using var tile=Bitmaps.Allocate(Bitmaps.ColorInfo(3,3)); tile.Erase(SKColors.Red); tile.SetPixel(0,0,SKColors.Blue);
        using var doc=Make(SKColors.Transparent,out var layer); Paint(doc,layer,Brush() with { Mode=BrushMode.Pattern,Pattern=tile }); Assert.Equal(SKColors.Blue,At(layer,33,33));
        using var doc2=Make(SKColors.Transparent,out var layer2); Paint(doc2,layer2,Brush(new() { PatternAligned=false }) with { Mode=BrushMode.Pattern,Pattern=tile }); Assert.NotEqual(At(layer),At(layer2));
        using var doc3=Make(SKColors.Transparent,out var layer3); Paint(doc3,layer3,Brush(new() { PatternImpressionist=true }) with { Mode=BrushMode.Pattern,Pattern=tile }); Assert.Equal(At(layer3),At(layer3,33,33));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void EveryArtStylePaintsAndFullToleranceSkipsMatchingImage(int style)
    {
        using var source=Bitmaps.Allocate(Bitmaps.ColorInfo(64,64));source.Erase(SKColors.Blue);
        using var doc=Make(SKColors.Red,out var layer); var b=Brush(new() { ArtStyle=style,ArtArea=24 }) with { Mode=BrushMode.ArtHistory,History=source };
        Assert.True(Paint(doc,layer,b)); Assert.Contains(layer.Asset!.Image.Pixels,c=>c.Blue>0);
        using var same=Make(SKColors.Blue,out var other); Assert.False(Paint(same,other,b with { Painting=b.Painting! with { ArtTolerance=1 } }));
    }
    [Fact] public void BackgroundEraserContiguityAndForegroundProtection()
    {
        using var doc=Make(SKColors.White,out var layer);var pixels=Bitmaps.Copy(layer.Asset!.Image);for(var y=0;y<64;y++) pixels.SetPixel(33,y,SKColors.Red);layer.Asset=ImportedImage.Create(pixels,"Fence");
        var b=Brush(new() { ReplaceLimits=1,ReplaceSampling=1 }) with { Mode=BrushMode.BackgroundErase,Tolerance=0 };
        Paint(doc,layer,b);Assert.Equal((byte)0,At(layer).Alpha);Assert.Equal((byte)255,At(layer,35,32).Alpha);Assert.Equal(SKColors.Red,At(layer,33,32));
        Paint(doc,layer,b with { Painting=b.Painting! with { ReplaceLimits=0,ReplaceSampling=2,Background=(uint)SKColors.White } });Assert.Equal((byte)0,At(layer,35,32).Alpha);
        using var protectedDoc=Make(SKColors.Red,out var protectedLayer); Assert.False(Paint(protectedDoc,protectedLayer,b with { ProtectForeground=true }));
    }
    [Fact] public void EraserBlockErasesSquareCornerAndHistoryOptionUsesHistoryMode()
    {
        using var doc=Make(SKColors.Red,out var layer); Paint(doc,layer,Brush(new() { EraserMode=2 }) with { Erasing=true });Assert.Equal((byte)0,At(layer,37,37).Alpha);
    }
    [Fact] public void MagicEraserHonorsOpacityContiguityAndAntialias()
    {
        using var doc=Make(SKColors.White,out var layer);var pixels=Bitmaps.Copy(layer.Asset!.Image);for(var y=0;y<64;y++)pixels.SetPixel(33,y,SKColors.Red);layer.Asset=ImportedImage.Create(pixels,"Fence");
        Assert.True(BucketEdits.Apply(doc,layer.ID,new(20,20),new(0,0,true,true),SKColors.Black,true,.5));
        Assert.InRange((int)At(layer,20,20).Alpha,127,128);Assert.True(At(layer,32,32).Alpha>At(layer,20,20).Alpha);Assert.Equal(SKColors.White,At(layer,40,32));
    }
    [Fact] public void MirrorSymmetryPaintsBothSides()
    {
        using var doc=Make(SKColors.Transparent,out var layer);Paint(doc,layer,Brush(new(){Symmetry=BrushSymmetry.Vertical}),new SKPoint(12.5f,20.5f));Assert.Equal(SKColors.Red,At(layer,12,20));Assert.Equal(SKColors.Red,At(layer,51,20));Assert.Equal((byte)0,At(layer,32,20).Alpha);
    }
    [Fact] public void ProceduralPatternsRoundTripAndHaveStableUniqueIds()
    {
        var defaults=PatternLibrary.Defaults();Assert.Equal(9,defaults.Count);Assert.Equal(9,defaults.Select(p=>p.ID).Distinct().Count());Assert.Equal(defaults.Select(p=>p.ID),PatternLibrary.Defaults().Select(p=>p.ID));
        foreach(var p in defaults){using var bitmap=PaintBrushEngine.Decode(p.Image);Assert.Equal(64,bitmap!.Width);}
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{EditorPresets.Save(path,defaults);Assert.Equal(defaults,EditorPresets.Load(path));}finally{File.Delete(path);}
    }
    [Fact] public void TextureDualBrushNoiseAndWetEdgesChangeActualPixelsDeterministically()
    {
        byte[] Render(PaintBrushOptions options)
        { using var doc=Make(SKColors.Transparent,out var layer); Paint(doc,layer,Brush(options) with {Diameter=24}); return layer.Asset!.Image.GetPixelSpan().ToArray(); }
        var plain=Render(new());
        foreach(var options in new PaintBrushOptions[]{new(){TextureEnabled=true,Texture=3,TextureDepth=1,TextureMode=1},new(){DualEnabled=true,DualTip=2,DualSize=13},new(){Noise=true},new(){WetEdges=true}})
        {var result=Render(options);Assert.False(plain.SequenceEqual(result));Assert.Equal(result,Render(options));}
    }
    [Fact] public void ScatteringReachesOutsideUnscatteredFootprint()
    {
        using var doc=Make(SKColors.Transparent,out var layer);Paint(doc,layer,Brush(new(){ScatterEnabled=true,ScatterBothAxes=true,Scatter=2,Count=16}) with {Diameter=6});
        Assert.Contains(Enumerable.Range(0,64),y=>Enumerable.Range(0,64).Any(x=>(Math.Abs(x-32)>4||Math.Abs(y-32)>4)&&At(layer,x,y).Alpha>0));
    }
    [Fact] public void TransferAndPosePressureControlOpacity()
    {
        using var doc=Make(SKColors.Transparent,out var layer);Paint(doc,layer,Brush(new(){PoseEnabled=true,OverridePressure=true,Pressure=.3,TransferEnabled=true,OpacityControl=BrushControl.PenPressure}));Assert.InRange((int)At(layer).Alpha,75,78);
    }
    [Fact] public void SelectionClipsStampAndMirrorOnce()
    {
        using var doc=Make(SKColors.Transparent,out var layer);doc.Selection=DocumentSelection.Rectangular(new(0,0,32,64),64,64);Paint(doc,layer,Brush(new(){Symmetry=BrushSymmetry.Vertical}),new SKPoint(12.5f,20.5f));Assert.Equal(SKColors.Red,At(layer,12,20));Assert.Equal((byte)0,At(layer,51,20).Alpha);
    }
    [Fact] public void BudgetFailureDoesNotReplacePixels()
    {
        using var doc=Make(SKColors.Red,out var layer);var before=layer.Asset;using var limited=new MemoryBudget(32,()=>0).Enter();Assert.Throws<OutOfMemoryException>(()=>Paint(doc,layer,Brush()));Assert.Same(before,layer.Asset);
    }
    [Fact] public void FloatPaintingPreservesHdrOutsideStroke()
    {
        using var doc=Make(SKColors.Black,out var layer);var deep=Bitmaps.Allocate(DeepPixels.Info(64,64));var values=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(deep.GetPixelSpan());for(var i=0;i<values.Length;i+=4){values[i]=4;values[i+1]=2;values[i+2]=.5f;values[i+3]=1;}layer.Asset=ImportedImage.CreateDeep(deep,"HDR",32);
        Paint(doc,layer,Brush());var result=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(layer.Asset!.Deep!.GetPixelSpan());Assert.Equal(4,result[0]);Assert.Equal(2,result[1]);Assert.Equal(1,result[(32*64+32)*4]);
    }
    [Fact] public void ColorReplacementLeavesAlphaAndLuminanceIntactInColorMode()
    {
        using var doc=Make(new SKColor(80,120,180,100),out var layer);Paint(doc,layer,Brush(new(){ReplaceSampling=1}) with {Mode=BrushMode.ColorReplacement,Tolerance=0});var color=At(layer);Assert.Equal(100,color.Alpha);Assert.NotEqual((byte)80,color.Red);
        Assert.InRange(.3*color.Red+.59*color.Green+.11*color.Blue,113,116);
    }
    [Fact] public void MixerReservoirCarriesColorAndDepletesLoad()
    {
        using var doc=Make(SKColors.Blue,out var layer);var reservoir=new MixerReservoir(SKColors.Red,1);var b=Brush(new(){Reservoir=reservoir,MixerAutoClean=false}) with {Mode=BrushMode.Mixer,MixerWet=.5,MixerMix=.5,MixerLoad=1};Paint(doc,layer,b);Assert.True(At(layer).Red>0&&At(layer).Blue>0);Assert.True(reservoir.Amount<1&&reservoir.Amount>0);Assert.True(reservoir.Color.Blue>0);
    }

    [Fact] public void RotatedSquareTipsKeepTheirCorners()
    {
        using var doc = Make(SKColors.Blue, out var layer);
        Assert.True(Paint(doc, layer, Brush(new() { EraserMode = 2 }) with { Diameter = 20, Angle = 45, Erasing = true }));
        Assert.Equal((byte)0, At(layer, 45, 32).Alpha);
        Assert.Equal(SKColors.Blue, At(layer, 47, 32));
        Assert.True(Paint(doc, layer, Brush(new() { Tip = 22 }) with { Diameter = 20, Angle = 45 }));
        Assert.Equal(SKColors.Red, At(layer, 45, 32));
    }
    [Fact] public void ExcessiveStationaryBuildUpIsRefusedAtomically()
    {
        using var doc = Make(SKColors.Red, out var layer, 256, 256); var before = layer.Asset;
        var stroke = Enumerable.Repeat(new SKPoint(128,128), 5000).ToArray();
        Assert.Throws<PaintStrokeLimitException>(() => Paint(doc, layer, Brush(new() { BuildUp = true }) with { Diameter = 5000 }, stroke));
        Assert.Same(before, layer.Asset);
    }
}
