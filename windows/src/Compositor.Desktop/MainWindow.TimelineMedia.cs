using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task AddTimelineMedia()
    {
        if(_document is not {Timeline:{ } timeline} doc)return;
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title=Localize.Text("Add Media…"),FileTypeFilter=[new("Images and video"){Patterns=["*.png","*.jpg","*.jpeg","*.gif","*.webp","*.tif","*.mp4","*.mov","*.mkv","*.webm"]}]});
        if(files.FirstOrDefault()?.TryGetLocalPath() is not { } path)return;var revision=_history.CurrentRevision;
        ImportedImage? asset=null;
        try
        {
            var video=new[]{".mp4",".mov",".mkv",".webm"}.Contains(Path.GetExtension(path).ToLowerInvariant());TimelineMediaInfo? info=null;
            asset=await Task.Run(()=>{if(video){info=TimelineMedia.Inspect(path);return ImportedImage.Create(TimelineMedia.VideoFrame(path,0),Path.GetFileNameWithoutExtension(path));}return ImageImporter.Decode(path);});
            if(doc!=_document||revision!=_history.CurrentRevision){asset.Dispose();return;}
            var imported=asset;var factor=Math.Min(1,Math.Min((double)doc.Width/asset.Image.Width,(double)doc.Height/asset.Image.Height));
            var layer=new ImageLayer(Guid.NewGuid(),asset,new LayerTransform(0,0,asset.Image.Width*factor,asset.Image.Height*factor),Path.GetFileNameWithoutExtension(path));
            if(asset.Animation is { } gif)layer.Gif=gif;
            var length=info?.Duration??layer.Gif?.Frames.Sum(f=>layer.Gif.Delay(f.Index))??5;
            var clip=new TimelineClip{LayerID=layer.ID,Start=_timelineTime,Duration=Math.Min(length,DocumentTimeline.MaxDuration-_timelineTime),SourceFile=video?path:null};
            Edit("Add Timeline Media",()=>
            {
                if(timeline.AutoGroup&&PropertyLayer?.ParentID is { } parent)layer.ParentID=parent;
                doc.Layers.Add(layer);doc.Timeline=TimelineEdits.Replace(timeline,clip) with{WorkEnd=Math.Max(timeline.WorkEnd,clip.End)};return true;
            });asset=null;_timelineClip=clip.ID;Reselect(layer.ID);
        }
        catch(Exception error){asset?.Dispose();Say(error.Message);}
    }
    private async Task RelinkTimelineMedia()
    {
        if(ActiveClip is not {SourceFile:not null} clip||_document is not { } doc)return;
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title=Localize.Text("Relink Media…"),FileTypeFilter=[new("Video"){Patterns=["*.mp4","*.mov","*.mkv","*.webm"]}]});
        if(files.FirstOrDefault()?.TryGetLocalPath() is not { } path)return;
        try{var info=await Task.Run(()=>TimelineMedia.Inspect(path));if(info.Width<=0)throw new InvalidDataException("The file has no video stream.");if(doc==_document)ChangeTimeline("Relink Media…",t=>TimelineEdits.Replace(t,clip with{SourceFile=path}));}catch(Exception error){Say(error.Message);}
    }
    private async Task TimelineAudioSettings()
    {
        if(_document is not {Timeline:{ } timeline} doc)return;
        var form=new FormDialog("Audio Track",620,520);var enabled=form.Check("Enable Audio",timeline.AudioEnabled);var tracks=timeline.Audio.ToList();
        var list=new ListBox{ItemsSource=tracks.Select(a=>Path.GetFileName(a.File)).ToArray(),MinHeight=80,SelectedIndex=tracks.Count>0?0:-1};form.Fields.Children.Add(list);
        var start=form.Number("Start (seconds)",0,0,DocumentTimeline.MaxDuration);var offset=form.Number("Source Offset",0,0,DocumentTimeline.MaxDuration);var duration=form.Number("Duration (seconds)",1,.01,DocumentTimeline.MaxDuration);var volume=form.Number("Volume (%)",100,0,400);var muted=form.Check("Mute",false);var loading=false;var selected=-1;
        void Store(){if(loading||selected<0||selected>=tracks.Count)return;tracks[selected]=tracks[selected] with{Start=(double)start.Value!,SourceStart=(double)offset.Value!,Duration=(double)duration.Value!,Volume=(double)volume.Value!/100,Muted=muted.IsChecked==true};}
        void Load(){loading=true;selected=list.SelectedIndex;if(selected>=0&&selected<tracks.Count){var a=tracks[selected];start.Value=(decimal)a.Start;offset.Value=(decimal)a.SourceStart;duration.Value=(decimal)a.Duration;volume.Value=(decimal)a.Volume*100;muted.IsChecked=a.Muted;}loading=false;}
        list.SelectionChanged+=(_,_)=>Load();form.Changed=Store;
        form.Fields.Children.Add(PropertyPair(PropertyAction("Add Audio…",async()=>
        {
            var files=await form.StorageProvider.OpenFilePickerAsync(new(){Title=Localize.Text("Add Audio…"),FileTypeFilter=[new("Audio"){Patterns=["*.wav","*.mp3","*.aac","*.m4a","*.flac","*.ogg"]}]});
            if(files.FirstOrDefault()?.TryGetLocalPath() is not { } path)return;
            try{var info=await Task.Run(()=>TimelineMedia.Inspect(path));if(!info.Audio)throw new InvalidDataException("The file has no audio stream.");tracks.Add(new(Guid.NewGuid(),path,_timelineTime,info.Duration));list.ItemsSource=tracks.Select(a=>Path.GetFileName(a.File)).ToArray();list.SelectedIndex=tracks.Count-1;}catch(Exception e){Say(e.Message);}
        }),PropertyAction("Delete Audio",()=>{if(list.SelectedIndex is var i&&i>=0){tracks.RemoveAt(i);list.ItemsSource=tracks.Select(a=>Path.GetFileName(a.File)).ToArray();list.SelectedIndex=Math.Min(i,tracks.Count-1);}})));
        form.Validate=()=>tracks.Count>128||tracks.Any(a=>!a.IsValid)?"Invalid audio timing.":null;Load();
        if(await form.ShowDialog<bool>(this)&&doc==_document)ChangeTimeline("Audio Track",t=>t with{Audio=System.Collections.Immutable.ImmutableArray.CreateRange(tracks),AudioEnabled=enabled.IsChecked==true,WorkEnd=Math.Max(t.WorkEnd,tracks.Select(a=>a.Start+a.Duration).DefaultIfEmpty(0).Max())});
    }
    private async Task RenderTimeline()
    {
        _stopDocumentTimeline?.Invoke();if(_document is not {Timeline:{ } timeline} doc)return;
        var exporting=_open;var rendered=false;
        var form=new TimelineExportDialog(doc,timeline,_open.Name);
        if(!await form.ShowDialog<bool>(this)||doc!=_document)return;
        var path=form.Destination;
        if(File.Exists(path)&&!await ConfirmDialog.Ask(this,"Replace File",Localize.Text("The output file already exists. Replace it?"),"Replace","Cancel"))return;
        var first=form.From;var last=form.To;
        try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);}catch(Exception error){Say(error.Message);return;}
        using var cancellation=new CancellationTokenSource();using var lease=new DocumentLease(doc);
        var progress=new ProgressBar{Minimum=0,Maximum=1,Height=14,Margin=new Thickness(20)};var status=new TextBlock{Text=Localize.Text("Rendering Video…"),Margin=new Thickness(20,8)};var cancel=FormDialog.ActionButton("Cancel",cancellation.Cancel);var dialog=new TimelineProgressDialog{Title=Localize.Text("Render Video"),Width=430,Height=185,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new StackPanel{Children={status,progress,cancel}}};
        dialog.Closing+=(_,_)=>cancellation.Cancel();dialog.Show(this);
        try{if(!form.ColorManaged)lease.Document.Image=lease.Document.Image with{ProfileEncoding=null};await Task.Run(()=>TimelineMedia.Export(lease.Document,timeline,path,form.OutputWidth,form.OutputHeight,form.Rate,first,last,form.Quality,form.IncludeAudio,p=>Dispatcher.UIThread.Post(()=>{progress.Value=p;status.Text=$"{Localize.Text("Rendering Video…")} {p:P0}";}),cancellation.Token));Say("Video rendered.");rendered=true;}
        catch(OperationCanceledException){Say("Video rendering canceled.");}catch(Exception error){Say(error.Message);}finally{dialog.Close();}
        // After the progress window has gone, so an event script's notice is not hidden behind it.
        if(rendered)await RaiseScriptEvent(ScriptEvent.ExportDocument,exporting);
    }
}

internal sealed class TimelineProgressDialog : DialogWindow { }
