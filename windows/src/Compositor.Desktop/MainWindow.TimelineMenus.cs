using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void FillTimelineMenu(MenuFlyout menu)
    {
        menu.Items.Clear();if(_document?.Timeline is not { } t)return;
        MenuItem Sub(string title){var item=new MenuItem{Header=Localize.Text(title)};menu.Items.Add(item);return item;}
        var go=Sub("Go To");go.Items.Add(TypeItem("Time…",()=>_ = TimelineGoTo()));
        foreach(var (title,time) in new[]{("First Frame",0d),("Previous Frame",_timelineTime-1/t.FrameRate),("Next Frame",_timelineTime+1/t.FrameRate),("Last Frame",t.Duration-1/t.FrameRate),("Work Area Start",t.WorkStart),("Work Area End",t.WorkEnd-1/t.FrameRate)})go.Items.Add(TypeItem(title,()=>_seekDocumentTimeline?.Invoke(time)));
        menu.Items.Add(TypeItem("Split at Playhead",()=>{if(ActiveClip is { } c)ChangeTimeline("Split at Playhead",v=>TimelineEdits.Split(v,c.ID,TimelineEdits.Snap(_timelineTime,v.FrameRate)));},enabled:ActiveClip is not null));
        var trim=Sub("Move and Trim");
        foreach(var (title,action) in new (string,System.Func<TimelineClip,TimelineClip>)[]{("Move Start to Playhead",c=>c with{Start=_timelineTime}),("Move End to Playhead",c=>c with{Start=System.Math.Max(0,_timelineTime-c.Duration)}),("Trim Start at Playhead",c=>TimelineEdits.Trim(c,_timelineTime,c.End)),("Trim End at Playhead",c=>TimelineEdits.Trim(c,c.Start,_timelineTime))})trim.Items.Add(TypeItem(title,()=>{if(ActiveClip is { } c)ChangeTimeline(title,v=>TimelineEdits.Replace(v,action(c)));},enabled:ActiveClip is not null));
        var convert=Sub("Convert Frames");convert.Items.Add(TypeItem("Convert to Frame Animation",()=>{if(_document is { } doc)ChangeTimeline("Convert to Frame Animation",v=>TimelineEdits.ConvertToFrames(doc,v));},enabled:t.Mode==TimelineMode.Video));convert.Items.Add(TypeItem("Convert to Video Timeline",()=>ChangeTimeline("Convert to Video Timeline",TimelineEdits.ConvertToVideo),enabled:t.Mode==TimelineMode.Frames));
        convert.Items.Add(TypeItem("Make Frames from Layers",()=>{if(_document is { } doc)ChangeTimeline("Make Frames from Layers",v=>{var frames=doc.Layers.Where(l=>!l.IsGroup).Select(layer=>{using var clone=doc.Clone();foreach(var other in clone.Layers.Where(l=>!l.IsGroup))other.IsVisible=other.ID==layer.ID;return new TimelineFrame(System.Guid.NewGuid(),.1,LayerComps.Capture(clone,layer.Name));}).ToImmutableArray();return v with{Mode=TimelineMode.Frames,Frames=frames,WorkStart=0,WorkEnd=frames.Sum(f=>f.Duration)};});}));
        var area=Sub("Work Area");area.Items.Add(TypeItem("Set Start at Playhead",()=>ChangeTimeline("Work Area",v=>v with{WorkStart=System.Math.Min(_timelineTime,v.WorkEnd-1/v.FrameRate)})));area.Items.Add(TypeItem("Set End at Playhead",()=>ChangeTimeline("Work Area",v=>v with{WorkEnd=System.Math.Max(v.WorkStart+1/v.FrameRate,_timelineTime+1/v.FrameRate)})));area.Items.Add(TypeItem("Extract Work Area",()=>ChangeTimeline("Extract Work Area",v=>v with{Clips=v.Clips.Where(c=>c.End>v.WorkStart&&c.Start<v.WorkEnd).Select(c=>TimelineEdits.Trim(c,v.WorkStart,v.WorkEnd) with{Start=System.Math.Max(0,c.Start-v.WorkStart)}).ToImmutableArray(),WorkEnd=v.WorkEnd-v.WorkStart,WorkStart=0,Audio=v.Audio.Where(a=>a.Start+a.Duration>v.WorkStart&&a.Start<v.WorkEnd).Select(a=>a with{Start=System.Math.Max(0,a.Start-v.WorkStart),SourceStart=a.SourceStart+System.Math.Max(0,v.WorkStart-a.Start),Duration=System.Math.Min(a.Start+a.Duration,v.WorkEnd)-System.Math.Max(a.Start,v.WorkStart)}).ToImmutableArray(),Notes=v.Notes.Where(n=>n.Time>=v.WorkStart&&n.Time<v.WorkEnd).Select(n=>n with{Time=n.Time-v.WorkStart}).ToImmutableArray()}),enabled:t.Mode==TimelineMode.Video));area.Items.Add(TypeItem("Reset Work Area",()=>ChangeTimeline("Work Area",v=>v with{WorkStart=0,WorkEnd=v.Duration})));
        menu.Items.Add(TypeItem("Relink Media…",()=>_ = RelinkTimelineMedia(),enabled:ActiveClip?.SourceFile is not null));
        menu.Items.Add(TypeItem("Delete Timeline",()=>{if(_document is { } doc)Edit("Delete Timeline",()=>{doc.Timeline=null;return true;});}));menu.Items.Add(new Separator());
        var keys=Sub("Keyframes");keys.Items.Add(TypeItem("Add Keyframe",AddTimelineKey,enabled:ActiveClip is not null));
        keys.Items.Add(TypeItem("Delete Keyframe",()=>{if(ActiveClip is { } c)ChangeTimeline("Delete Keyframe",v=>TimelineEdits.Replace(v,c with{Keys=c.Keys.Where(k=>System.Math.Abs(c.Start+k.Time-_timelineTime)>.5/v.FrameRate).ToImmutableArray()}));},enabled:ActiveClip is not null));
        foreach(var mode in System.Enum.GetValues<KeyInterpolation>())keys.Items.Add(TypeItem(mode.ToString(),()=>{if(ActiveClip is { } c)ChangeTimeline("Keyframe Interpolation",v=>TimelineEdits.Replace(v,c with{Keys=c.Keys.Select(k=>System.Math.Abs(c.Start+k.Time-_timelineTime)<=.5/v.FrameRate?k with{Interpolation=mode}:k).ToImmutableArray()}));},enabled:ActiveClip is not null));
        var notes=Sub("Comments");notes.Items.Add(TypeItem("Edit Timeline Comment…",()=>_ = TimelineComment()));notes.Items.Add(TypeItem("Export as HTML…",()=>_ = ExportTimelineComments(true)));notes.Items.Add(TypeItem("Export as Text…",()=>_ = ExportTimelineComments(false)));
        menu.Items.Add(new Separator());menu.Items.Add(TypeItem("Loop Playback",()=>ChangeTimeline("Loop Playback",v=>v with{Loop=!v.Loop}),t.Loop));menu.Items.Add(TypeItem("Allow Frame Skipping",()=>ChangeTimeline("Allow Frame Skipping",v=>v with{AllowFrameSkipping=!v.AllowFrameSkipping}),t.AllowFrameSkipping));
        var display=Sub("Display");var filters=new[]{"All Clips","Video Groups Only","Favorite Clips Only"};for(var i=0;i<filters.Length;i++){var at=i;display.Items.Add(TypeItem(filters[i],()=>ChangeTimeline("Timeline Display",v=>v with{DisplayFilter=at}),t.DisplayFilter==i));}display.Items.Add(TypeItem("Comment Track",()=>ChangeTimeline("Comment Track",v=>v with{ShowNotes=!v.ShowNotes}),t.ShowNotes));
        menu.Items.Add(TypeItem("Enable Timeline Shortcuts",()=>ChangeTimeline("Timeline Shortcuts",v=>v with{Shortcuts=!v.Shortcuts}),t.Shortcuts));menu.Items.Add(TypeItem("Auto Group Clips",()=>ChangeTimeline("Auto Group Clips",v=>v with{AutoGroup=!v.AutoGroup}),t.AutoGroup));
        menu.Items.Add(TypeItem("Enable Onion Skin",()=>ChangeTimeline("Enable Onion Skin",v=>v with{OnionSkin=!v.OnionSkin}),t.OnionSkin));
        menu.Items.Add(TypeItem("Onion Skin Settings…",async()=>{var form=new FormDialog("Onion Skin Settings…",430,220){MinHeight=170};var opacity=form.Number("Onion Skin Opacity (%)",t.OnionOpacity*100,0,100);if(await form.ShowDialog<bool>(this))ChangeTimeline("Onion Skin Settings…",v=>v with{OnionOpacity=(double)opacity.Value!/100});}));
        menu.Items.Add(TypeItem("Timeline Frame Rate…",()=>_ = TimelineFrameRate()));menu.Items.Add(TypeItem("Panel Options…",()=>_ = TimelinePanelOptions()));menu.Items.Add(TypeItem("Render Video…",()=>_ = RenderTimeline()));
        menu.Items.Add(new Separator());menu.Items.Add(TypeItem("Close",()=>{_stopDocumentTimeline?.Invoke();HidePanel(_dockPanels["Timeline"]);}));menu.Items.Add(TypeItem("Close Tab Group",()=>{_stopDocumentTimeline?.Invoke();if(_dockPanels["Timeline"].Group is { } group)foreach(var entry in Members(group).ToArray())HidePanel(entry);}));
    }
    private async Task TimelineGoTo(){if(_document?.Timeline is not { } t)return;var form=new FormDialog("Go To Time",420,220);var time=form.Number("Seconds",_timelineTime,0,t.Duration);if(await form.ShowDialog<bool>(this))_seekDocumentTimeline?.Invoke((double)time.Value!);}
    private async Task TimelinePanelOptions()
    {
        if(_document is not {Timeline:{ } t} doc)return;var form=new TimelinePanelDialog(doc,t);
        if(await form.ShowDialog<bool>(this)&&doc==_document)ChangeTimeline("Animation Panel Options",v=>v with{ThumbnailSize=form.ThumbnailSize,FrameNumbers=form.FrameNumbers});
    }
    private async Task TimelineComment()
    {
        if(_document is not {Timeline:{ } t} doc)return;var at=TimelineEdits.Snap(_timelineTime,t.FrameRate);var existing=t.Notes.FirstOrDefault(n=>System.Math.Abs(n.Time-at)<.5/t.FrameRate);var form=new FormDialog("Timeline Comment",500,330);var text=form.Text("Comment",existing?.Text??"",true);form.Validate=()=>text.Text?.Length>10000?"Comment is too long.":null;
        if(await form.ShowDialog<bool>(this)&&doc==_document)ChangeTimeline("Timeline Comment",v=>v with{Notes=v.Notes.Where(n=>n.ID!=existing?.ID).Concat(string.IsNullOrWhiteSpace(text.Text)?[]:new[]{new TimelineNote(existing?.ID??System.Guid.NewGuid(),at,text.Text!)}).OrderBy(n=>n.Time).ToImmutableArray()});
    }
    private async Task ExportTimelineComments(bool html)
    {
        if(_document?.Timeline is not { } t)return;var file=await StorageProvider.SaveFilePickerAsync(new(){SuggestedFileName="timeline-comments."+(html?"html":"txt"),DefaultExtension=html?"html":"txt"});if(file?.TryGetLocalPath() is not { } path)return;
        try{var lines=t.Notes.OrderBy(n=>n.Time).Select(n=>TimelineEdits.Timecode(n.Time,t.FrameRate)+"  "+n.Text);await System.IO.File.WriteAllTextAsync(path,html?"<!doctype html><meta charset=\"utf-8\"><title>Timeline comments</title><pre>"+System.Net.WebUtility.HtmlEncode(string.Join("\n",lines))+"</pre>":string.Join("\n",lines));}catch(System.Exception e){Say(e.Message);}
    }
}
