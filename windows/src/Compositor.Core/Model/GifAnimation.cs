using System.Collections.Immutable;
using Compositor.Core.Format;
using Compositor.Core.IO;
using SkiaSharp;
namespace Compositor.Core.Model;

/// <summary>One immutable encoded source. Full-size pixels are kept only for the current frame and edited frames.</summary>
public sealed class GifSource
{
    public const int MaxFrames = 4096;
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<double> Delays { get; }
    public GifSource(byte[] data)
    {
        if (data.Length > DocumentLimits.MaxAssetBytes) throw new InvalidDataException("GIF source is too large.");
        using var codec = SKCodec.Create(new SKMemoryStream(data));
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Gif || codec.FrameCount is < 1 or > MaxFrames) throw new InvalidDataException("Invalid GIF frame count.");
        Width = codec.Info.Width; Height = codec.Info.Height;
        if (Width < 1 || Height < 1 || Width > DocumentLimits.MaxSide || Height > DocumentLimits.MaxSide || (long)Width * Height > DocumentLimits.MaxSurfacePixels) throw new InvalidDataException("GIF exceeds the image size limit.");
        Data = data; Delays = codec.FrameInfo.Select(f => Math.Clamp(f.Duration <= 0 ? .1 : f.Duration / 1000d, .02, 655.35)).ToArray();
    }
    public ImportedImage Decode(int index, string name, GifGeometry? geometry = null)
    {
        if (index < 0 || index >= Delays.Count) throw new InvalidDataException("GIF frame is out of range.");
        using var codec = SKCodec.Create(new SKMemoryStream(Data)); var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(Width, Height));
        try
        {
            pixels.Erase(SKColors.Transparent);
            if (codec.GetPixels(pixels.Info, pixels.GetPixels(), new SKCodecOptions(index)) != SKCodecResult.Success) throw new InvalidDataException("Could not decode GIF frame.");
            if (geometry is not null)
            {
                var transformed = Bitmaps.Allocate(Bitmaps.ColorInfo(geometry.Width, geometry.Height));
                try { using var canvas = new SKCanvas(transformed); canvas.Clear(SKColors.Transparent); canvas.SetMatrix(new SKMatrix((float)(geometry.A * geometry.Width / Width), (float)(geometry.C * geometry.Width / Height), (float)(geometry.Tx * geometry.Width), (float)(geometry.B * geometry.Height / Width), (float)(geometry.D * geometry.Height / Height), (float)(geometry.Ty * geometry.Height), 0, 0, 1)); using var image = SKImage.FromBitmap(pixels); canvas.DrawImage(image, 0, 0, new SKSamplingOptions(geometry.Sampling == LayerSampling.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear)); }
                catch { transformed.Dispose(); throw; }
                pixels.Dispose(); pixels = transformed;
            }
            return ImportedImage.Create(pixels, name);
        }
        catch { pixels.Dispose(); throw; }
    }
}

public sealed record GifFrameReference(int Index, int SourceIndex);
public sealed record GifFramePixels(int Index, string ImageFile);
public sealed record GifFrameDelay(int Index, double Duration);
public sealed record GifGeometry(int Width, int Height, double A, double B, double C, double D, double Tx, double Ty, LayerSampling Sampling = LayerSampling.HighQuality)
{
    public bool IsValid => Width is > 0 and <= DocumentLimits.MaxSide && Height is > 0 and <= DocumentLimits.MaxSide && (long)Width * Height <= DocumentLimits.MaxSurfacePixels
        && new[] { A, B, C, D, Tx, Ty }.All(n => double.IsFinite(n) && Math.Abs(n) <= 1000000) && Math.Abs(A * D - B * C) > 1e-15 && Enum.IsDefined(Sampling);
}
public sealed record GifTiming(string Mode = "Frame delay", double UniformDelay = .1, List<GifFrameDelay>? FrameDelays = null);
/// <summary>The upstream version-17 manifest representation, also written by Windows format 27.</summary>
public sealed record GifRecord(string SourceFile, int SelectedFrameIndex = 0, GifGeometry? Geometry = null, List<GifFramePixels>? FramePixels = null,
    List<int>? DeletedFrameIndices = null, List<GifFrameReference>? FrameOrder = null, GifTiming? Timing = null)
{
    public static string SourceName(Guid id) => id.ToString().ToUpperInvariant() + ".gif";
    public static string FrameName(Guid id, int frame) => id.ToString().ToUpperInvariant() + $".frame-{frame}.png";
    public bool IsValid(Guid id) => SourceFile == SourceName(id) && SelectedFrameIndex is >= 0 and < 8192 && Geometry is not { IsValid: false }
        && (FramePixels?.Count ?? 0) <= GifSource.MaxFrames && (FramePixels is null || FramePixels.All(f => f is not null && f.Index is >= 0 and < 8192 && f.ImageFile == FrameName(id, f.Index)) && FramePixels.Select(f => f.Index).Distinct().Count() == FramePixels.Count)
        && (DeletedFrameIndices?.Count ?? 0) <= GifSource.MaxFrames && (DeletedFrameIndices is null || DeletedFrameIndices.All(i => i is >= 0 and < GifSource.MaxFrames) && DeletedFrameIndices.Distinct().Count() == DeletedFrameIndices.Count)
        && (FrameOrder is null || FrameOrder.Count is > 0 and <= GifSource.MaxFrames && FrameOrder.All(f => f is not null && f.Index is >= 0 and < 8192 && f.SourceIndex is >= 0 and < GifSource.MaxFrames) && FrameOrder.Select(f => f.Index).Distinct().Count() == FrameOrder.Count)
        && (Timing is null || Timing.Mode is "Frame delay" or "Same for all" && ValidDelay(Timing.UniformDelay) && (Timing.FrameDelays is null || Timing.FrameDelays.Count <= GifSource.MaxFrames && Timing.FrameDelays.All(f => f is not null && f.Index is >= 0 and < 8192 && ValidDelay(f.Duration)) && Timing.FrameDelays.Select(f => f.Index).Distinct().Count() == Timing.FrameDelays.Count));
    public static bool ValidDelay(double value) => double.IsFinite(value) && value is >= .02 and <= 655.35;
}
public sealed class GifGeometryException : InvalidOperationException
{
    public GifGeometryException() : base("This edit changes the GIF frame grid. Use Image Size or non-destructive layer transforms for animated layers.") { }
}
public sealed record GifAnimation(GifSource Source)
{
    public int SelectedFrameIndex { get; init; }
    public GifGeometry? Geometry { get; init; }
    public ImmutableDictionary<int, ImportedImage> Edited { get; init; } = ImmutableDictionary<int, ImportedImage>.Empty;
    public ImmutableHashSet<int> Deleted { get; init; } = ImmutableHashSet<int>.Empty;
    public ImmutableArray<GifFrameReference> Order { get; init; } = [];
    public string TimingMode { get; init; } = "Frame delay";
    public double UniformDelay { get; init; } = .1;
    public ImmutableDictionary<int, double> Delays { get; init; } = ImmutableDictionary<int, double>.Empty;
    public IReadOnlyList<GifFrameReference> Frames => Order.IsDefaultOrEmpty ? Enumerable.Range(0, Source.Delays.Count).Where(i => !Deleted.Contains(i)).Select(i => new GifFrameReference(i, i)).ToArray() : Order;
    public double Delay(int index) => TimingMode == "Same for all" ? UniformDelay : Delays.GetValueOrDefault(index, Source.Delays[Frames.Single(f => f.Index == index).SourceIndex]);
    public ImportedImage Decode(int index, string name) => Edited.TryGetValue(index, out var edited) ? edited : Source.Decode(Frames.Single(f => f.Index == index).SourceIndex, name, Geometry);
    public GifAnimation Edit(int index, ImportedImage image)
    {
        if (image.Width != (Geometry?.Width ?? Source.Width) || image.Height != (Geometry?.Height ?? Source.Height)) throw new GifGeometryException();
        return this with { SelectedFrameIndex = index, Edited = Edited.SetItem(index, image) };
    }
    public GifAnimation Duplicate(int index)
    {
        var frames = Frames.ToList(); if (frames.Count >= GifSource.MaxFrames) throw new InvalidOperationException("GIF contains too many frames.");
        var at = frames.FindIndex(f => f.Index == index); if (at < 0) throw new InvalidDataException("Unknown GIF frame.");
        var id = Math.Max(Source.Delays.Count, frames.Max(f => f.Index) + 1); if (id >= 8192) throw new InvalidOperationException("GIF frame identity limit reached.");
        frames.Insert(at + 1, new GifFrameReference(id, frames[at].SourceIndex));
        return this with { Order = frames.ToImmutableArray(), SelectedFrameIndex = id, Delays = Delays.SetItem(id, Delay(index)), Edited = Edited.TryGetValue(index, out var image) ? Edited.SetItem(id, image) : Edited };
    }
    public GifAnimation Remove(int index)
    {
        var frames = Frames.ToList(); if (frames.Count <= 1) throw new InvalidOperationException("Keep at least one GIF frame.");
        var at = frames.FindIndex(f => f.Index == index); if (at < 0) throw new InvalidDataException("Unknown GIF frame."); frames.RemoveAt(at);
        return this with { Order = frames.ToImmutableArray(), SelectedFrameIndex = frames[Math.Min(at,frames.Count-1)].Index, Deleted = index < Source.Delays.Count ? Deleted.Add(index) : Deleted, Edited = Edited.Remove(index), Delays = Delays.Remove(index) };
    }
    public GifAnimation Move(int index, int delta)
    {
        var frames = Frames.ToList(); var at = frames.FindIndex(f => f.Index == index); var target = Math.Clamp(at + delta, 0, frames.Count - 1); if (at < 0 || target == at) return this;
        var item = frames[at]; frames.RemoveAt(at); frames.Insert(target,item); return this with { Order = frames.ToImmutableArray(), SelectedFrameIndex = index };
    }
    public GifRecord Record(Guid id) => new(GifRecord.SourceName(id), SelectedFrameIndex, Geometry, Edited.Count == 0 ? null : Edited.Keys.Order().Select(i => new GifFramePixels(i, GifRecord.FrameName(id,i))).ToList(), Deleted.Count == 0 ? null : Deleted.Order().ToList(), Order.IsDefaultOrEmpty ? null : Order.ToList(), new(TimingMode,UniformDelay,Delays.Count == 0 ? null : Delays.Select(p=>new GifFrameDelay(p.Key,p.Value)).ToList()));
    public static GifAnimation Restore(GifSource source, GifRecord record, IReadOnlyDictionary<int,ImportedImage> pixels)
    {
        var animation = new GifAnimation(source) { SelectedFrameIndex=record.SelectedFrameIndex, Geometry=record.Geometry, Edited=pixels.ToImmutableDictionary(), Deleted=(record.DeletedFrameIndices??[]).ToImmutableHashSet(), Order=(record.FrameOrder??[]).ToImmutableArray(), TimingMode=record.Timing?.Mode??"Frame delay", UniformDelay=record.Timing?.UniformDelay??.1, Delays=(record.Timing?.FrameDelays??[]).ToImmutableDictionary(f=>f.Index,f=>f.Duration) };
        var frames=animation.Frames;var ids=frames.Select(f=>f.Index).ToHashSet();
        if(frames.Count==0||frames.Count>GifSource.MaxFrames||!ids.Contains(animation.SelectedFrameIndex)||frames.Any(f=>f.SourceIndex>=source.Delays.Count||f.Index<source.Delays.Count&&(f.Index!=f.SourceIndex||animation.Deleted.Contains(f.Index)))||animation.Deleted.Any(i=>i>=source.Delays.Count)||!animation.Edited.Keys.All(ids.Contains)||!animation.Delays.Keys.All(ids.Contains)||!frames.Where(f=>f.Index<source.Delays.Count).Select(f=>f.Index).ToHashSet().SetEquals(Enumerable.Range(0,source.Delays.Count).Where(i=>!animation.Deleted.Contains(i)))) throw new InvalidDataException("Invalid GIF frame references.");
        var width=animation.Geometry?.Width??source.Width;var height=animation.Geometry?.Height??source.Height;
        if(pixels.Values.Any(p=>p.Width!=width||p.Height!=height))throw new InvalidDataException("GIF edit dimensions do not match.");
        return animation;
    }
}
