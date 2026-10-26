using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

internal sealed class TimelineTracks : Control
{
    internal CanvasDocument? Document;
    internal DocumentTimeline? Timeline;
    internal double Time;
    internal double Scale = 90;
    internal Guid? Selected;
    internal Action<double>? Seek;
    internal Action<Guid>? SelectClip;
    internal Action<TimelineClip>? ChangeClip;
    internal Action<Guid>? EditClip;
    internal Action<Guid>? AddMedia;
    internal Action<int>? SelectFrame;
    private TimelineClip? _drag;
    private TimelineClip? _draft;
    private double _down;
    private int _edge;
    private bool _seeking;
    private readonly Dictionary<Guid, Avalonia.Media.Imaging.WriteableBitmap> _thumbnails = [];
    private const double Header = 225, Ruler = 35, Row = 58;
    private ImageLayer[] Rows => Document?.Layers.Where(l => !l.IsGroup && (Timeline?.DisplayFilter != 2 || Timeline.Clips.Any(c => c.LayerID == l.ID && c.Favorite)) && (Timeline?.DisplayFilter != 1 || l.ParentID is not null)).Reverse().ToArray() ?? [];
    internal void Update(CanvasDocument? document, DocumentTimeline? timeline)
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        Document = document; Timeline = timeline;
        foreach (var bitmap in _thumbnails.Values) bitmap.Dispose(); _thumbnails.Clear();
        if (timeline?.ThumbnailSize > 0)
            foreach (var layer in Rows) if ((layer.Asset?.Image ?? layer.Text?.Image ?? layer.Shape?.Image) is { } pixels) { using var thumb = Compositor.Core.Model.Bitmaps.Thumbnail(pixels); _thumbnails[layer.ID] = CanvasView.ToImage(thumb); }
        Width = Math.Max(780, Header + (timeline?.Duration ?? 5) * Scale + 60);
        Height = timeline?.Mode == TimelineMode.Frames ? 130 : Ruler + Row * Rows.Length + 50;
        InvalidateVisual();
    }
    public override void Render(DrawingContext ctx)
    {
        ctx.DrawRectangle(Skin.ChromeBrush, null, new Rect(Bounds.Size));
        if (Timeline is not { } timeline) return;
        void Text(string text, double x, double y, double width = 500) => ctx.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Skin.LabelBrush) { MaxTextWidth = Math.Max(1,width), Trimming = TextTrimming.CharacterEllipsis }, new Point(x,y));
        if (timeline.Mode == TimelineMode.Frames)
        {
            var x = 5d; var time = 0d;
            for (var i = 0; i < timeline.Frames.Length; i++)
            {
                var frame = timeline.Frames[i]; var active = Time >= time && Time < time + frame.Duration;
                ctx.DrawRectangle(active ? Skin.SurfaceControlHoverBrush : Skin.SurfaceDarkBrush, new Pen(active ? Skin.AccentBrush : Skin.BorderControlBrush,active ? 2 : 1),new Rect(x,8,90,95),3,3);
                Text((i+1).ToString(),x+8,16); Text(frame.State.Name,x+8,40,75); Text($"{frame.Duration:0.###} s",x+8,75);
                x+=98; time+=frame.Duration;
            }
            Width = Math.Max(780,x); return;
        }
        var step = Scale >= 100 ? .5 : Scale >= 35 ? 1 : 5;
        for (var t = 0d; t <= timeline.Duration; t += step)
        {
            var x = Header+t*Scale; ctx.DrawLine(new Pen(Skin.BorderControlBrush,1),new Point(x,23),new Point(x,Height));
            Text(TimelineEdits.Timecode(t,timeline.FrameRate,timeline.FrameNumbers),x+4,5);
        }
        ctx.DrawRectangle(new SolidColorBrush(Skin.Accent, .3),null,new Rect(Header+timeline.WorkStart*Scale,25,Math.Max(1,(timeline.WorkEnd-timeline.WorkStart)*Scale),8));
        var rows = Rows;
        for (var i = 0; i < rows.Length; i++)
        {
            var layer = rows[i]; var y = Ruler+i*Row;
            ctx.DrawRectangle(Skin.SurfaceDarkBrush,new Pen(Skin.BorderControlBrush,1),new Rect(0,y,Header,Row)); Text(layer.Name,14,y+20,Header-48);
            Text("+",Header-25,y+18);
            foreach (var actual in timeline.Clips.Where(c=>c.LayerID==layer.ID))
            {
                var clip = _draft?.ID==actual.ID ? _draft : actual;
                var rect = new Rect(Header+clip.Start*Scale,y+4,Math.Max(4,clip.Duration*Scale),Row-8);
                ctx.DrawRectangle(Skin.AccentBrush,new Pen(clip.ID==Selected ? Brushes.White : Skin.BorderControlBrush,clip.ID==Selected ? 2 : 1),rect,3,3);
                using (ctx.PushClip(rect))
                {
                    var inset = 8d;
                    if (_thumbnails.TryGetValue(layer.ID,out var thumb)) { var w = timeline.ThumbnailSize*22;ctx.DrawImage(thumb,new Rect(0,0,thumb.PixelSize.Width,thumb.PixelSize.Height),new Rect(rect.X+4,rect.Y+6,w,35));inset+=w; }
                    Text(layer.Name,rect.X+inset,rect.Y+5,Math.Max(1,rect.Width-inset));
                    foreach(var key in clip.Keys) Text("◆",Header+(clip.Start+key.Time)*Scale-4,y+32,16);
                    if(clip.FadeIn>0) ctx.DrawLine(new Pen(Brushes.White,1),rect.BottomLeft,new Point(rect.X+clip.FadeIn*Scale,rect.Top));
                    if(clip.FadeOut>0) ctx.DrawLine(new Pen(Brushes.White,1),new Point(rect.Right-clip.FadeOut*Scale,rect.Top),rect.BottomRight);
                }
            }
        }
        var audioY=Ruler+rows.Length*Row;Text(Localize.Text("Audio Track"),12,audioY+14);
        foreach(var audio in timeline.Audio) { var rect=new Rect(Header+audio.Start*Scale,audioY+5,audio.Duration*Scale,32);ctx.DrawRectangle(Skin.SurfaceControlHoverBrush,null,rect,3,3);Text(System.IO.Path.GetFileName(audio.File),rect.X+6,rect.Y+8,Math.Max(1,rect.Width-10)); }
        if(timeline.ShowNotes) foreach(var note in timeline.Notes) Text("◆",Header+note.Time*Scale-4,18,18);
        var playhead=Header+Time*Scale;ctx.DrawLine(new Pen(Brushes.IndianRed,2),new Point(playhead,23),new Point(playhead,Height));Text("▼",playhead-6,13,18);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if(Timeline is not { } timeline)return; Focus();var p=e.GetPosition(this);
        if(timeline.Mode==TimelineMode.Frames){SelectFrame?.Invoke(Math.Clamp((int)(p.X/98),0,Math.Max(0,timeline.Frames.Length-1)));return;}
        if(p.X<Header){var i=(int)((p.Y-Ruler)/Row);if(i>=0&&i<Rows.Length&&p.X>Header-35)AddMedia?.Invoke(Rows[i].ID);return;}
        if(p.Y<Ruler){_seeking=true;Seek?.Invoke(Math.Clamp((p.X-Header)/Scale,0,timeline.Duration));e.Pointer.Capture(this);return;}
        var row=(int)((p.Y-Ruler)/Row);if(row<0||row>=Rows.Length)return;
        var time=(p.X-Header)/Scale;var clip=timeline.Clips.LastOrDefault(c=>c.LayerID==Rows[row].ID&&time>=c.Start&&time<=c.End);if(clip is null)return;
        Selected=clip.ID;SelectClip?.Invoke(clip.ID);InvalidateVisual();
        if(e.ClickCount==2){EditClip?.Invoke(clip.ID);return;}
        _drag=clip;_down=p.X;_edge=p.X-(Header+clip.Start*Scale)<7?-1:(Header+clip.End*Scale)-p.X<7?1:0;e.Pointer.Capture(this);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(Timeline is not { } timeline)return;var p=e.GetPosition(this);
        if(_seeking){Seek?.Invoke(Math.Clamp((p.X-Header)/Scale,0,timeline.Duration));return;}
        if(_drag is not { } clip)return;var delta=TimelineEdits.Snap(Math.Abs(p.X-_down)/Scale,timeline.FrameRate)*Math.Sign(p.X-_down);
        _draft=_edge==0?clip with{Start=Math.Clamp(clip.Start+delta,0,DocumentTimeline.MaxDuration-clip.Duration)}:_edge<0?TimelineEdits.Trim(clip,clip.Start+delta,clip.End):TimelineEdits.Trim(clip,clip.Start,clip.End+delta);InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);var draft=_draft;_draft=null;_drag=null;_seeking=false;e.Pointer.Capture(null);if(draft is not null)ChangeClip?.Invoke(draft);InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);_draft=null;_drag=null;_seeking=false;InvalidateVisual();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){foreach(var bitmap in _thumbnails.Values)bitmap.Dispose();_thumbnails.Clear();base.OnDetachedFromVisualTree(e);}
}
