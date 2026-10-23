using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;
namespace Compositor.Core.IO;

public readonly record struct GifExportFrame(int Start, int Sample, int Ticks);
public static class GifExport
{
    /// <summary>Merge independent frame clocks. Shorter tracks hold their last frame, matching upstream.</summary>
    public static IReadOnlyList<GifExportFrame> Schedule(IEnumerable<GifAnimation> animations)
    {
        var boundaries = new SortedSet<int> { 0 }; var end = 0;
        foreach (var animation in animations)
        {
            var time = 0; foreach (var frame in animation.Frames) { boundaries.Add(time); time = checked(time + (int)Math.Round(animation.Delay(frame.Index) * 100)); } end = Math.Max(end,time);
        }
        var frames = new List<GifExportFrame>(); var start = 0; var sample = 0;
        void Append(int boundary)
        {
            while(start<boundary)
            {
                if(frames.Count>=GifSource.MaxFrames)throw new InvalidOperationException("Export contains too many GIF frames.");
                var ticks=Math.Min(65535,boundary-start);if(boundary-start-ticks==1)ticks--;
                frames.Add(new(start,Math.Max(start,sample),ticks));start+=ticks;sample=start;
            }
        }
        foreach(var next in boundaries.Where(t=>t>0&&t<end))
        {
            if(next<=start)continue;
            if(next-start>=2)Append(next);else if(end-start>=4)Append(start+2);else sample=next;
        }
        Append(end == 0 ? 10 : end);return frames;
    }
    public static int FrameAt(GifAnimation animation,int ticks)
    {
        var elapsed=0;var frames=animation.Frames;
        foreach(var frame in frames){elapsed+=(int)Math.Round(animation.Delay(frame.Index)*100);if(ticks<elapsed)return frame.Index;}return frames[^1].Index;
    }
    public static ExportResult Encode(CanvasDocument document,ExportOptions options,CancellationToken cancellation=default)
    {
        var visible=document.EffectiveVisibleIDs();var tracks=document.Layers.Where(l=>l.Gif is not null&&visible.Contains(l.ID)).ToArray();var schedule=Schedule(tracks.Select(l=>l.Gif!));
        // Q16-HDRI native frames use substantially more memory than their encoded GIF representation.
        using var budget=MemoryBudget.Current.Reserve(checked((long)options.Width*options.Height*schedule.Count*32));
        using var view=document.Clone();using var collection=new MagickImageCollection();SKBitmap? preview=null;
        try
        {
            foreach(var frame in schedule)
            {
                cancellation.ThrowIfCancellationRequested();var decoded=new List<ImportedImage>();
                try
                {
                    foreach(var track in tracks)
                    {
                        var layer=view.Layers.Single(l=>l.ID==track.ID);var animation=track.Gif!;var index=FrameAt(animation,frame.Sample);layer.ShowGifFrame(index);if(!animation.Edited.ContainsKey(index))decoded.Add(layer.Asset!);
                    }
                    using var rendered=DocumentRenderer.Render(view);
                    using var resized=Bitmaps.Allocate(Bitmaps.ColorInfo(options.Width,options.Height));
                    using(var canvas=new SKCanvas(resized)){canvas.Clear(options.Transparency?SKColors.Transparent:options.Background??SKColors.White);using var image=SKImage.FromBitmap(rendered);canvas.DrawImage(image,SKRect.Create(options.Width,options.Height),new SKSamplingOptions(SKFilterMode.Linear));}
                    if(preview is null)preview=Bitmaps.Thumbnail(resized);
                    var imageFrame=new MagickImage(PngCodec.Encode(resized)){AnimationTicksPerSecond=100,AnimationDelay=(uint)frame.Ticks,AnimationIterations=0,GifDisposeMethod=GifDisposeMethod.Background};collection.Add(imageFrame);
                }
                finally{foreach(var image in decoded)image.Dispose();}
            }
            using var stream=new MemoryStream();collection.Write(stream,MagickFormat.Gif);return new ExportResult(stream.ToArray(),preview!);
        }
        catch{preview?.Dispose();throw;}
    }
}
