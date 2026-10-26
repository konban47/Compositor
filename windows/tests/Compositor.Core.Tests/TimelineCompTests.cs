using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class TimelineCompTests
{
    private static CanvasDocument Document()
    {
        var image=new SKBitmap(Bitmaps.ColorInfo(16,16));image.Erase(SKColors.Coral);var doc=new CanvasDocument(Guid.NewGuid(),64,48);
        doc.Layers.Add(new(Guid.NewGuid(),ImportedImage.Create(image,"Test"),new(2,3,16,16),"Test"));return doc;
    }
    [Fact] public void LayerCompsApplySelectedFieldsAndKeepAssetsAndUnknownLayers()
    {
        using var doc=Document();var layer=doc.Layers[0];var asset=layer.Asset;var comp=LayerComps.Capture(doc,"Original");
        layer.Transform=layer.Transform with{X=20};layer.Opacity=.3;layer.IsVisible=false;
        Assert.True(LayerComps.Apply(doc,comp,CompCapture.Visibility));Assert.True(layer.IsVisible);Assert.Equal(.3,layer.Opacity);Assert.Equal(20,layer.Transform.X);Assert.Same(asset,layer.Asset);
        var updated=LayerComps.Update(doc,comp,CompCapture.Position);Assert.Equal(20,updated.Layers[0].Transform.X);Assert.Equal(1,updated.Layers[0].Opacity);
        layer.Locks=LayerLocks.All;Assert.False(LayerComps.Apply(doc,comp));Assert.True(LayerComps.Apply(doc,comp,respectLocks:false));Assert.Equal(2,layer.Transform.X);
    }
    [Fact] public void ScrubInterpolatesOnBorrowedCloneAndLeavesHistorySourceUnchanged()
    {
        using var doc=Document();var layer=doc.Layers[0];var clip=new TimelineClip{LayerID=layer.ID,Duration=2,Keys=[new(0,layer.Transform,1),new(2,layer.Transform with{X=42},0)]};
        var t=new DocumentTimeline{Clips=[clip],WorkEnd=2};using var preview=TimelineMedia.Preview(doc,t,1);
        Assert.Equal(22,preview.Document.Layers[0].Transform.X);Assert.Equal(.5,preview.Document.Layers[0].Opacity);Assert.Equal(2,layer.Transform.X);Assert.Equal(1,layer.Opacity);
    }
    [Fact] public void SplitAndTrimPreserveAnimationAtBoundaryAndSourceOffset()
    {
        using var doc=Document();var transform=doc.Layers[0].Transform;var clip=new TimelineClip{LayerID=doc.Layers[0].ID,Start=1,Duration=4,Speed=2,SourceStart=3,Keys=[new(0,transform,1),new(4,transform with{X=42},0)]};
        var t=TimelineEdits.Split(new(){Clips=[clip]},clip.ID,3);Assert.True(t.IsValid);Assert.Equal(2,t.Clips.Length);Assert.Equal(7,t.Clips[1].SourceStart);Assert.Equal(t.Clips[0].Keys[^1].Transform,t.Clips[1].Keys[0].Transform);
        var trimmed=TimelineEdits.Trim(clip,2,4);Assert.Equal(5,trimmed.SourceStart);Assert.Equal(12,trimmed.Keys[0].Transform.X);Assert.Equal(32,trimmed.Keys[^1].Transform.X);Assert.True(trimmed.IsValid);
    }
    [Fact] public void VideoConversionSamplesEachFrameIncludingLockedLayers()
    {
        using var doc=Document();var layer=doc.Layers[0];layer.Locks=LayerLocks.All;
        var t=new DocumentTimeline{FrameRate=10,WorkEnd=1,Clips=[new(){LayerID=layer.ID,Duration=1,Keys=[new(0,layer.Transform,1),new(1,layer.Transform with{X=32},1)]}]};
        var frames=TimelineEdits.ConvertToFrames(doc,t);Assert.Equal(10,frames.Frames.Length);Assert.True(frames.IsValid);
        using var preview=doc.Clone();TimelineEdits.Evaluate(preview,frames,.5);Assert.Equal(17,preview.Layers[0].Transform.X,5);
        var video=TimelineEdits.ConvertToVideo(frames);Assert.True(video.IsValid);TimelineEdits.Evaluate(preview,video,.5);Assert.Equal(17,preview.Layers[0].Transform.X,5);
    }
    [Fact] public void HiddenFrameLayersStayHiddenAfterConversionToVideo()
    {
        using var doc=Document();doc.Layers[0].IsVisible=false;var t=TimelineEdits.Create(doc,TimelineMode.Frames);var video=TimelineEdits.ConvertToVideo(t);doc.Layers[0].IsVisible=true;
        TimelineEdits.Evaluate(doc,video,0);Assert.False(doc.Layers[0].IsVisible);
    }
    [Fact] public void FrameRoundtripPreservesLinkedMediaClockAndRejectsOversizedClipExpansion()
    {
        using var doc=Document();
        var source=new TimelineClip{LayerID=doc.Layers[0].ID,Duration=1,SourceStart=3,Speed=2,SourceFile=Path.Combine(Path.GetTempPath(),"linked.mp4")};
        var timeline=new DocumentTimeline{FrameRate=10,WorkEnd=1,Clips=[source]};
        var frames=TimelineEdits.ConvertToFrames(doc,timeline);var video=TimelineEdits.ConvertToVideo(frames);
        Assert.True(video.IsValid);Assert.Equal(source.SourceFile,video.Clips[5].SourceFile);Assert.Equal(4,video.Clips[5].SourceStart,6);Assert.Equal(2,video.Clips[5].Speed);
        var first=frames.Frames[0].State.Layers[0];var state=frames.Frames[0].State with{Layers=[first,first with{LayerID=Guid.NewGuid()},first with{LayerID=Guid.NewGuid()}]};
        var tooMany=frames with{Frames=System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Range(0,4000).Select(_=>frames.Frames[0] with{ID=Guid.NewGuid(),State=state}))};
        Assert.True(tooMany.IsValid);
        Assert.Throws<InvalidOperationException>(()=>TimelineEdits.ConvertToVideo(tooMany));
    }
    [Fact] public void FrameConversionPreservesFillBlendAndOnionPreviewDoesNotAlterExport()
    {
        using var doc=Document();var layer=doc.Layers[0];layer.FillOpacity=.4;layer.BlendMode=LayerBlendMode.Screen;
        var frames=TimelineEdits.Create(doc,TimelineMode.Frames);var video=TimelineEdits.ConvertToVideo(frames);
        layer.FillOpacity=1;layer.BlendMode=LayerBlendMode.Normal;TimelineEdits.Evaluate(doc,video,0);
        Assert.Equal(.4,layer.FillOpacity);Assert.Equal(LayerBlendMode.Screen,layer.BlendMode);
        var timeline=TimelineEdits.Create(doc,TimelineMode.Video) with{OnionSkin=true};
        using var onion=TimelineMedia.Preview(doc,timeline,1,onion:true);using var export=TimelineMedia.Preview(doc,timeline,1);
        Assert.Equal(2,onion.Document.Layers.Count);Assert.Single(export.Document.Layers);Assert.Single(doc.Layers);
    }
    [Fact] public void SerializedTimelineAndCompsRetainAppearanceAndRejectInvalidReferences()
    {
        using var doc=Document();doc.Layers[0].FillOpacity=.3;doc.LayerComps.Add(LayerComps.Capture(doc,"Draft"));doc.Timeline=TimelineEdits.ConvertToVideo(TimelineEdits.Create(doc,TimelineMode.Frames));
        using var snapshot=ProjectSnapshot.FromDocument(doc);
        var json=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot.Manifest,ManifestJson.Options);
        var manifest=ManifestJson.Deserialize(json);ProjectStore.Validate(manifest);
        Assert.Equal(.3,manifest.Timeline!.Clips[0].State!.FillOpacity);Assert.Equal(.3,manifest.LayerComps![0].Layers[0].FillOpacity);
        Assert.False((manifest.Timeline.Clips[0] with{State=manifest.Timeline.Clips[0].State! with{LayerID=Guid.NewGuid()}}).IsValid);
    }
    [Fact] public void TimelineAndCompsSaveReopenAndRejectOldVersionsOrInvalidTiming()
    {
        using var doc=Document();doc.LayerComps.Add(LayerComps.Capture(doc,"Layout A"));doc.Timeline=TimelineEdits.Create(doc,TimelineMode.Video) with{Notes=[new(Guid.NewGuid(),1,"Review <tag>")]};
        using var snapshot=ProjectSnapshot.FromDocument(doc);Assert.Equal(29,snapshot.Manifest.Version);ProjectStore.Validate(snapshot.Manifest);using var restored=snapshot.ToDocument();Assert.Single(restored.LayerComps);Assert.Single(restored.Timeline!.Notes);
        snapshot.Manifest.Version=28;Assert.Throws<ProjectException>(()=>ProjectStore.Validate(snapshot.Manifest));
        Assert.False((doc.Timeline with{FrameRate=double.NaN}).IsValid);Assert.False((doc.Timeline with{Clips=[doc.Timeline.Clips[0] with{Duration=-1}]}).IsValid);
    }
    [Fact] public void ExportUsesActualFrameContentAndPreservesOutputOnCancellation()
    {
        // FFmpeg is optional on build agents; the deterministic renderer remains tested above.
        if(!Find("ffmpeg")||!Find("ffprobe"))return;
        var folder=Path.Combine(Path.GetTempPath(),"timeline-test-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            using var doc=Document();var t=TimelineEdits.Create(doc,TimelineMode.Video) with{FrameRate=10,WorkEnd=.3,Clips=[new(){LayerID=doc.Layers[0].ID,Duration=.3}]};var file=Path.Combine(folder,"test.mp4");
            TimelineMedia.Export(doc,t,file,64,48,10,0,.3,audio:false);var info=TimelineMedia.Inspect(file);Assert.Equal(64,info.Width);Assert.InRange(info.Duration,.29,.41);
            using var frame=TimelineMedia.VideoFrame(file,0,64);Assert.True(frame.GetPixel(8,8).Red>100);var saved=File.ReadAllBytes(file);
            using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>TimelineMedia.Export(doc,t,file,64,48,10,0,.3,cancellation:cancel.Token));Assert.Equal(saved,File.ReadAllBytes(file));
        }
        finally{Directory.Delete(folder,true);}
    }
    private static bool Find(string name)=>(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Any(p=>File.Exists(Path.Combine(p,name+(OperatingSystem.IsWindows()?".exe":""))));
}
