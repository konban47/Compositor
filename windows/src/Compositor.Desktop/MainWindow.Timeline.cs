using System.Collections.Immutable;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private double _timelineTime;
    private Guid? _timelineClip;
    private TimelineTracks? _tracks;
    private Action? _stopDocumentTimeline;
    private Action? _playDocumentTimeline;
    private Action<double>? _seekDocumentTimeline;
    private void ChangeTimeline(string title, Func<DocumentTimeline,DocumentTimeline> change)
    {
        if (_document is not { Timeline: { } current } doc) return;
        try { var next=change(current); if(!next.IsValid)throw new InvalidDataException("Invalid timeline settings.");Edit(title,()=>{doc.Timeline=next;return next!=current;}); }
        catch(Exception error){Say(error.Message);}
    }
    private TimelineClip? ActiveClip => _document?.Timeline?.Clips.FirstOrDefault(c=>c.ID==_timelineClip);
    private void BuildDocumentTimeline(StackPanel body)
    {
        var root=new Grid{RowDefinitions=new("Auto,*,Auto"),MinHeight=300,Tag="document-timeline"};
        var top=new WrapPanel{Margin=new Thickness(5),Orientation=Orientation.Horizontal};root.Children.Add(top);
        var tracks=new TimelineTracks{Scale=170,Focusable=true,Tag="timeline-tracks"};_tracks=tracks;
        var scroll=new ScrollViewer{Content=tracks,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=180,MaxHeight=650};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var empty=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16)};Grid.SetRow(empty,1);root.Children.Add(empty);
        var kind=new ComboBox{ItemsSource=new[]{Localize.Text("Create Video Timeline"),Localize.Text("Create Frame Animation")},SelectedIndex=0,Tag="timeline-create-kind"};empty.Children.Add(kind);
        empty.Children.Add(PropertyAction("Create",()=>{CommitText();if(_document is { } doc)Edit("Create Timeline",()=>{doc.Timeline=TimelineEdits.Create(doc,(TimelineMode)kind.SelectedIndex);return true;});}));
        var footer=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(6),VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(footer,2);root.Children.Add(footer);
        var clockLabel=new TextBlock{Margin=new Thickness(10,4),MinWidth=125};footer.Children.Add(clockLabel);
        var rate=TypeButton("30 fps","Timeline Frame Rate…",()=>_ = TimelineFrameRate());footer.Children.Add(rate);
        var zoom=new Slider{Minimum=10,Maximum=300,Value=170,Width=160,Tag="timeline-zoom"};footer.Children.Add(zoom);zoom.ValueChanged+=(_,_)=>{tracks.Scale=zoom.Value;tracks.Update(_document,_document?.Timeline);};
        footer.Children.Add(TypeButton("⊞","New Frame",()=>NewTimelineFrame(false)));footer.Children.Add(TypeButton("⧉","Duplicate Frame",()=>NewTimelineFrame(true)));
        footer.Children.Add(TypeButton("⌫","Delete Frame",DeleteTimelineFrame));footer.Children.Add(TypeButton("◷","Frame Delay…",()=>_ = TimelineFrameDelay()));
        var play=TypeButton("▶","Play Timeline",()=>_playDocumentTimeline?.Invoke());
        top.Children.Add(TypeButton("|◀","First Frame",()=>_seekDocumentTimeline?.Invoke(0)));
        top.Children.Add(TypeButton("◀|","Previous Frame",()=>_seekDocumentTimeline?.Invoke(_timelineTime-1/(_document?.Timeline?.FrameRate??30))));top.Children.Add(play);
        top.Children.Add(TypeButton("|▶","Next Frame",()=>_seekDocumentTimeline?.Invoke(_timelineTime+1/(_document?.Timeline?.FrameRate??30))));
        top.Children.Add(TypeButton("■","Stop Timeline",()=>_stopDocumentTimeline?.Invoke()));
        top.Children.Add(TypeButton("◖))","Audio Settings…",()=>_ = TimelineAudioSettings()));
        var settings=TypeButton("⚙","Playback Resolution",()=>{});var quality=new MenuFlyout();foreach(var percent in new[]{25,50,100})quality.Items.Add(TypeItem(percent+"%",()=>ChangeTimeline("Playback Resolution",t=>t with{PreviewPercent=percent})));settings.Flyout=quality;top.Children.Add(settings);
        top.Children.Add(TypeButton("✂","Split at Playhead",()=>{if(ActiveClip is { } c)ChangeTimeline("Split at Playhead",t=>TimelineEdits.Split(t,c.ID,TimelineEdits.Snap(_timelineTime,t.FrameRate)));}));
        top.Children.Add(TypeButton("◩","Clip Options…",()=>_ = TimelineClipOptions()));
        top.Children.Add(TypeButton("◆","Add Keyframe",()=>AddTimelineKey()));top.Children.Add(TypeButton("+","Add Media…",()=>_ = AddTimelineMedia()));
        var menuButton=TypeButton("≡","Timeline Menu",()=>{});var menu=new MenuFlyout();menu.Opening+=(_,_)=>FillTimelineMenu(menu);menuButton.Flyout=menu;top.Children.Add(menuButton);
        // Keep the existing GIF frame editor accessible, with its tested decode/undo behavior.
        var gifBody=new StackPanel{Spacing=6};BuildGifTimeline(gifBody);var refreshGif=_utilityRefresh["Timeline"];
        var gif=new Expander{Header=Localize.Text("GIF Frame Editor"),Content=gifBody,IsExpanded=false};Grid.SetRow(gif,3);
        body.Children.Add(root);body.Children.Add(gif);
        TimelinePreview? preview=null;CanvasDocument? source=null;Guid revision=Guid.Empty;var generation=0;var busy=false;CancellationTokenSource? pending=null;var audioPlayer=new TimelineAudioPlayer();var audioFailed=false;var playing=false;var clock=new Stopwatch();var start=0d;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};
        void Clear(){_canvas.PreviewResolutionScale=1;if(ReferenceEquals(_canvas.PreviewDocument,preview?.Document))_canvas.PreviewDocument=null;preview?.Dispose();preview=null;_canvas.InvalidateVisual();}
        void Stop(){generation++;pending?.Cancel();pending?.Dispose();pending=null;audioPlayer.Dispose();playing=false;busy=false;timer.Stop();clock.Stop();Clear();play.Content="▶";}
        async Task ShowTime(double time)
        {
            if(_document is not {Timeline:{ } timeline} doc)return;
            _timelineTime=Math.Clamp(time,0,Math.Max(0,timeline.Duration-1/timeline.FrameRate));tracks.Time=_timelineTime;tracks.InvalidateVisual();clockLabel.Text=TimelineEdits.Timecode(_timelineTime,timeline.FrameRate,timeline.FrameNumbers);
            var request=++generation;var at=_timelineTime;var rev=_history.CurrentRevision;busy=true;pending?.Cancel();pending?.Dispose();pending=new();var token=pending.Token;
            using var lease=new DocumentLease(doc);
            try
            {
                var view=await Task.Run(()=>TimelineMedia.Preview(lease.Document,timeline,at,Math.Clamp(Math.Max(doc.Width,doc.Height)*timeline.PreviewPercent/100,128,8192),token,onion:!playing));
                if(request!=generation||doc!=_document||rev!=_history.CurrentRevision){view.Dispose();return;}
                Clear();preview=view;_canvas.PreviewResolutionScale=timeline.PreviewPercent/100d;_canvas.PreviewDocument=view.Document;_canvas.InvalidateVisual();
            }
            catch(OperationCanceledException){}
            catch(Exception error){if(request==generation){Stop();Say(error.Message);}}
            finally{if(request==generation)busy=false;}
        }
        _stopDocumentTimeline=Stop;
        _seekDocumentTimeline=time=>{Stop();_ = ShowTime(time);};tracks.Seek=_seekDocumentTimeline;
        _playDocumentTimeline=()=>
        {
            if(playing){Stop();return;}if(_document is not {Timeline:{ } t})return;
            _stopGifPlayback?.Invoke();Stop();source=_document;revision=_history.CurrentRevision;start=_timelineTime<t.WorkStart||_timelineTime>=t.WorkEnd?t.WorkStart:_timelineTime;
            playing=true;busy=false;audioFailed=false;clock.Restart();timer.Start();play.Content="Ⅱ";
        };
        timer.Tick+=async(_,_)=>
        {
            if(!playing||busy)return;
            if(source!=_document||revision!=_history.CurrentRevision||_dockPanels.GetValueOrDefault("Timeline")?.Visible!=true){Stop();return;}
            var t=_document!.Timeline!;var time=t.AllowFrameSkipping?start+clock.Elapsed.TotalSeconds:_timelineTime+1/t.FrameRate;
            if(time>=t.WorkEnd){if(!t.Loop){Stop();return;}start=t.WorkStart;clock.Restart();time=start;}
            if(!audioFailed)try{audioPlayer.Update(t,time);}catch(Exception error){audioFailed=true;audioPlayer.Dispose();Say(error.Message);}
            await ShowTime(TimelineEdits.Snap(time,t.FrameRate));
        };
        tracks.SelectClip=id=>{_timelineClip=id;if(ActiveClip is { } c)Reselect(c.LayerID);};
        tracks.ChangeClip=c=>ChangeTimeline("Move or Trim Clip",t=>TimelineEdits.Replace(t,c));tracks.EditClip=id=>{_timelineClip=id;_ = TimelineClipOptions();};tracks.AddMedia=id=>{Reselect(id);_ = AddTimelineMedia();};
        tracks.SelectFrame=i=>{if(_document?.Timeline is { } t && i<t.Frames.Length)_seekDocumentTimeline?.Invoke(t.Frames.Take(i).Sum(f=>f.Duration));};
        tracks.KeyDown+=(_,e)=>{if(_document?.Timeline?.Shortcuts!=true)return;if(e.Key==Key.Space){_playDocumentTimeline?.Invoke();e.Handled=true;}else if(e.Key is Key.Left or Key.Right){_seekDocumentTimeline?.Invoke(_timelineTime+(e.Key==Key.Left?-1:1)/_document.Timeline.FrameRate);e.Handled=true;}};
        _utilityRefresh["Timeline"]=()=>{var t=_document?.Timeline;if(source!=_document){Stop();source=_document;_timelineTime=0;}_timelineTime=Math.Min(_timelineTime,t?.Duration??0);tracks.Time=_timelineTime;tracks.Update(_document,t);empty.IsVisible=t is null;scroll.IsVisible=t is not null;top.IsEnabled=footer.IsEnabled=t is not null;rate.Content=$"{t?.FrameRate??30:0.###} fps";clockLabel.Text=TimelineEdits.Timecode(_timelineTime,t?.FrameRate??30,t?.FrameNumbers??false);gif.IsVisible=PropertyLayer?.Gif is not null;if(gif.IsVisible){if(t is null)gif.IsExpanded=true;refreshGif();}root.MinHeight=gif.IsVisible&&t is null?100:300;if(gif.IsVisible&&t is null)root.Height=140;else if(TopLevel.GetTopLevel(body) is Window panel && panel!=this)root.Height=Math.Max(300,panel.ClientSize.Height-(gif.IsVisible?120:60));};
        Window? host=null;
        void SizeTimeline(object? sender, SizeChangedEventArgs e){if(host is not null && host!=this)root.Height=Math.Max(300,host.ClientSize.Height-(gif.IsVisible?120:60));}
        body.AttachedToVisualTree+=(_,_)=>{host=TopLevel.GetTopLevel(body) as Window;if(host is not null){host.SizeChanged+=SizeTimeline;root.Height=host==this?double.NaN:Math.Max(300,host.ClientSize.Height-60);}};
        body.DetachedFromVisualTree+=(_,_)=>{if(host is not null)host.SizeChanged-=SizeTimeline;Stop();host=null;};
        Closed+=(_,_)=>Stop();
    }
    private void NewTimelineFrame(bool duplicate)
    {
        if(_document is not {Timeline:{Mode:TimelineMode.Frames} t} doc||t.Frames.Length>=4096)return;var i=TimelineEdits.FrameAt(t,_timelineTime);
        var frame=duplicate&&t.Frames.Length>0?t.Frames[i] with{ID=Guid.NewGuid()}:new TimelineFrame(Guid.NewGuid(),.1,LayerComps.Capture(doc,$"{Localize.Text("Frame")} {t.Frames.Length+1}"));
        ChangeTimeline("New Frame",v=>{var frames=v.Frames.Insert(Math.Min(i+1,v.Frames.Length),frame);return v with{Frames=frames,WorkEnd=frames.Sum(f=>f.Duration)};});
    }
    private void DeleteTimelineFrame()=>ChangeTimeline("Delete Frame",t=>t.Mode!=TimelineMode.Frames||t.Frames.Length<=1?t:t with{Frames=t.Frames.RemoveAt(TimelineEdits.FrameAt(t,_timelineTime)),WorkEnd=t.Duration-t.Frames[TimelineEdits.FrameAt(t,_timelineTime)].Duration,WorkStart=0});
    private async Task TimelineFrameDelay()
    {
        if(_document is not {Timeline:{Mode:TimelineMode.Frames} t} doc||t.Frames.Length==0)return;var i=TimelineEdits.FrameAt(t,_timelineTime);var id=t.Frames[i].ID;var form=new FormDialog("Frame Delay",400,210);var delay=form.Number("Seconds",t.Frames[i].Duration,.01,600);var all=form.Check("Same for all",false);
        if(await form.ShowDialog<bool>(this)&&doc==_document)ChangeTimeline("Frame Delay",v=>{var frames=v.Frames.Select(f=>all.IsChecked==true||f.ID==id?f with{Duration=(double)delay.Value!}:f).ToImmutableArray();return v with{Frames=frames,WorkStart=0,WorkEnd=frames.Sum(f=>f.Duration)};});
    }
    private async Task TimelineFrameRate()
    {
        if(_document is not {Timeline:{ } t} doc)return;var form=new TimelineRateDialog(t.FrameRate);
        if(await form.ShowDialog<bool>(this)&&doc==_document)ChangeTimeline("Timeline Frame Rate",v=>v with{FrameRate=form.Rate});
    }
    private async Task TimelineClipOptions()
    {
        if(ActiveClip is not { } c)return;var doc=_document;var form=new FormDialog("Clip Options",480,540);var start=form.Number("Start (seconds)",c.Start,0,DocumentTimeline.MaxDuration);var duration=form.Number("Duration (seconds)",c.Duration,.001,DocumentTimeline.MaxDuration);var offset=form.Number("Source Offset",c.SourceStart,0,DocumentTimeline.MaxDuration);var speed=form.Number("Speed (%)",c.Speed*100,1,10000);var fadeIn=form.Number("Fade In",c.FadeIn,0,c.Duration);var fadeOut=form.Number("Fade Out",c.FadeOut,0,c.Duration);var favorite=form.Check("Favorite Clip",c.Favorite);
        if(await form.ShowDialog<bool>(this)&&doc==_document)ChangeTimeline("Clip Options",t=>TimelineEdits.Replace(t,c with{Start=(double)start.Value!,Duration=(double)duration.Value!,SourceStart=(double)offset.Value!,Speed=(double)speed.Value!/100,FadeIn=(double)fadeIn.Value!,FadeOut=(double)fadeOut.Value!,Favorite=favorite.IsChecked==true,Keys=c.Keys.Where(k=>k.Time<=(double)duration.Value!).ToImmutableArray()}));
    }
    private void AddTimelineKey()
    {
        if(ActiveClip is not { } c||_document?.Layers.FirstOrDefault(l=>l.ID==c.LayerID) is not { } layer)return;
        var at=Math.Clamp(TimelineEdits.Snap(_timelineTime-c.Start,_document.Timeline!.FrameRate),0,c.Duration);var key=new TimelineKey(at,layer.Transform,layer.Opacity);
        ChangeTimeline("Add Keyframe",t=>TimelineEdits.Replace(t,c with{Keys=c.Keys.Where(k=>k.Time!=at).Append(key).OrderBy(k=>k.Time).ToImmutableArray()}));
    }
}
