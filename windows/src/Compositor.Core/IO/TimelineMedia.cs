using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record TimelineMediaInfo(int Width, int Height, double Duration, bool Audio);

/// <summary>Optional FFmpeg bridge. Files are local linked media; no shell or network inputs are accepted.</summary>
public static class TimelineMedia
{
    private static string N(double value) => value.ToString("0.########",CultureInfo.InvariantCulture);
    private static Process Start(string program,IEnumerable<string> arguments,bool input=false)
    {
        var info=new ProcessStartInfo(program){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        try{return Process.Start(info)??throw new IOException("Could not start FFmpeg.");}
        catch(System.ComponentModel.Win32Exception){throw new NotSupportedException("Install FFmpeg and ffprobe on PATH to import video or audio and render video.");}
    }
    private static string Local(string file)
    {
        if(!Path.IsPathFullyQualified(file)||!File.Exists(file))throw new FileNotFoundException("Linked media is missing. Relink the clip before playback or export.",file);
        return Path.GetFullPath(file);
    }
    private static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} }
    public static TimelineMediaInfo Inspect(string path)
    {
        using var process=Start("ffprobe",["-v","error","-protocol_whitelist","file,pipe","-show_streams","-show_format","-of","json",Local(path)]);
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(20000)){Kill(process);throw new IOException("Media inspection timed out.");}
        if(process.ExitCode!=0)throw new IOException(error.GetAwaiter().GetResult());
        using var json=JsonDocument.Parse(output.GetAwaiter().GetResult());var streams=json.RootElement.GetProperty("streams").EnumerateArray().ToArray();var video=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="video");
        var width=video.ValueKind==JsonValueKind.Undefined?0:video.GetProperty("width").GetInt32();var height=video.ValueKind==JsonValueKind.Undefined?0:video.GetProperty("height").GetInt32();
        var duration=double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
        if(!double.IsFinite(duration)||duration<=0||duration>DocumentTimeline.MaxDuration||width>16384||height>16384)throw new InvalidDataException("Unsupported media dimensions or duration.");
        return new(width,height,duration,streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"));
    }
    public static SKBitmap VideoFrame(string path,double seconds,int maxSize=2048,CancellationToken cancellation=default)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        if(maxSize is < 1 or > 8192)throw new ArgumentOutOfRangeException(nameof(maxSize));
        using var process=Start("ffmpeg",["-v","error","-protocol_whitelist","file,pipe","-ss",N(seconds),"-i",Local(path),"-an","-frames:v","1","-vf",$"scale={maxSize}:{maxSize}:force_original_aspect_ratio=decrease","-f","image2pipe","-vcodec","png","pipe:1"]);
        using var cancel=cancellation.Register(()=>Kill(process));var error=process.StandardError.ReadToEndAsync();using var buffer=new MemoryStream();var copying=process.StandardOutput.BaseStream.CopyToAsync(buffer,cancellation);
        if(!process.WaitForExit(30000)){Kill(process);throw new IOException("Video frame decoding timed out.");}copying.GetAwaiter().GetResult();cancellation.ThrowIfCancellationRequested();
        if(process.ExitCode!=0)throw new IOException(error.GetAwaiter().GetResult());
        return SKBitmap.Decode(buffer.ToArray())??throw new InvalidDataException("The video has no frame at this time.");
    }
    public static TimelinePreview Preview(CanvasDocument document,DocumentTimeline timeline,double time,int maxVideoSize=2048,CancellationToken cancellation=default,bool onion=false)
    {
        var view=document.Clone();var owned=new List<ImportedImage>();
        try
        {
            TimelineEdits.Evaluate(view,timeline,time);
            foreach(var layer in view.Layers.Where(l=>l.IsVisible))
            {
                cancellation.ThrowIfCancellationRequested();var clip=timeline.Clips.LastOrDefault(c=>c.LayerID==layer.ID&&time>=c.Start&&time<c.End);
                var sourceTime=clip is null?time:clip.SourceStart+(time-clip.Start)*clip.Speed;
                if(clip?.SourceFile is { } file){var asset=ImportedImage.Create(VideoFrame(file,sourceTime,maxVideoSize,cancellation),layer.Name);layer.Asset=asset;owned.Add(asset);}
                else if(layer.Gif is { } gif){var index=GifExport.FrameAt(gif,(int)Math.Round(sourceTime*100));var asset=gif.Decode(index,layer.Name);layer.ShowGifFrame(index,asset);if(!gif.Edited.ContainsKey(index))owned.Add(asset);}
            }
            if(onion && timeline.OnionSkin && time > 0)
            {
                var previousTime = timeline.Mode == TimelineMode.Frames
                    ? timeline.Frames.Take(Math.Max(0, TimelineEdits.FrameAt(timeline, time) - 1)).Sum(f => f.Duration)
                    : Math.Max(0, time - 1 / timeline.FrameRate);
                using var previous = Preview(document, timeline, previousTime, maxVideoSize, cancellation);
                previous.Document.Image = previous.Document.Image with { ProfileEncoding = null };
                var ghost = ImportedImage.Create(DocumentRenderer.Render(previous.Document), "Onion Skin"); owned.Add(ghost);
                view.Layers.Add(new(Guid.NewGuid(), ghost, new(0, 0, view.Width, view.Height), "Onion Skin") { Opacity = timeline.OnionOpacity });
            }
            return new(view,owned);
        }
        catch{view.Dispose();foreach(var asset in owned)asset.Dispose();throw;}
    }
    public static void Export(CanvasDocument document,DocumentTimeline timeline,string destination,int width,int height,double rate,double from,double to,int quality=18,bool audio=true,Action<double>? progress=null,CancellationToken cancellation=default)
    {
        if(!timeline.IsValid||width<2||height<2||width>8192||height>8192||rate is <1 or >120||!double.IsFinite(from+to+rate)||from<0||to<=from||to>timeline.Duration||quality is <0 or >51)throw new ArgumentException("Invalid video export settings.");
        width=(width+1)/2*2;height=(height+1)/2*2;var count=(int)Math.Ceiling((to-from)*rate);
        var full=Path.GetFullPath(destination);var temporary=Path.Combine(Path.GetDirectoryName(full)!,"."+Path.GetFileName(full)+"."+Guid.NewGuid().ToString("N")+".tmp");
        var arguments=new List<string>{"-v","error","-y","-f","rawvideo","-pixel_format","rgba","-video_size",$"{width}x{height}","-framerate",N(rate),"-i","pipe:0"};
        var tracks=audio&&timeline.AudioEnabled?timeline.Audio.Where(a=>!a.Muted&&a.Start<to&&a.Start+a.Duration>from).ToArray():[];
        foreach(var track in tracks)arguments.AddRange(["-protocol_whitelist","file,pipe","-i",Local(track.File)]);
        if(tracks.Length>0)
        {
            var filters=new List<string>();
            for(var i=0;i<tracks.Length;i++){var a=tracks[i];var skip=Math.Max(0,from-a.Start);var length=Math.Min(a.Duration-skip,to-Math.Max(from,a.Start));var delay=Math.Max(0,a.Start-from)*1000;filters.Add($"[{i+1}:a]atrim=start={N(a.SourceStart+skip)}:duration={N(length)},asetpts=PTS-STARTPTS,volume={N(a.Volume)},adelay={N(delay)}:all=1[a{i}]");}
            filters.Add(string.Concat(Enumerable.Range(0,tracks.Length).Select(i=>$"[a{i}]"))+$"amix=inputs={tracks.Length}:normalize=0[audio]");arguments.AddRange(["-filter_complex",string.Join(';',filters),"-map","0:v","-map","[audio]","-c:a","aac","-b:a","192k"]);
        }
        else arguments.Add("-an");
        arguments.AddRange(["-c:v","libx264","-crf",quality.ToString(),"-vf","scale=out_color_matrix=bt709:out_range=tv","-colorspace","bt709","-color_primaries","bt709","-color_trc","iec61966-2-1","-pix_fmt","yuv420p","-t",N(to-from),"-movflags","+faststart","-f","mp4",temporary]);
        try
        {
            using var process=Start("ffmpeg",arguments,true);using var cancel=cancellation.Register(()=>Kill(process));var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
            try
            {
                var bytes=new byte[checked(width*height*4)];using var scaled=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul));
                for(var i=0;i<count;i++)
                {
                    cancellation.ThrowIfCancellationRequested();using var preview=Preview(document,timeline,from+i/rate,Math.Max(width,height),cancellation);using var rendered=DocumentRenderer.Render(preview.Document);
                    using(var canvas=new SKCanvas(scaled)){canvas.Clear(SKColors.Black);using var image=SKImage.FromBitmap(rendered);canvas.DrawImage(image,SKRect.Create(width,height),new SKSamplingOptions(SKFilterMode.Linear));}
                    Marshal.Copy(scaled.GetPixels(),bytes,0,bytes.Length);process.StandardInput.BaseStream.Write(bytes);progress?.Invoke((i+1d)/count);
                }
                process.StandardInput.Close();if(!process.WaitForExit(60000)){Kill(process);throw new IOException("Video encoding timed out.");}cancellation.ThrowIfCancellationRequested();
                if(process.ExitCode!=0)throw new IOException(error.GetAwaiter().GetResult());File.Move(temporary,full,true);
            }
            catch{Kill(process);throw;}
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

public sealed class TimelinePreview(CanvasDocument document,List<ImportedImage> owned) : IDisposable
{
    public CanvasDocument Document { get; }=document;
    public void Dispose(){Document.Dispose();foreach(var asset in owned)asset.Dispose();}
}
