using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Rendering;

/// <summary>Immutable drawing commands. Playback uses the destination's GPU when it has one.
/// Unsupported effects use the reference compositor; display acceleration never changes saved pixels.</summary>
public sealed class DisplayScene : IDisposable
{
    private readonly SKPicture _picture;
    private int _references = 1;
    internal DisplayScene(SKPicture picture) => _picture = picture;
    public DisplayScene Retain() { Interlocked.Increment(ref _references); return this; }
    public void Draw(SKCanvas canvas) => canvas.DrawPicture(_picture);
    public void Dispose() { if (Interlocked.Decrement(ref _references) == 0) _picture.Dispose(); }
}

/// <summary>Retains immutable image uploads across transform changes. A new asset replaces only its own
/// cache entry; recording a new scene does not copy every layer again.</summary>
public sealed class DisplaySceneCache : IDisposable
{
    private readonly Dictionary<ImportedImage, SKImage> _images = [];
    public static bool Supports(CanvasDocument document)
    {
        var byId = document.Layers.ToDictionary(l => l.ID);
        bool Visible(ImageLayer layer)
        {
            var seen = new HashSet<Guid>();
            while (true)
            {
                if (!layer.IsVisible || !seen.Add(layer.ID)) return false;
                if (layer.ParentID is not { } parent || !byId.TryGetValue(parent, out layer!)) return true;
            }
        }
        return document.Layers.Where(Visible).All(l =>
        l.Adjustment is null && l.Effects is null && l.MaskSourceID is null
        && l.Blending is not { IsDefault: false } && l.Asset?.Deep is null
        && (!l.IsGroup || l.Container == LayerContainer.Group && l.BlendMode == LayerBlendMode.Normal)
        && BlendModes.Skia(BlendModes.From(l.BlendMode)) is not null
        && l.Mask is not { IsEnabled: true, Feather: > 0 }
        && l.Mask is not { IsEnabled: true, Density: < 1 });
    }

    public DisplayScene? Build(CanvasDocument document)
    {
        if (IO.ColorDefaults.Settings.BlendRgbGamma || IO.ColorDefaults.Settings.BlendTextGamma && document.Layers.Any(l => l.LiveText is not null)) { Dispose(); return null; }
        if (!Supports(document)) { Dispose(); return null; }
        var used = new HashSet<ImportedImage>();
        SKImage Image(ImportedImage asset)
        {
            used.Add(asset);
            if (!_images.TryGetValue(asset, out var image)) _images[asset] = image = SKImage.FromBitmap(asset.Image);
            return image;
        }
        var byId = document.Layers.ToDictionary(l => l.ID);
        var children = document.Layers.Where(l => l.ParentID is not null).GroupBy(l => l.ParentID!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create(document.Width, document.Height));
        canvas.ClipRect(SKRect.Create(document.Width, document.Height));
        if (document.Image.Background is { } background) canvas.Clear((SKColor)background);
        void Draw(ImageLayer layer, double inherited)
        {
            if (!layer.IsVisible) return;
            var opacity = inherited * layer.Opacity * layer.FillOpacity;
            if (opacity <= 0) return;
            if (layer.IsGroup)
            {
                if (children.TryGetValue(layer.ID, out var members)) foreach (var child in members) Draw(child, opacity);
                return;
            }
            if (layer.Asset is not { } asset) return;
            var masks = new List<(Model.LayerMask Mask, Model.LayerTransform Placement)>();
            if (layer.Mask is { IsEnabled: true } own) masks.Add((own, layer.MaskTransform));
            var parent = layer.ParentID;
            while (parent is { } id && byId.TryGetValue(id, out var group))
            {
                if (group.Mask is { IsEnabled: true } mask) masks.Add((mask, mask.Placement ?? group.Transform));
                parent = group.ParentID;
            }
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)), BlendMode = BlendModes.Skia(BlendModes.From(layer.BlendMode))!.Value };
            var bounds = Document.BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height).MapRect(SKRect.Create(asset.Width, asset.Height));
            if (masks.Count > 0)
            {
                canvas.SaveLayer(bounds, paint);
                paint.BlendMode = SKBlendMode.SrcOver; paint.Color = SKColors.White;
            }
            DrawImage(canvas, Image(asset), layer.Transform, paint);
            foreach (var (mask, placement) in masks)
            {
                using var clipPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                canvas.SaveLayer(bounds, clipPaint);
                using var maskPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
                if (mask.VectorPath is { } svg)
                {
                    using var path = mask.CreateVectorPath(svg);
                    canvas.Save(); canvas.Concat(Document.BrushEdits.PixelToDocument(placement, mask.Asset.Width, mask.Asset.Height));
                    if (path is not null) canvas.DrawPath(path, maskPaint);
                    canvas.Restore();
                }
                else
                {
                    using var luma = SKColorFilter.CreateLumaColor(); maskPaint.ColorFilter = luma;
                    DrawImage(canvas, Image(mask.Asset), placement, maskPaint);
                }
                canvas.Restore();
            }
            if (masks.Count > 0) canvas.Restore();
        }
        foreach (var layer in document.Layers.Where(l => l.ParentID is null)) Draw(layer, 1);
        var scene = new DisplayScene(recorder.EndRecording());
        foreach (var asset in _images.Keys.Where(a => !used.Contains(a)).ToArray()) { _images[asset].Dispose(); _images.Remove(asset); }
        return scene;
    }

    private static void DrawImage(SKCanvas canvas, SKImage image, Model.LayerTransform transform, SKPaint paint)
    {
        canvas.Save();
        canvas.Concat(Document.BrushEdits.PixelToDocument(transform, image.Width, image.Height));
        // Linear reduction follows the reference compositor; nearest stays exact at 1:1.
        var sampling = transform.Width == image.Width && transform.Height == image.Height && transform.Rotation == 0
            || transform.Sampling == LayerSampling.Nearest ? new SKSamplingOptions(SKFilterMode.Nearest)
            : transform.Width <= image.Width && transform.Height <= image.Height || transform.Sampling == LayerSampling.Smooth
                ? new SKSamplingOptions(SKFilterMode.Linear) : new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image, 0, 0, sampling, paint);
        canvas.Restore();
    }
    public void Dispose() { foreach (var image in _images.Values) image.Dispose(); _images.Clear(); }
}
