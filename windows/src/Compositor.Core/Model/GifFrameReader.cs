using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>A bounded, disposable reader for one encoded GIF. Output images never alias its working pixels.</summary>
public sealed class GifFrameReader : IDisposable
{
    private readonly GifSource _source;
    private readonly SKCodecFrameInfo[] _frames;
    private readonly object _gate = new();
    private SKCodec? _codec;
    private SKBitmap? _pixels;
    private int _index = -1;
    public GifFrameReader(GifSource source)
    {
        _source = source;
        using var reservation = MemoryBudget.Current.Reserve(source.Data.LongLength);
        _codec = SKCodec.Create(new SKMemoryStream(source.Data)) ?? throw new InvalidDataException("Could not open GIF reader.");
        _frames = _codec.FrameInfo;
    }
    public ImportedImage Decode(int index, string name, GifGeometry? geometry = null) => Decode(index, name, geometry, true);
    internal ImportedImage DecodeOnce(int index, string name, GifGeometry? geometry) => Decode(index, name, geometry, false);
    private ImportedImage Decode(int index, string name, GifGeometry? geometry, bool retainWorkingPixels)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_codec is null, this);
            if (index < 0 || index >= _frames.Length) throw new InvalidDataException("GIF frame is out of range.");
            if (geometry is { IsValid: false }) throw new InvalidDataException("Invalid GIF geometry.");
            _pixels ??= Bitmaps.Allocate(Bitmaps.ColorInfo(_source.Width, _source.Height));
            if (_index != index)
            {
                // Skia accepts a cached predecessor only within the required range, excluding RestorePrevious.
                var required = _frames[index].RequiredFrame;
                var prior = required >= 0 && _index >= required && _index < index && _frames[_index].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious ? _index : -1;
                if (prior < 0) _pixels.Erase(SKColors.Transparent);
                _index = -1;
                if (_codec.GetPixels(_pixels.Info, _pixels.GetPixels(), _pixels.RowBytes, new SKCodecOptions(index, prior)) != SKCodecResult.Success)
                    throw new InvalidDataException("Could not decode GIF frame.");
                _index = index;
            }
            var output = geometry is null ? retainWorkingPixels ? Bitmaps.Copy(_pixels) : _pixels : Transform(_pixels, geometry);
            if (!retainWorkingPixels && geometry is null) { _pixels = null; _index = -1; }
            try { return ImportedImage.Create(output, name); }
            catch { output.Dispose(); throw; }
        }
    }
    private static SKBitmap Transform(SKBitmap pixels, GifGeometry geometry)
    {
        var transformed = Bitmaps.Allocate(Bitmaps.ColorInfo(geometry.Width, geometry.Height));
        try
        {
            using var canvas = new SKCanvas(transformed); canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(new SKMatrix((float)(geometry.A * geometry.Width / pixels.Width), (float)(geometry.C * geometry.Width / pixels.Height), (float)(geometry.Tx * geometry.Width),
                (float)(geometry.B * geometry.Height / pixels.Width), (float)(geometry.D * geometry.Height / pixels.Height), (float)(geometry.Ty * geometry.Height), 0, 0, 1));
            using var image = SKImage.FromBitmap(pixels); canvas.DrawImage(image, 0, 0, new SKSamplingOptions(geometry.Sampling == LayerSampling.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear));
            return transformed;
        }
        catch { transformed.Dispose(); throw; }
    }
    public void Dispose()
    {
        lock (_gate) { _pixels?.Dispose(); _pixels = null; _codec?.Dispose(); _codec = null; _index = -1; }
    }
}

/// <summary>Readers are shared by layers using the same source, and released when playback or export ends.</summary>
public sealed class GifDecodeSession : IDisposable
{
    private readonly Dictionary<GifSource, GifFrameReader> _readers = [];
    private readonly object _gate = new();
    private bool _disposed;
    public ImportedImage Decode(GifAnimation animation, int index, string name)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (animation.Edited.TryGetValue(index, out var edited)) return edited;
            if (!_readers.TryGetValue(animation.Source, out var reader))
            { reader = new GifFrameReader(animation.Source); _readers.Add(animation.Source, reader); }
            return reader.Decode(animation.Frames.Single(f => f.Index == index).SourceIndex, name, animation.Geometry);
        }
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; foreach (var reader in _readers.Values) reader.Dispose(); _readers.Clear(); }
    }
}

