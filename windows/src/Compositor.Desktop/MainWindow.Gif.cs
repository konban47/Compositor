using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private Action? _stopGifPlayback;
    private Func<Task>? _gifPlaybackFrame;
    private int _gifNavigation;
    private async Task NavigateGif(int index)
    {
        if(PropertyLayer is not {Gif:{ } animation} layer)return;
        _stopGifPlayback?.Invoke();var request=++_gifNavigation;var document=_document;
        try
        {
            var image=await WorkScheduler.Run(_=>animation.Decode(index,layer.Name));
            if(request!=_gifNavigation||_document!=document||layer.Gif!=animation||PropertyLayer!=layer){if(!animation.Edited.ContainsKey(index))image.Dispose();return;}
            layer.ShowGifFrame(index,image);ShowLayers(document!);Refresh();
        }
        catch(Exception error){Say(error.Message);}
    }
    private void BuildGifTimeline(StackPanel body)
    {
        var preview=new Image{Height=150,Stretch=Stretch.Uniform};var frame=new Slider{Minimum=0,Maximum=0,TickFrequency=1,IsSnapToTickEnabled=true,Tag="gif-frame"};var label=new TextBlock();
        var mode=new ComboBox{ItemsSource=new[]{Localize.Text("Frame delay"),Localize.Text("Same for all")},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch,Tag="gif-delay-mode"};
        var delay=new NumericUpDown{Minimum=.02m,Maximum=655.35m,Increment=.01m,FormatString="0.00",Tag="gif-delay"};
        body.Children.Add(preview);body.Children.Add(frame);body.Children.Add(label);body.Children.Add(mode);body.Children.Add(new TextBlock{Text=Localize.Text("Frame delay, seconds")});body.Children.Add(delay);
        WriteableBitmap? previous=null;var loading=false;var playing=false;var legacyPlayback=false;var busy=false;var clock=new Stopwatch();string? playbackKey=null;CanvasDocument? playback=null;var decoded=new List<ImportedImage>();CanvasDocument? playbackSource=null;
        GifDecodeSession? readers=null;var retiredReaders=new List<GifDecodeSession>();var generation=0;var schedules=new Dictionary<Guid,GifPlaybackSchedule>();
        void ReleasePreview(){if(ReferenceEquals(_canvas.PreviewDocument,playback))_canvas.PreviewDocument=null;playback?.Dispose();playback=null;foreach(var image in decoded)image.Dispose();decoded.Clear();_canvas.InvalidateVisual();}
        void Stop()
        {
            generation++;playing=false;legacyPlayback=false;playbackKey=null;_timelineTimer.Stop();clock.Stop();ReleasePreview();
            if(readers is { } old){readers=null;if(busy)retiredReaders.Add(old);else old.Dispose();}
        }
        _stopGifPlayback=Stop;
        void RefreshTimeline()
        {
            if(loading)return;loading=true;
            try
            {
                previous?.Dispose();previous=null;preview.Source=null;
                if(PropertyLayer is not {Asset:{ } asset} layer){Stop();return;}
                var animation=layer.Gif;var frames=animation?.Frames;frame.Maximum=Math.Max(0,(frames?.Count??_document?.Layers.Count??1)-1);
                if(animation is not null){var pos=frames!.ToList().FindIndex(f=>f.Index==layer.GifFrameIndex);frame.Value=Math.Max(0,pos);delay.Value=(decimal)animation.Delay(layer.GifFrameIndex);mode.SelectedIndex=animation.TimingMode=="Same for all"?1:0;label.Text=$"{pos+1} / {frames!.Count} — {layer.Name}";}
                else {var index=Math.Clamp((int)frame.Value,0,_document!.Layers.Count-1);asset=_document.Layers[index].Asset??asset;label.Text=Localize.Text("Layer frame preview (5 fps). Document pixels stay unchanged.")+" "+(index+1)+" / "+_document.Layers.Count;}
                _timelineFrame=(int)frame.Value;frame.IsEnabled=true;mode.IsEnabled=delay.IsEnabled=animation is not null;
                previous=CanvasView.ToImage(asset.Image);preview.Source=previous;
            }
            finally{loading=false;}
        }
        frame.ValueChanged+=async(_,_)=>{if(!loading&&PropertyLayer?.Gif is { } gif){var frames=gif.Frames;await NavigateGif(frames[Math.Clamp((int)frame.Value,0,frames.Count-1)].Index);}else if(!loading)RefreshTimeline();};
        mode.SelectionChanged+=(_,_)=>{if(!loading&&PropertyLayer is {Gif:{ } gif} layer)Edit("GIF Timing",()=>{layer.Gif=gif with {TimingMode=mode.SelectedIndex==1?"Same for all":"Frame delay"};return true;});};
        delay.ValueChanged+=(_,_)=>{if(!loading&&delay.Value is { } seconds&&PropertyLayer is {Gif:{ } gif} layer)Edit("GIF Frame Delay",()=>{layer.Gif=gif.TimingMode=="Same for all"?gif with {UniformDelay=(double)seconds}:gif with {Delays=gif.Delays.SetItem(layer.GifFrameIndex,(double)seconds)};return true;});};
        void Change(string title,Func<GifAnimation,int,GifAnimation> change)
        {
            if(PropertyLayer is not {Gif:{ } gif} layer)return;
            try { Edit(title,()=>{var next=change(gif,layer.GifFrameIndex);if(next==gif)return false;layer.Gif=next;layer.ShowGifFrame(next.SelectedFrameIndex);return true;});ShowLayers(_document!); }
            catch(InvalidOperationException error) { Say(error.Message); }
        }
        body.Children.Add(PropertyPair(PropertyAction("Duplicate Frame",()=>Change("Duplicate GIF Frame",(g,i)=>g.Duplicate(i))),PropertyAction("Delete Frame",()=>Change("Delete GIF Frame",(g,i)=>g.Remove(i)))));
        body.Children.Add(PropertyPair(PropertyAction("Move Frame Left",()=>Change("Move GIF Frame",(g,i)=>g.Move(i,-1))),PropertyAction("Move Frame Right",()=>Change("Move GIF Frame",(g,i)=>g.Move(i,1)))));
        body.Children.Add(PropertyPair(PropertyAction("Play",()=>{if(_document is null)return;Stop();if(PropertyLayer?.Gif is null){legacyPlayback=true;_timelineTimer.Interval=TimeSpan.FromMilliseconds(200);_timelineTimer.Start();return;}readers=new GifDecodeSession();schedules=_document.Layers.Where(l=>l.Gif is not null).ToDictionary(l=>l.ID,l=>new GifPlaybackSchedule(l.Gif!,l.GifFrameIndex));playing=true;playbackSource=_document;clock.Restart();_timelineTimer.Interval=TimeSpan.FromMilliseconds(20);_timelineTimer.Start();_=Tick();}),PropertyAction("Stop",Stop)));
        body.Children.Add(PropertyAction("Export GIF…",()=>{Stop();_=ExportAs(ExportFormat.Gif);}));
        async Task Tick()
        {
            if(legacyPlayback){if(_dockPanels.GetValueOrDefault("Timeline")?.Visible!=true){Stop();return;}frame.Value=frame.Value>=frame.Maximum?0:frame.Value+1;return;}
            if(!playing||busy)return;if(_document!=playbackSource||_dockPanels.GetValueOrDefault("Timeline")?.Visible!=true){Stop();return;}
            if(_document is not { } doc||readers is not { } activeReaders)return;busy=true;var requestGeneration=generation;
            try
            {
                var elapsed=clock.Elapsed;
                var indices=schedules.ToDictionary(p=>p.Key,p=>p.Value.FrameAt(elapsed));
                var key=string.Join("/",indices.Select(p=>p.Key+":"+p.Value));
                if(key==playbackKey)return;
                using var lease=new DocumentLease(doc);
                var result=await WorkScheduler.Run(_=>
                {
                    var view=lease.Document.Clone();var owned=new List<ImportedImage>();
                    try{foreach(var layer in view.Layers.Where(l=>l.Gif is not null)){var gif=layer.Gif!;var index=indices[layer.ID];layer.ShowGifFrame(index,activeReaders.Decode(gif,index,layer.Name));if(!gif.Edited.ContainsKey(index))owned.Add(layer.Asset!);}return(view,owned);}
                    catch{view.Dispose();foreach(var image in owned)image.Dispose();throw;}
                });
                if(!playing||_document!=doc||requestGeneration!=generation){result.view.Dispose();foreach(var image in result.owned)image.Dispose();return;}
                ReleasePreview();playbackKey=key;playback=result.view;decoded=result.owned;_canvas.PreviewDocument=playback;_canvas.InvalidateVisual();
            }
            catch(Exception error){if(requestGeneration==generation){Stop();Say(error.Message);}}
            finally{busy=false;foreach(var reader in retiredReaders)reader.Dispose();retiredReaders.Clear();}
        }
        _gifPlaybackFrame=Tick;_timelineTimer.Tick+=async(_,_)=>await Tick();
        _utilityRefresh["Timeline"]=RefreshTimeline;Closed+=(_,_)=>{Stop();previous?.Dispose();};RefreshTimeline();
    }
}
