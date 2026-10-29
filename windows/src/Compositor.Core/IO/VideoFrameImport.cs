using System.Collections.Immutable;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record VideoFrameImportOptions(double Start, double End, double FrameRate, int Every = 1,
    int MaxSide = 1920, bool MakeAnimation = true);

/// <summary>Imports independent raster layers; the result no longer depends on the source video.</summary>
public static class VideoFrameImport
{
    public static double[] Times(TimelineMediaInfo media, VideoFrameImportOptions options)
    {
        if (media.Width <= 0 || media.Height <= 0 || !double.IsFinite(media.Duration) || media.Duration <= 0
            || !double.IsFinite(options.Start) || !double.IsFinite(options.End) || options.Start < 0
            || options.End - options.Start < .001 || options.End > media.Duration
            || !double.IsFinite(options.FrameRate) || options.FrameRate is < 1 or > 120
            || options.Every is < 1 or > 1000 || options.MaxSide is < 1 or > 8192)
            throw new ArgumentException("Invalid video import range or sampling settings.");
        if (options.MakeAnimation && options.Every / options.FrameRate > 600)
            throw new ArgumentException("An animation frame may not exceed 600 seconds.");
        var count = Math.Ceiling((options.End - options.Start) * options.FrameRate / options.Every - 1e-9);
        // Frame animations store a visibility snapshot for every layer at every frame.
        var limit = options.MakeAnimation ? 100 : 1000;
        if (count < 1 || count > limit)
            throw new InvalidDataException($"This import supports at most {limit} frames. Shorten the range or increase the frame interval.");
        var scale = Math.Min(1, (double)options.MaxSide / Math.Max(media.Width, media.Height));
        var width = Math.Max(1, (int)Math.Round(media.Width * scale));
        var height = Math.Max(1, (int)Math.Round(media.Height * scale));
        if ((long)width * height * count > DocumentLimits.DocumentPixelBudget)
            throw new InvalidDataException("Video frames exceed the document memory budget. Reduce the size or frame count.");
        return Enumerable.Range(0, (int)count).Select(index => options.Start + index * options.Every / options.FrameRate).ToArray();
    }

    public static CanvasDocument Import(string path, TimelineMediaInfo media, VideoFrameImportOptions options,
        Action<double>? progress = null, CancellationToken cancellation = default) =>
        Build(media, options, (time, size, token) => TimelineMedia.VideoFrame(path, time, size, token), progress, cancellation);

    internal static CanvasDocument Build(TimelineMediaInfo media, VideoFrameImportOptions options,
        Func<double, int, CancellationToken, SKBitmap> decode, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        var times = Times(media, options);
        CanvasDocument? document = null;
        try
        {
            long pixels = 0;
            for (var index = 0; index < times.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                using var bitmap = decode(times[index], Math.Min(options.MaxSide, Math.Max(media.Width, media.Height)), cancellation);
                cancellation.ThrowIfCancellationRequested();
                pixels += (long)bitmap.Width * bitmap.Height;
                if (bitmap.Width < 1 || bitmap.Height < 1 || bitmap.Width > options.MaxSide || bitmap.Height > options.MaxSide
                    || pixels > DocumentLimits.DocumentPixelBudget)
                    throw new InvalidDataException("Decoded video frames exceed the import size or memory limit.");
                document ??= new CanvasDocument(Guid.NewGuid(), bitmap.Width, bitmap.Height);
                var name = $"Frame {index + 1}";
                var asset = ImportedImage.Create(bitmap.Copy(), name);
                document.Layers.Add(new ImageLayer(Guid.NewGuid(), asset, new LayerTransform(0, 0, bitmap.Width, bitmap.Height), name)
                { IsVisible = index == 0 });
                progress?.Invoke((double)(index + 1) / times.Length);
            }
            if (options.MakeAnimation)
            {
                var frames = ImmutableArray.CreateBuilder<TimelineFrame>();
                for (var index = 0; index < times.Length; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    foreach (var layer in document!.Layers) layer.IsVisible = layer == document.Layers[index];
                    var duration = Math.Max(.001, (index + 1 < times.Length ? times[index + 1] : options.End) - times[index]);
                    frames.Add(new TimelineFrame(Guid.NewGuid(), duration,
                        LayerComps.Capture(document, $"Frame {index + 1}", CompCapture.Visibility)));
                }
                document!.Timeline = new DocumentTimeline
                {
                    Mode = TimelineMode.Frames, FrameRate = options.FrameRate,
                    WorkEnd = frames.Sum(frame => frame.Duration), Frames = frames.ToImmutable()
                };
                foreach (var layer in document.Layers) layer.IsVisible = layer == document.Layers[0];
            }
            cancellation.ThrowIfCancellationRequested();
            return document!;
        }
        catch { document?.Dispose(); throw; }
    }
}
