using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Document;

public enum GradientMethod { Perceptual, Linear, Classic, Smooth, Stripes }
public sealed record GradientStopValue(double Position, uint Color);
public sealed record GradientPaintSettings
{
    public GradientShape Shape { get; init; }
    public GradientMethod Method { get; init; } = GradientMethod.Smooth;
    public bool Reverse { get; init; }
    public bool Dither { get; init; } = true;
    public GradientStopValue[] Stops { get; init; } = [new(0, 0xff000000), new(1, 0xffffffff)];
    [JsonIgnore] public bool IsValid => Enum.IsDefined(Shape) && Enum.IsDefined(Method) && Stops is { Length: >= 2 and <= 64 }
        && Stops.All(s => s is not null && double.IsFinite(s.Position) && s.Position is >= 0 and <= 1)
        && Stops.Zip(Stops.Skip(1)).All(p => p.First.Position <= p.Second.Position);
    public SKColorF Sample(double parameter, int x = 0, int y = 0, int depth = 8)
    {
        var t = Math.Clamp(Reverse ? 1 - parameter : parameter, 0, 1);
        var hi = Array.FindIndex(Stops, s => s.Position > t); if (hi < 0) hi = Stops.Length - 1;
        var lo = Math.Max(0, hi - 1); var a = Stops[lo]; var b = Stops[hi];
        var f = Math.Clamp((t - a.Position) / Math.Max(.000001, b.Position - a.Position), 0, 1);
        if (Method == GradientMethod.Smooth) f = f * f * (3 - 2 * f);
        if (Method == GradientMethod.Stripes) f = t >= b.Position ? 1 : 0;
        var ca = new SKColor(a.Color); var cb = new SKColor(b.Color);
        var va = new Vector3(ca.Red, ca.Green, ca.Blue) / 255; var vb = new Vector3(cb.Red, cb.Green, cb.Blue) / 255;
        if (Method == GradientMethod.Linear) { va = Linear(va); vb = Linear(vb); }
        if (Method == GradientMethod.Perceptual) { va = Lab(Linear(va)); vb = Lab(Linear(vb)); }
        var v = Vector3.Lerp(va, vb, (float)f);
        if (Method == GradientMethod.Perceptual) v = Gamma(FromLab(v));
        if (Method == GradientMethod.Linear) v = Gamma(v);
        if (Dither) v += new Vector3((float)((BrushDabs.Random(43, x + y * 32749, 7) - .5) / (depth == 8 ? 255 : 65535)));
        v = Vector3.Clamp(v, Vector3.Zero, Vector3.One);
        return new(v.X, v.Y, v.Z, (float)((ca.Alpha + (cb.Alpha - ca.Alpha) * f) / 255));
    }
    private static Vector3 Linear(Vector3 c) { float F(float v) => v <= .04045 ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f); return new(F(c.X), F(c.Y), F(c.Z)); }
    private static Vector3 Gamma(Vector3 c) { float F(float v) => v <= .0031308 ? v * 12.92f : 1.055f * MathF.Pow(Math.Max(0, v), 1 / 2.4f) - .055f; return new(F(c.X), F(c.Y), F(c.Z)); }
    private static Vector3 Lab(Vector3 c)
    { var l = MathF.Cbrt(Vector3.Dot(c, new(.41222147f, .53633254f, .05144599f))); var m = MathF.Cbrt(Vector3.Dot(c, new(.2119035f, .68069955f, .10739696f))); var s = MathF.Cbrt(Vector3.Dot(c, new(.08830246f, .28171884f, .6299787f))); return new(.21045426f*l + .7936178f*m - .00407205f*s, 1.9779985f*l - 2.4285922f*m + .4505937f*s, .02590404f*l + .78277177f*m - .80867577f*s); }
    private static Vector3 FromLab(Vector3 c)
    { var l = MathF.Pow(c.X + .39633778f*c.Y + .21580376f*c.Z, 3); var m = MathF.Pow(c.X - .10556135f*c.Y - .06385417f*c.Z, 3); var s = MathF.Pow(c.X - .08948418f*c.Y - 1.2914855f*c.Z, 3); return new(4.0767417f*l - 3.3077116f*m + .23096994f*s, -1.268438f*l + 2.6097574f*m - .3413194f*s, -.00419609f*l - .7034186f*m + 1.7076147f*s); }
}
public sealed record LiveGradientSettings(double StartX, double StartY, double EndX, double EndY, GradientPaintSettings Paint)
{
    [JsonIgnore] public bool IsValid => new[] { StartX, StartY, EndX, EndY }.All(n => double.IsFinite(n) && Math.Abs(n) < 1000000) && Paint is { IsValid: true };
}
public sealed record LayerGradient(LiveGradientSettings Settings, SKBitmap Image);
public static class GradientPaint
{
    public static bool Fill(CanvasDocument doc, Guid id, bool mask, SKPoint start, SKPoint end, GradientPaintSettings settings, double opacity, PaintBlend blend = PaintBlend.Normal)
    {
        if (!settings.IsValid || !GradientEdits.HasLine(start, end) || !LayerProtection.CanPaint(doc, id) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { } layer) return false;
        if (!mask)
        {
            BrushEdits.EnsurePixels(doc, id); var depth = layer.Asset?.BitDepth ?? 8;
            return DeepPaint.Paint(doc, layer, p => settings.Sample(GradientEdits.Parameter(start, end, p, settings.Shape), (int)p.X, (int)p.Y, depth), opacity, false, blend);
        }
        if (layer.Mask is null) return false; BrushEdits.GrowMask(doc, id);
        var bitmap = Bitmaps.Copy(layer.Mask.Asset.Image); var map = BrushEdits.PixelToDocument(layer.MaskTransform, bitmap.Width, bitmap.Height);
        using var clip = FillEdits.Coverage(doc, out var region);
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
        { var at = map.MapPoint(x + .5f, y + .5f); var c = settings.Sample(GradientEdits.Parameter(start, end, at, settings.Shape), x, y); var amount = FillEdits.Amount(clip, region, at) * opacity * c.Alpha; var old = bitmap.GetPixel(x, y).Red; var v = (byte)Math.Clamp(Math.Round(old + ((c.Red + c.Green + c.Blue) * 85 - old) * amount), 0, 255); bitmap.SetPixel(x, y, new SKColor(v, v, v)); }
        layer.Mask = layer.Mask.Replacing(ImportedImage.Create(bitmap, layer.Mask.Asset.Name)); return true;
    }
    public static bool SetLive(CanvasDocument doc, ImageLayer layer, LiveGradientSettings settings)
    {
        if (!settings.IsValid || !LayerProtection.CanPaint(doc, layer.ID)) return false;
        var width = layer.Asset?.Width ?? doc.Width; var height = layer.Asset?.Height ?? doc.Height; var depth = layer.Asset?.BitDepth ?? doc.Image.Depth;
        using var pixels = Bitmaps.Allocate(DeepPixels.Info(width, height)); var data = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
        var start = new SKPoint((float)settings.StartX, (float)settings.StartY); var end = new SKPoint((float)settings.EndX, (float)settings.EndY);
        if (!GradientEdits.HasLine(start, end)) return false;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var c = settings.Paint.Sample(GradientEdits.Parameter(start, end, new(x + .5f, y + .5f), settings.Paint.Shape), x, y, depth); var i = (y * width + x) * 4; data[i] = c.Red; data[i + 1] = c.Green; data[i + 2] = c.Blue; data[i + 3] = c.Alpha; }
        if (layer.Asset is null) { var empty = Bitmaps.Allocate(depth > 8 ? DeepPixels.Info(width, height) : Bitmaps.ColorInfo(width, height)); empty.Erase(SKColors.Transparent); layer.Asset = depth > 8 ? ImportedImage.CreateDeep(empty, layer.Name, depth) : ImportedImage.Create(empty, layer.Name); }
        ImageAdjustments.SetPixels(doc, layer, pixels, selection: false);
        layer.Gradient = new(settings, layer.Asset!.Image); return true;
    }
}
