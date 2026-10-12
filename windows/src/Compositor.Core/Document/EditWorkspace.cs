using System.Runtime.InteropServices;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class EditWorkspace
{
    public static bool MaskPasted(CanvasDocument document, IEnumerable<Guid> roots, DocumentSelection selection, bool outside)
    {
        if (selection.Path is null) return false;
        var ids = roots.Distinct().ToHashSet();
        if (ids.Count == 0 || ids.Any(id => document.Layers.All(l => l.ID != id))) return false;
        var retained = document.Channels.Sum(c => (long)c.Asset.Width * c.Asset.Height) + document.Layers.Where(l => !ids.Contains(l.ID)).Sum(l => (long)(l.Mask?.Asset.Width ?? 0) * (l.Mask?.Asset.Height ?? 0));
        if (retained + (long)ids.Count * document.Width * document.Height > DocumentLimits.DocumentPixelBudget) return false;
        var whole = SKRectI.Create(document.Width, document.Height);
        using var coverage = selection.Coverage(whole); if (coverage is null) return false;
        foreach (var id in ids)
        {
            var layer = document.Layers.First(l => l.ID == id);
            var mask = coverage.Copy();
            if (outside) { var p = mask.GetPixelSpan(); for (var i = 0; i < p.Length; i++) p[i] = (byte)(255 - p[i]); }
            if (layer.Mask is { IsEnabled: true } old)
            {
                using var previous = MaskProperties.Coverage(old, old.Placement ?? layer.Transform,
                    new LayerTransform(0, 0, document.Width, document.Height), document.Width, document.Height);
                var p = mask.GetPixelSpan(); var b = previous.GetPixelSpan();
                for (var y = 0; y < mask.Height; y++) for (var x = 0; x < mask.Width; x++)
                    p[y * mask.RowBytes + x] = (byte)(p[y * mask.RowBytes + x] * b[y * previous.RowBytes + x] / 255);
            }
            layer.Mask = LayerMask.AssetFrom(mask); layer.Mask.Placement = new(0, 0, document.Width, document.Height); layer.Mask.IsLinked = false;
        }
        document.Selection = DocumentSelection.All; return true;
    }
    public static bool Paint(CanvasDocument doc, Guid id, SKColor color, double opacity, SKBlendMode mode, bool preserveAlpha,
        SKBitmap? pattern = null, double strokeWidth = 0, int strokeLocation = 1)
    {
        if (!LayerProtection.CanPaint(doc, id) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { IsGroup: false, Adjustment: null } layer
            || opacity is < 0 or > 1 || !double.IsFinite(opacity) || strokeWidth is < 0 or > 250 || !double.IsFinite(strokeWidth)) return false;
        var old = layer.Asset;
        if (old is null)
        {
            if ((long)doc.Width * doc.Height + doc.Layers.Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) > DocumentLimits.DocumentPixelBudget) return false;
            var blank = Bitmaps.Allocate(Bitmaps.ColorInfo(doc.Width, doc.Height)); blank.Erase(SKColors.Transparent);
            layer.Asset = ImportedImage.Create(blank, layer.Name); layer.Transform = new(0, 0, doc.Width, doc.Height);
        }
        var asset = layer.Asset!; using var work = DeepPixels.From(asset.WorkingImage);
        using var overlay = Bitmaps.Allocate(Bitmaps.ColorInfo(asset.Width, asset.Height)); overlay.Erase(SKColors.Transparent);
        var toDocument = BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height);
        if (!toDocument.TryInvert(out var inverse)) return false;
        using (var canvas = new SKCanvas(overlay))
        using (var paint = new SKPaint { Color = color, IsAntialias = true })
        {
            canvas.SetMatrix(inverse);
            if (pattern is not null) paint.Shader = SKShader.CreateBitmap(pattern, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            if (strokeWidth > 0)
            {
                using var path = doc.Selection.Path is { } selected ? new SKPath(selected) : SKPath.ParseSvgPathData($"M0 0 H{doc.Width} V{doc.Height} H0 Z");
                if (strokeLocation != 1) canvas.ClipPath(path, strokeLocation == 0 ? SKClipOperation.Intersect : SKClipOperation.Difference, true);
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)(strokeLocation == 1 ? strokeWidth : strokeWidth * 2); canvas.DrawPath(path, paint);
            }
            else canvas.DrawPaint(paint);
        }
        using var composed = new SKBitmap(work.Info.WithAlphaType(SKAlphaType.Premul));
        using (var canvas = new SKCanvas(composed))
        {
            canvas.DrawBitmap(work, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            using var paint = new SKPaint { BlendMode = mode, Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) };
            canvas.DrawBitmap(overlay, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        }
        using var result = DeepPixels.From(composed);
        if (preserveAlpha)
        {
            var a = MemoryMarshal.Cast<byte, float>(work.GetPixelSpan()); var b = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
            for (var i = 3; i < b.Length; i += 4) b[i] = a[i];
        }
        return ImageAdjustments.SetPixels(doc, layer, result, selection: strokeWidth == 0);
    }
    public static bool CanFade(CanvasDocument before, CanvasDocument after, Guid id) =>
        before.Layers.FirstOrDefault(l => l.ID == id) is { Asset: { } a } first &&
        after.Layers.FirstOrDefault(l => l.ID == id) is { Asset: { } b } last && first.Transform == last.Transform && a.Width == b.Width && a.Height == b.Height && !ReferenceEquals(a, b);
    public static bool Fade(CanvasDocument document, CanvasDocument before, Guid id, double opacity)
    {
        if (!CanFade(before, document, id) || !double.IsFinite(opacity) || opacity is < 0 or > 1) return false;
        var layer = document.Layers.First(l => l.ID == id);
        using var prior = DeepPixels.From(before.Layers.First(l => l.ID == id).Asset!.WorkingImage);
        using var result = DeepPixels.From(layer.Asset!.WorkingImage);
        var a = MemoryMarshal.Cast<byte, float>(prior.GetPixelSpan()); var b = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        for (var i = 0; i < b.Length; i += 4)
        {
            var alpha = (float)(a[i + 3] * (1 - opacity) + b[i + 3] * opacity);
            for (var c = 0; c < 3; c++) b[i + c] = alpha <= 0 ? 0 : (float)((a[i + c] * a[i + 3] * (1 - opacity) + b[i + c] * b[i + 3] * opacity) / alpha);
            b[i + 3] = alpha;
        }
        return ImageAdjustments.SetPixels(document, layer, result, selection: false);
    }
}
