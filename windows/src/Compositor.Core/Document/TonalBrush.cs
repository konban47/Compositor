using System.Numerics;
using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Local retouching in straight floating-point color; the same dab geometry as painting.</summary>
public static class TonalBrush
{
    public static bool Paint(CanvasDocument document, ImageLayer layer, IReadOnlyList<SKPoint> points, BrushSettings brush)
    {
        if (layer.Asset is not { } asset || !LayerProtection.CanPaint(document, layer.ID) || points.Count == 0
            || brush.Strength <= 0 || !double.IsFinite(brush.Strength + brush.Diameter) || brush.Diameter is <= 0 or > 5000) return false;
        var options=brush.Painting ?? new(); if (!options.IsValid) return false;
        using var texture=PaintBrushEngine.Decode(options.TextureImage);
        var map = BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height);
        if (!map.TryInvert(out var inverse)) return false;
        using var original = DeepPixels.From(asset.WorkingImage);
        using var result = DeepPixels.From(asset.WorkingImage);
        using var sampled = Sample(document, layer, brush.SampleAll);
        using var softened = brush.Mode is BrushMode.Blur or BrushMode.Sharpen
            ? DeepAdjustments.Blur(sampled, brush.Mode == BrushMode.Blur ? Math.Max(.5, brush.BlurRadius / Math.Max(.001, layer.Transform.Width / asset.Width)) : 1, true) : null;
        var before = MemoryMarshal.Cast<byte, float>(original.GetPixelSpan());
        var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        var source = MemoryMarshal.Cast<byte, float>(sampled.GetPixelSpan());
        var soft = softened is null ? default : MemoryMarshal.Cast<byte, float>(softened.GetPixelSpan());
        using var selection = FillEdits.Coverage(document, out var region);
        var coverage = MemoryBudget.Allocate<float>(asset.Width * asset.Height);
        var prior = points[0]; var changed = false; long work = 0;
        using var tip = brush.TipImage is null ? null : DeepPixels.From(brush.TipImage);
        using var carried = brush.Mode == BrushMode.Smudge ? DeepPixels.From(result) : null;
        var carry = carried is null ? default : MemoryMarshal.Cast<byte, float>(carried.GetPixelSpan());
        foreach (var dab in BrushDabs.Generate(points, brush))
        {
            var radius = (float)(dab.Radius * 1.42 + 1);
            var box = SKRectI.Intersect(SKRectI.Ceiling(inverse.MapRect(SKRect.Create(dab.At.X - radius, dab.At.Y - radius, radius * 2, radius * 2))), SKRectI.Create(asset.Width, asset.Height));
            work += (long)box.Width * box.Height;
            if (work > 300_000_000) throw new PaintStrokeLimitException("The stroke exceeds the interactive work limit. Increase spacing or reduce the brush size.");
            double Control(BrushControl control) => BrushDabs.Control(control, dab.Sample, dab.Index, options.FadeSteps, dab.Direction, dab.InitialDirection);
            var angle = dab.Angle * Math.PI / 180;
            for (var y = box.Top; y < box.Bottom; y++) for (var x = box.Left; x < box.Right; x++)
            {
                var at = map.MapPoint(x + .5f, y + .5f); var dx = at.X - dab.At.X; var dy = at.Y - dab.At.Y;
                var tx = (dx * Math.Cos(angle) + dy * Math.Sin(angle)) / dab.Radius * (dab.FlipX ? -1 : 1);
                var ty = (-dx * Math.Sin(angle) + dy * Math.Cos(angle)) / (dab.Radius * Math.Max(.01, dab.Roundness)) * (dab.FlipY ? -1 : 1);
                var shape = BrushDabs.Tip(brush.Painting?.Tip ?? 0, tx, ty, brush.Hardness);
                if (tip is not null)
                {
                    var px = (int)((tx + 1) * .5 * tip.Width); var py = (int)((ty + 1) * .5 * tip.Height);
                    shape = px < 0 || py < 0 || px >= tip.Width || py >= tip.Height ? 0 : brush.TipImage!.GetPixel(px, py).Alpha / 255d;
                }
                if (options.DualEnabled)
                {
                    double second = 0; var phase = dab.Distance % Math.Max(.25, options.DualSize * options.DualSpacing);
                    for (var n = 0; n < options.DualCount; n++) { var scatter = options.DualScatter * options.DualSize; var sx = options.DualBothAxes ? (BrushDabs.Random(brush.Seed, dab.Index, 100 + n) * 2 - 1) * scatter : 0; var sy = (BrushDabs.Random(brush.Seed, dab.Index, 120 + n) * 2 - 1) * scatter; var xx = (dx - sx - phase) / (options.DualSize / 2); var yy = (dy - sy) / (options.DualSize / 2); second = Math.Max(second, BrushDabs.Tip(options.DualTip, options.DualFlip ? -xx : xx, yy, 1)); }
                    shape *= BrushBlending.Channel((float)second, (float)shape, options.DualMode);
                }
                if (options.TextureEnabled)
                {
                    var grainX = (options.TextureEachTip ? dx : at.X) / options.TextureScale; var grainY = (options.TextureEachTip ? dy : at.Y) / options.TextureScale;
                    double grain;
                    if (texture is not null) { var c = texture.GetPixel(Mod((int)Math.Floor(grainX), texture.Width), Mod((int)Math.Floor(grainY), texture.Height)); grain = (.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue) / 255; }
                    else grain = options.Texture switch { 1 => (Math.Sin(grainX * .8) + Math.Sin(grainY * .8) + 2) / 4, 2 => (Math.Sin(grainX * .2 + Math.Sin(grainY * .3)) + 1) / 2, 3 => (Mod((int)Math.Floor(grainX / 8), 2) + Mod((int)Math.Floor(grainY / 8), 2)) % 2, _ => BrushDabs.Random(71, (int)Math.Floor(grainX) + (int)Math.Floor(grainY) * 32749, 0) };
                    grain = Math.Clamp((grain - .5) * (1 + options.TextureContrast * 3) + .5 + options.TextureBrightness, 0, 1); if (options.TextureInvert) grain = 1 - grain;
                    var depth = Math.Max(options.TextureMinimumDepth, options.TextureDepth * Control(options.TextureControl) * (1 - options.TextureJitter * BrushDabs.Random(brush.Seed, dab.Index, 140)));
                    shape *= options.TextureMode switch { 1 => 1 - depth + depth * grain, 2 => 1 - depth + depth * Math.Min(shape, grain), 3 => 1 - depth + depth * Math.Max(shape, grain), 4 => 1 - depth + depth * Math.Abs(shape - grain), 5 => 1 - depth + depth * BrushBlending.Channel((float)shape, (float)grain, PaintBlend.ColorBurn), _ => Math.Clamp(1 - depth + grain, 0, 1) };
                }
                if (options.Noise) shape *= BrushDabs.Random(brush.Seed, x + y * asset.Width, dab.Index + 170);
                if (options.WetEdges) { var r = Math.Sqrt(tx * tx + ty * ty); shape *= .35 + .65 * Math.Clamp(r * r, 0, 1); }
                var selected = FillEdits.Amount(selection, region, at);
                var power = Math.Clamp(brush.Strength * (brush.PressureStrength ? dab.Pressure : 1), 0, 1);
                var amount = Math.Clamp(shape * dab.Flow * dab.Opacity * power, 0, 1);
                if (amount <= 0 || selected <= 0) continue;
                var pixel = y * asset.Width + x; var i = pixel * 4;
                var old = Read(before, i); var color = Read(source, i); var rgb = new Vector3(color.Red, color.Green, color.Blue);
                if (brush.Mode == BrushMode.Smudge)
                {
                    var from = inverse.MapPoint(at + prior - dab.At);
                    var sx = Math.Clamp((int)from.X, 0, asset.Width - 1); var sy = Math.Clamp((int)from.Y, 0, asset.Height - 1);
                    color = Read(carry, (sy * asset.Width + sx) * 4);
                    if (brush.SampleAll && color.Alpha == 0) color = Read(source, (sy * asset.Width + sx) * 4);
                    if (dab.Index == 0 && brush.FingerPaint) color = new SKColorF((float)brush.Red, (float)brush.Green, (float)brush.Blue, 1);
                    var under = Read(output, i); var a = amount * brush.Opacity * selected;
                    var mixed = BrushBlending.Composite(under, color, a, brush.EffectBlend);
                    Write(output, i, brush.SampleAll || brush.FingerPaint ? mixed : new(mixed.Red, mixed.Green, mixed.Blue, under.Alpha));
                }
                else
                {
                    if (brush.Mode == BrushMode.Blur) { color = Read(soft, i); rgb = new(color.Red, color.Green, color.Blue); }
                    else if (brush.Mode == BrushMode.Sharpen)
                    {
                        var smooth = Read(soft, i); var detail = rgb - new Vector3(smooth.Red, smooth.Green, smooth.Blue);
                        var gate = brush.ProtectDetails ? Math.Clamp(detail.Length() * 16, 0, 1) : 1;
                        rgb += detail * (3 * gate);
                        if (brush.ProtectDetails) rgb = Vector3.Clamp(rgb, Vector3.Zero, new Vector3(Math.Max(1, Math.Max(color.Red, Math.Max(color.Green, color.Blue)))));
                    }
                    else if (brush.Mode is BrushMode.Dodge or BrushMode.Burn)
                    {
                        var lum = Math.Clamp(Vector3.Dot(rgb, new(.2126f, .7152f, .0722f)), 0, 1);
                        var tone = brush.ToneRange switch { 0 => (1 - lum) * (1 - lum), 2 => lum * lum, _ => 4 * lum * (1 - lum) };
                        var exponent = MathF.Pow(2, (brush.Mode == BrushMode.Dodge ? -1 : 1) * tone);
                        var edited = new Vector3(MathF.Pow(Math.Max(0, rgb.X), exponent), MathF.Pow(Math.Max(0, rgb.Y), exponent), MathF.Pow(Math.Max(0, rgb.Z), exponent));
                        rgb = brush.ProtectTones ? BrushBlending.Color(rgb, edited, PaintBlend.Luminosity) : edited;
                    }
                    else if (brush.Mode == BrushMode.Sponge)
                    {
                        var gray = Vector3.Dot(rgb, new(.2126f, .7152f, .0722f));
                        var saturation = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)) - Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
                        var vibrance = brush.Vibrance ? 1 - Math.Clamp(saturation, 0, 1) : 1;
                        rgb = new Vector3(gray) + (rgb - new Vector3(gray)) * (1 + (brush.Saturate ? 1 : -1) * vibrance);
                    }
                    var total = Math.Max(coverage[pixel], Math.Min(dab.Opacity, coverage[pixel] + (1 - coverage[pixel]) * amount)); coverage[pixel] = (float)total;
                    var mixed = BrushBlending.Composite(old, new SKColorF(rgb.X, rgb.Y, rgb.Z, color.Alpha), total * brush.Opacity * selected, brush.EffectBlend);
                    Write(output, i, brush.SampleAll ? mixed : new(mixed.Red, mixed.Green, mixed.Blue, old.Alpha));
                }
                changed = true;
            }
            if (carried is not null) for (var y = box.Top; y < box.Bottom; y++)
                output.Slice((y * asset.Width + box.Left) * 4, box.Width * 4).CopyTo(carry.Slice((y * asset.Width + box.Left) * 4, box.Width * 4));
            prior = dab.At;
        }
        return changed && ImageAdjustments.SetPixels(document, layer, result, selection: false);
    }
    private static SKBitmap Sample(CanvasDocument document, ImageLayer layer, bool all)
    {
        if (!all) return DeepPixels.From(layer.Asset!.WorkingImage);
        using var rendered = DocumentRenderer.RenderDeep(document);
        var result = Bitmaps.Allocate(DeepPixels.Info(layer.Asset!.Width, layer.Asset.Height)); result.Erase(SKColors.Transparent);
        var map = BrushEdits.PixelToDocument(layer.Transform, result.Width, result.Height); map.TryInvert(out var inverse);
        using var canvas = new SKCanvas(result); canvas.SetMatrix(inverse); canvas.DrawBitmap(rendered, 0, 0, new SKSamplingOptions(SKFilterMode.Linear)); return result;
    }
    private static int Mod(int n,int d)=>(n%d+d)%d;
    private static SKColorF Read(ReadOnlySpan<float> p, int i) => new(p[i], p[i + 1], p[i + 2], p[i + 3]);
    private static void Write(Span<float> p, int i, SKColorF c) { p[i] = c.Red; p[i + 1] = c.Green; p[i + 2] = c.Blue; p[i + 3] = c.Alpha; }
}
