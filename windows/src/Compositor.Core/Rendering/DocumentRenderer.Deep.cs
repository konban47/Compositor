using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Rendering;

public static partial class DocumentRenderer
{
    /// <summary>These compositing operations have float paths. Legacy style/knockout kernels still use the display renderer.</summary>
    public static bool SupportsDeepComposite(CanvasDocument document) => document.Layers.All(l =>
        !l.IsGroup && l.Effects is null && l.Blending is null && l.MaskSourceID is null
        && (l.Adjustment is null || l.BlendMode == LayerBlendMode.Normal && DeepAdjustments.Supports(l.Adjustment)) && BlendModes.Skia(BlendModes.From(l.BlendMode)) is not null);
    public static SKBitmap RenderDeep(CanvasDocument document, SKRectI? region = null)
    {
        if (!SupportsDeepComposite(document)) { using var display = region is { } requested ? RenderRegion(document, requested) : Render(document); return DeepPixels.From(display); }
        var area = region ?? SKRectI.Create(document.Width, document.Height);
        var info = DeepPixels.Info(area.Width, area.Height).WithAlphaType(SKAlphaType.Premul);
        using var output = Bitmaps.Allocate(info); output.Erase(document.Image.Background is { } bg ? (SKColor)bg : SKColors.Transparent);
        var byId = document.Layers.ToDictionary(l => l.ID); var canvasPlacement = new Model.LayerTransform(area.Left, area.Top, area.Width, area.Height);
        foreach (var layer in document.Layers)
        {
            if (layer.IsGroup || !layer.IsVisible) continue;
            var chain = new List<ImageLayer> { layer }; var parent = layer.ParentID; var seen = new HashSet<Guid> { layer.ID };
            while (parent is { } id && seen.Add(id) && byId.TryGetValue(id, out var group)) { chain.Add(group); parent = group.ParentID; }
            if (chain.Any(l => !l.IsVisible)) continue;
            var opacity = chain.Aggregate(1.0, (value, l) => value * l.Opacity * l.FillOpacity);
            using var raster = Bitmaps.Allocate(info); raster.Erase(SKColors.Transparent);
            if (layer.Adjustment is { } adjustment)
            {
                using var straight = DeepPixels.From(output); DeepAdjustments.Adjust(straight, adjustment);
                using var surface = new SKCanvas(raster); surface.DrawBitmap(straight, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            }
            else if (layer.Asset is { } asset)
            { using var surface = new SKCanvas(raster); using var paint = new SKPaint { IsAntialias = true }; surface.Translate(-area.Left, -area.Top); DrawTransformed(surface, asset.WorkingImage, layer.Transform, SKBlendMode.SrcOver, paint); }
            else continue;
            var rgba = MemoryMarshal.Cast<byte, float>(raster.GetPixelSpan());
            foreach (var item in chain)
            {
                if (item.Mask is not { IsEnabled: true } mask) continue;
                using var coverage = MaskProperties.Coverage(mask, item.MaskTransform, canvasPlacement, raster.Width, raster.Height);
                if (coverage is null) continue;
                for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++)
                { var a = coverage.GetPixel(x, y).Red / 255f; var i = y * raster.RowBytes / 4 + x * 4; for (var c = 0; c < 4; c++) rgba[i + c] *= a; }
            }
            if (layer.Adjustment is not null)
            {
                var destination = MemoryMarshal.Cast<byte, float>(output.GetPixelSpan());
                for (var i = 0; i < rgba.Length; i += 4)
                {
                    var amount = destination[i + 3] > 0 ? rgba[i + 3] / destination[i + 3] * (float)opacity : 0;
                    for (var c = 0; c < 3; c++) destination[i + c] = destination[i + c] * (1 - amount) + rgba[i + c] * (float)opacity;
                }
                continue;
            }
            var settings = IO.ColorDefaults.Settings;
            if (layer.BlendMode == LayerBlendMode.Normal && (settings.BlendRgbGamma || layer.LiveText is not null && settings.BlendTextGamma))
            { ColorControls.Composite(raster, output, 0, 0, opacity, layer.LiveText is not null && settings.BlendTextGamma ? settings.TextGamma : settings.RgbGamma); continue; }
            using var canvas = new SKCanvas(output); using var brush = new SKPaint { ColorF = new SKColorF(1, 1, 1, (float)opacity), BlendMode = BlendModes.Skia(BlendModes.From(layer.BlendMode))!.Value };
            canvas.DrawBitmap(raster, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), brush);
        }
        return DeepPixels.From(output);
    }
}
