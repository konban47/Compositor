using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Float fill/gradient paths: interpolate before quantization, including feathered selections.</summary>
public static class DeepPaint
{
    public static bool Fill(CanvasDocument doc, ImageLayer layer, SKColor color, bool clear = false) =>
        Paint(doc, layer, _ => new SKColorF(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f), 1, clear);
    public static bool Gradient(CanvasDocument doc, ImageLayer layer, SKPoint start, SKPoint end, SKColor from, SKColor to, double opacity, GradientShape shape) =>
        Paint(doc, layer, point =>
        {
            var t = GradientEdits.Parameter(start, end, point, shape);
            float Mix(byte a, byte b) => (float)((a + (b - a) * t) / 255);
            return new SKColorF(Mix(from.Red, to.Red), Mix(from.Green, to.Green), Mix(from.Blue, to.Blue), Mix(from.Alpha, to.Alpha));
        }, opacity, false);
    public static bool Paint(CanvasDocument doc, ImageLayer layer, Func<SKPoint, SKColorF> sample, double opacity, bool clear, PaintBlend blend = PaintBlend.Normal)
    {
        if (layer.Asset is not { } asset || !LayerProtection.CanPaint(doc, layer.ID) || !double.IsFinite(opacity)) return false;
        using var result = DeepPixels.From(asset.WorkingImage); var p = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        using var coverage = FillEdits.Coverage(doc, out var region);
        var map = BrushEdits.PixelToDocument(layer.Transform, result.Width, result.Height);
        for (var y = 0; y < result.Height; y++) for (var x = 0; x < result.Width; x++)
        {
            var i = y * result.RowBytes / 4 + x * 4;
            var point = map.MapPoint(x + .5f, y + .5f); var amount = FillEdits.Amount(coverage, region, point);
            if (amount <= 0) continue;
            if (clear) { p[i + 3] *= (float)(1 - amount); continue; }
            var color = sample(point);
            if (blend != PaintBlend.Normal)
            {
                var power = opacity * amount;
                if (blend == PaintBlend.Dissolve) power = BrushDabs.Random(1729, x + y * result.Width, 1) < power ? 1 : 0;
                var mixed = BrushBlending.Composite(new SKColorF(p[i], p[i + 1], p[i + 2], p[i + 3]), color, power, blend);
                p[i] = mixed.Red; p[i + 1] = mixed.Green; p[i + 2] = mixed.Blue; p[i + 3] = mixed.Alpha; continue;
            }
            var alpha = (float)Math.Clamp(color.Alpha * opacity * amount, 0, 1);
            var under = p[i + 3]; var next = alpha + under * (1 - alpha); if (next <= 0) continue;
            p[i] = (color.Red * alpha + p[i] * under * (1 - alpha)) / next;
            p[i + 1] = (color.Green * alpha + p[i + 1] * under * (1 - alpha)) / next;
            p[i + 2] = (color.Blue * alpha + p[i + 2] * under * (1 - alpha)) / next; p[i + 3] = next;
        }
        return ImageAdjustments.SetPixels(doc, layer, result, selection: false);
    }
    /// <summary>
    /// The layers Convert Layer to 16-Bit / 8-Bit works on: the selected ones and those inside selected groups that
    /// hold pixels it can convert. A GIF's frame edits keep their own 8-bit grid, so animated layers are left alone.
    /// </summary>
    public static List<ImageLayer> DepthTargets(CanvasDocument doc, IEnumerable<Guid> ids)
    {
        if (doc.Image.Mode is Compositor.Core.Format.ImageMode.Bitmap or Compositor.Core.Format.ImageMode.Indexed or Compositor.Core.Format.ImageMode.Duotone) return [];
        var chosen = new HashSet<Guid>(ids);
        foreach (var id in chosen.ToList()) chosen.UnionWith(doc.Descendants(id));
        return doc.Layers.Where(l => chosen.Contains(l.ID) && l is { IsGroup: false, LiveText: null, LiveShape: null, Adjustment: null, Gif: null, Asset: not null }
            && LayerProtection.CanPaint(doc, l.ID)).ToList();
    }

    /// <summary>Whether converting the selected layers to <paramref name="depth"/> would change one of them.</summary>
    public static bool CanConvertLayers(CanvasDocument doc, IEnumerable<Guid> ids, int depth) =>
        DepthTargets(doc, ids).Any(l => l.Asset!.BitDepth != depth);

    /// <summary>Convert Layer to 16-Bit / 8-Bit for every selected layer (upstream de0a43d). False when none changed.</summary>
    public static bool ConvertLayers(CanvasDocument doc, IEnumerable<Guid> ids, int depth)
    {
        var changed = false;
        foreach (var layer in DepthTargets(doc, ids)) changed |= ConvertLayer(doc, layer.ID, depth);
        return changed;
    }

    public static bool ConvertLayer(CanvasDocument doc, Guid id, int depth)
    {
        if (depth is not (8 or 16) || doc.Image.Mode is Compositor.Core.Format.ImageMode.Bitmap or Compositor.Core.Format.ImageMode.Indexed or Compositor.Core.Format.ImageMode.Duotone
            || !LayerProtection.CanPaint(doc, id) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { IsGroup: false, LiveText: null, LiveShape: null, Adjustment: null, Asset: { } asset } layer || asset.BitDepth == depth) return false;
        if (depth == 8) layer.Asset = ImportedImage.Create(asset.Image.Copy(), asset.Name);
        else
        {
            var pixels = DeepPixels.From(asset.WorkingImage); var values = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
            for (var i = 0; i < values.Length; i++) values[i] = MathF.Round(Math.Clamp(values[i], 0, 1) * 65535) / 65535;
            layer.Asset = ImportedImage.CreateDeep(pixels, asset.Name, 16);
        }
        return true;
    }
}
