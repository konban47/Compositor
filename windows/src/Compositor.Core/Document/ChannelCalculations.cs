using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum ImageChannel { RGB, Red, Green, Blue, Alpha, Gray }
public enum ChannelBlend { Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference, Exclusion, Add, Subtract, Divide, ColorBurn, ColorDodge, HardLight, SoftLight }
public sealed record ChannelSource(CanvasDocument Document, Guid? Layer, ImageChannel Channel, bool Invert = false, Guid? Alpha = null);
public sealed record CalculationSettings(ChannelBlend Blend = ChannelBlend.Multiply, double Opacity = 1, double Scale = 1, double Offset = 0, bool PreserveTransparency = true);

public static class ChannelCalculations
{
    public static SKBitmap Source(ChannelSource source)
    {
        var doc = source.Document;
        if (source.Alpha is { } id && doc.Channels.FirstOrDefault(c => c.ID == id) is { } alpha)
        {
            var result = DeepPixels.From(alpha.Asset.Image); var values = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
            if (source.Invert) for (var i = 0; i < values.Length; i += 4) for (var c = 0; c < 3; c++) values[i + c] = 1 - values[i + c]; return result;
        }
        SKBitmap composite;
        if (source.Layer is { } layerId)
        {
            using var draft = doc.Clone(); var wanted = draft.Descendants(layerId); wanted.Add(layerId);
            draft.Layers.RemoveAll(l => !wanted.Contains(l.ID)); foreach (var layer in draft.Layers) if (!wanted.Contains(layer.ParentID ?? Guid.Empty)) layer.ParentID = null;
            composite = DocumentRenderer.RenderDeep(draft);
        }
        else composite = DocumentRenderer.RenderDeep(doc);
        using (composite)
        {
            var result = DeepPixels.From(composite); var p = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
            for (var i = 0; i < p.Length; i += 4)
            {
                if (source.Channel != ImageChannel.RGB)
                { var value = source.Channel switch { ImageChannel.Red => p[i], ImageChannel.Green => p[i + 1], ImageChannel.Blue => p[i + 2], ImageChannel.Alpha => p[i + 3], _ => p[i] * .2126f + p[i + 1] * .7152f + p[i + 2] * .0722f }; p[i] = p[i + 1] = p[i + 2] = value; }
                if (source.Invert) for (var c = 0; c < 3; c++) p[i + c] = 1 - p[i + c]; p[i + 3] = 1;
            }
            return result;
        }
    }
    public static SKBitmap Calculate(ChannelSource first, ChannelSource second, CalculationSettings settings)
    {
        if (first.Document.Width != second.Document.Width || first.Document.Height != second.Document.Height) throw new InvalidOperationException("Source images must have the same pixel dimensions.");
        Validate(settings);
        using var a = Source(first); var result = Source(second); var left = MemoryMarshal.Cast<byte, float>(a.GetPixelSpan()); var right = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        for (var i = 0; i < right.Length; i += 4) for (var c = 0; c < 3; c++) right[i + c] = (float)(right[i + c] + (Blend(left[i + c], right[i + c], settings) - right[i + c]) * settings.Opacity);
        return result;
    }
    public static bool Apply(CanvasDocument document, Guid layerID, ChannelSource source, CalculationSettings settings, ChannelSource? mask = null)
    {
        Validate(settings);
        if (mask is not null && (mask.Document.Width != document.Width || mask.Document.Height != document.Height)) throw new InvalidOperationException("Source images must have the same pixel dimensions.");
        if (document.Width != source.Document.Width || document.Height != source.Document.Height) throw new InvalidOperationException("Source images must have the same pixel dimensions.");
        if (!LayerProtection.CanPaint(document, layerID) || document.Layers.FirstOrDefault(l => l.ID == layerID) is not { Asset: { } asset } layer) return false;
        using var src = Source(source); using var gate = mask is null ? null : Source(mask); using var work = DeepPixels.From(asset.WorkingImage);
        var p = MemoryMarshal.Cast<byte, float>(work.GetPixelSpan()); var input = MemoryMarshal.Cast<byte, float>(src.GetPixelSpan());
        var placement = BrushEdits.PixelToDocument(layer.Transform, work.Width, work.Height);
        for (var y = 0; y < work.Height; y++) for (var x = 0; x < work.Width; x++)
        {
            var point = placement.MapPoint(x + .5f, y + .5f); var sx = (int)Math.Floor(point.X); var sy = (int)Math.Floor(point.Y);
            if (sx < 0 || sy < 0 || sx >= src.Width || sy >= src.Height) continue;
            var i = y * work.RowBytes / 4 + x * 4; var si = sy * src.RowBytes / 4 + sx * 4;
            var amount = settings.Opacity * (gate is null ? 1 : MemoryMarshal.Cast<byte, float>(gate.GetPixelSpan())[sy * gate.RowBytes / 4 + sx * 4]);
            if (!settings.PreserveTransparency) p[i + 3] = (float)(p[i + 3] + (1 - p[i + 3]) * amount);
            for (var c = 0; c < 3; c++) p[i + c] = (float)(p[i + c] + (Blend(input[si + c], p[i + c], settings) - p[i + c]) * amount);
        }
        return ImageAdjustments.SetPixels(document, layer, work);
    }
    private static void Validate(CalculationSettings settings)
    {
        if (!Enum.IsDefined(settings.Blend) || !double.IsFinite(settings.Opacity) || !double.IsFinite(settings.Scale) || !double.IsFinite(settings.Offset)
            || settings.Opacity is < 0 or > 1 || settings.Scale is < 1 or > 2 || settings.Offset is < -255 or > 255) throw new ArgumentException("Invalid calculation settings.");
    }
    private static double Blend(double a, double b, CalculationSettings s) => Math.Clamp(s.Blend switch
    {
        ChannelBlend.Multiply => a * b, ChannelBlend.Screen => 1 - (1 - a) * (1 - b),
        ChannelBlend.Overlay => b <= .5 ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b),
        ChannelBlend.HardLight => a <= .5 ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b),
        ChannelBlend.SoftLight => a <= .5 ? b - (1 - 2 * a) * b * (1 - b) : b + (2 * a - 1) * ((b <= .25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b)) - b),
        ChannelBlend.Darken => Math.Min(a, b), ChannelBlend.Lighten => Math.Max(a, b), ChannelBlend.Difference => Math.Abs(a - b),
        ChannelBlend.Exclusion => a + b - 2 * a * b, ChannelBlend.Add => (a + b) / s.Scale + s.Offset / 255,
        ChannelBlend.Subtract => (b - a) / s.Scale + s.Offset / 255, ChannelBlend.Divide => a == 0 ? 1 : b / a,
        ChannelBlend.ColorBurn => a <= 0 ? 0 : 1 - (1 - b) / a, ChannelBlend.ColorDodge => a >= 1 ? 1 : b / (1 - a), _ => a,
    }, 0, 1);
}
