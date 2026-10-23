using System.Collections.Immutable;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;
namespace Compositor.Core.Tests;
public sealed class GifAnimationTests
{
    internal static byte[] Sample()
    {
        using var images=new MagickImageCollection();
        foreach(var (color,delay) in new[]{(MagickColors.Red,2u),(MagickColors.Blue,4u),(MagickColors.Lime,6u)})images.Add(new MagickImage(color,12,10){AnimationDelay=delay,AnimationTicksPerSecond=100,AnimationIterations=0,GifDisposeMethod=GifDisposeMethod.Background});
        using var output=new MemoryStream();images.Write(output,MagickFormat.Gif);return output.ToArray();
    }
    private static CanvasDocument Document(out ImageLayer layer)
    {
        var source=new GifSource(Sample());var image=source.Decode(0,"Animation");image.Animation=new GifAnimation(source);var doc=ImageImporter.NewDocument(image);layer=doc.Layers.Single();return doc;
    }
    [Fact]public void SourceSeeksFramesAndRetainsDelays()
    {
        var source=new GifSource(Sample());Assert.Equal(new[]{.02,.04,.06},source.Delays);
        using var last=source.Decode(2,"Last");using var first=source.Decode(0,"First");Assert.Equal(SKColors.Lime,last.Image.GetPixel(4,4));Assert.Equal(SKColors.Red,first.Image.GetPixel(4,4));
    }
    [Fact]public void NavigationDoesNotCreateEditsAndPaintingTouchesOnlySelectedFrame()
    {
        using var doc=Document(out var layer);var animation=layer.Gif;layer.ShowGifFrame(1);Assert.Same(animation,layer.Gif);Assert.Empty(animation!.Edited);
        Assert.True(BrushEdits.Paint(doc,layer.ID,[new SKPoint(4.5f,4.5f)],new BrushSettings(Diameter:3,Green:1,Painting:new())));
        Assert.Single(layer.Gif!.Edited);Assert.Equal(1,layer.Gif.SelectedFrameIndex);layer.ShowGifFrame(0);Assert.Equal(SKColors.Red,layer.Asset!.Image.GetPixel(4,4));layer.ShowGifFrame(1);Assert.Equal(SKColors.Lime,layer.Asset!.Image.GetPixel(4,4));
    }
    [Fact]public void DuplicateDeleteReorderAndTimingUseStableIdentities()
    {
        var a=new GifAnimation(new GifSource(Sample()));Assert.Equal(2,a.Move(2,-1).SelectedFrameIndex);a=a.Duplicate(1);var id=a.SelectedFrameIndex;Assert.Equal(3,id);Assert.Equal(new[]{0,1,3,2},a.Frames.Select(f=>f.Index));Assert.Equal(.04,a.Delay(id));
        a=a.Move(id,-1).Remove(1);Assert.Equal(new[]{0,3,2},a.Frames.Select(f=>f.Index));Assert.Contains(1,a.Deleted);Assert.Equal(a.Frames,GifAnimation.Restore(a.Source,a.Record(Guid.NewGuid()),a.Edited).Frames);
        a=a with {TimingMode="Same for all",UniformDelay=.15};Assert.All(a.Frames,f=>Assert.Equal(.15,a.Delay(f.Index)));
    }
    [Theory][InlineData(17)][InlineData(27)]public void ProjectRoundTripPreservesSourceEditsOrderAndDelays(int version)
    {
        using var doc=Document(out var layer);layer.ShowGifFrame(1);BrushEdits.Paint(doc,layer.ID,[new SKPoint(4.5f,4.5f)],new BrushSettings(Diameter:3,Green:1,Painting:new()));
        layer.Gif=layer.Gif!.Duplicate(1) with {TimingMode="Same for all",UniformDelay=.22};layer.ShowGifFrame(3);
        var path=Path.Combine(Path.GetTempPath(),"gif-"+Guid.NewGuid()+".comp");
        try
        {
            var snapshot=ProjectSnapshot.FromDocument(doc);Assert.Equal(27,snapshot.Manifest.Version);snapshot.Manifest.Version=version;ProjectStore.Save(snapshot,path);
            using var loaded=ProjectStore.Load(path);using var restored=loaded.ToDocument();var copy=restored.Layers.Single();Assert.Equal(4,copy.Gif!.Frames.Count);Assert.Equal(3,copy.GifFrameIndex);Assert.Equal(.22,copy.Gif.Delay(3));Assert.Equal(SKColors.Lime,copy.Asset!.Image.GetPixel(4,4));copy.ShowGifFrame(2);Assert.Equal(SKColors.Lime,copy.Asset!.Image.GetPixel(4,4));
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [Fact]public void SourceReferencesAndPathsAreValidated()
    {
        var id=Guid.NewGuid();var source=new GifSource(Sample());var record=new GifAnimation(source).Record(id);Assert.True(record.IsValid(id));Assert.False((record with {SourceFile="../escape.gif"}).IsValid(id));Assert.False((record with {FrameOrder=[new(0,0),new(0,1)]}).IsValid(id));
        Assert.Throws<InvalidDataException>(()=>GifAnimation.Restore(source,record with {SelectedFrameIndex=25},ImmutableDictionary<int,ImportedImage>.Empty));
        Assert.Throws<InvalidDataException>(()=>GifAnimation.Restore(source,record with {FrameOrder=[new(0,0)]},ImmutableDictionary<int,ImportedImage>.Empty));
    }
    [Fact]public void ResizeAppliesGeometryToUneditedAndEditedFrames()
    {
        using var doc=Document(out var layer);layer.ShowGifFrame(1);BrushEdits.Paint(doc,layer.ID,[new SKPoint(4.5f,4.5f)],new BrushSettings(Diameter:3,Green:1,Painting:new()));
        Assert.True(ImageEdits.Resize(doc,24,20,72));Assert.Equal(24,layer.Asset!.Width);Assert.Single(layer.Gif!.Edited);layer.ShowGifFrame(2);Assert.Equal(24,layer.Asset!.Width);Assert.Equal(SKColors.Lime,layer.Asset.Image.GetPixel(8,8));layer.ShowGifFrame(1);Assert.Equal(SKColors.Lime,layer.Asset!.Image.GetPixel(9,9));Assert.True(layer.Gif.Record(layer.ID).IsValid(layer.ID));
    }
    [Fact]public void ExportPreservesCadenceAndDoesNotMutateDocument()
    {
        using var doc=Document(out var layer);var before=layer.Asset;var animation=layer.Gif;
        using var result=ExportImage.Encode(doc,new(ExportFormat.Gif,12,10));var source=new GifSource(result.Bytes);Assert.Equal(new[]{.02,.04,.06},source.Delays);using var last=source.Decode(2,"Last");Assert.Equal(SKColors.Lime,last.Image.GetPixel(4,4));Assert.Same(before,layer.Asset);Assert.Same(animation,layer.Gif);
    }
    [Fact]public void ScheduleMergesClocksAndHoldsShorterFinalFrame()
    {
        var a=new GifAnimation(new GifSource(Sample()));var b=a with {TimingMode="Same for all",UniformDelay=.03};var schedule=GifExport.Schedule([a,b]);Assert.Equal(12,schedule.Sum(f=>f.Ticks));Assert.All(schedule,f=>Assert.InRange(f.Ticks,2,65535));Assert.Equal(2,GifExport.FrameAt(b,100));
        var shortOnly=a with {Order=[new(0,0)],Deleted=ImmutableHashSet.Create(1,2)};Assert.Equal(2,GifExport.Schedule([shortOnly]).Single().Ticks);
    }
    [Fact]public void MemoryLimitRefusesGifExportBeforeMutation()
    {
        using var doc=Document(out var layer);var before=layer.Asset;using var budget=new MemoryBudget(100,()=>0).Enter();Assert.Throws<OutOfMemoryException>(()=>GifExport.Encode(doc,new(ExportFormat.Gif,12,10)));Assert.Same(before,layer.Asset);
    }
    [Fact]public void IndependentSeekDecodesOptimizedPartialFramesAndTransparency()
    {
        using var frames=new MagickImageCollection();
        for(var f=0;f<3;f++)
        {
            using var pixels=Bitmaps.Allocate(Bitmaps.ColorInfo(20,20));pixels.Erase(f==2?SKColors.Transparent:SKColors.Red);for(var y=5;y<10;y++)for(var x=5;x<10;x++)pixels.SetPixel(x,y,f==0?SKColors.Red:f==1?SKColors.Blue:SKColors.Lime);
            frames.Add(new MagickImage(PngCodec.Encode(pixels)){AnimationDelay=10,GifDisposeMethod=GifDisposeMethod.Background});
        }
        frames.Optimize();using var stream=new MemoryStream();frames.Write(stream,MagickFormat.Gif);var source=new GifSource(stream.ToArray());using var second=source.Decode(1,"Partial");using var last=source.Decode(2,"Transparent");Assert.Equal(SKColors.Red,second.Image.GetPixel(1,1));Assert.Equal(SKColors.Blue,second.Image.GetPixel(7,7));Assert.Equal((byte)0,last.Image.GetPixel(1,1).Alpha);Assert.Equal(SKColors.Lime,last.Image.GetPixel(7,7));
    }
    [Fact]public void NonUniformResizeKeepsRotatedUneditedFramesAlignedWithCurrentFrame()
    {
        using var doc=Document(out var layer);layer.Transform=layer.Transform with {Rotation=30};Assert.True(ImageEdits.Resize(doc,24,30,72));using var current=Bitmaps.Copy(layer.Asset!.Image);layer.ShowGifFrame(0);Assert.Equal(current.Width,layer.Asset!.Width);Assert.Equal(current.Height,layer.Asset.Height);
        var expected=current.Pixels;var actual=layer.Asset.Image.Pixels;Assert.True(expected.Zip(actual).All(p=>Math.Abs(p.First.Alpha-p.Second.Alpha)<=2));
    }

    [Fact]public void IncompatiblePixelReplacementIsRefusedBeforePublishing()
    {
        using var doc=Document(out var layer);var asset=layer.Asset;var animation=layer.Gif;
        using var image=ImportedImage.Create(Bitmaps.Allocate(Bitmaps.ColorInfo(17,13)),"Changed grid");
        Assert.Throws<GifGeometryException>(()=>layer.Asset=image);
        Assert.Same(asset,layer.Asset);Assert.Same(animation,layer.Gif);
        layer.ShowGifFrame(2);Assert.Equal(SKColors.Lime,layer.Asset!.Image.GetPixel(4,4));
    }
}
