using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;
public static class FrameEdits
{
    public static SKPath Path(FrameStyle style, double width, double height)
    {
        using var builder = new SKPathBuilder(); var rect = SKRect.Create((float)width, (float)height);
        switch (style.Shape)
        {
            case 0: builder.AddRoundRect(rect, (float)Math.Min(style.CornerRadius, Math.Min(width, height) / 2), (float)Math.Min(style.CornerRadius, Math.Min(width, height) / 2)); break;
            case 1: builder.AddOval(rect); break;
            case 2: builder.AddPoly([new((float)width / 2, 0), new((float)width, (float)height), new(0, (float)height)], true); break;
            case 3: builder.AddPoly(Enumerable.Range(0, 6).Select(i => new SKPoint((float)(width * (.5 + .5 * Math.Cos(i * Math.PI / 3))), (float)(height * (.5 + .5 * Math.Sin(i * Math.PI / 3))))).ToArray(), true); break;
            case 4:
                using (var path = SKPath.ParseSvgPathData(style.CustomPath!)) { path.Transform(SKMatrix.CreateScale((float)width, (float)height)); builder.AddPath(path); } break;
        }
        return builder.Detach();
    }
    public static bool Apply(ImageLayer layer, FrameStyle style)
    {
        if (layer.Container != LayerContainer.Frame || !style.IsValid) return false;
        var width = Math.Max(1, (int)Math.Ceiling(layer.Transform.Width)); var height = Math.Max(1, (int)Math.Ceiling(layer.Transform.Height));
        using var path = Path(style, width, height);
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(width, height));
        try
        {
            using (var canvas = new SKCanvas(mask)) { canvas.Clear(SKColors.Black); using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true }; canvas.DrawPath(path, paint); }
            layer.Mask = Compositor.Core.Model.LayerMask.AssetFrom(mask); layer.Mask.VectorPath = path.ToSvgPathData(); layer.FrameStyle = style;
            return true;
        }
        catch { mask.Dispose(); throw; }
    }
    public static void Stroke(SKCanvas canvas, ImageLayer layer)
    {
        if (layer.FrameStyle is not { Stroke: { } color, StrokeWidth: > 0 } style) return;
        var t = layer.Transform;
        using var path = Path(style, t.Width, t.Height);
        canvas.Save();
        canvas.Translate((float)t.CenterX, (float)t.CenterY); canvas.RotateDegrees((float)t.Rotation);
        canvas.Scale(t.FlipX ? -1 : 1, t.FlipY ? -1 : 1); canvas.Translate((float)(-t.Width / 2), (float)(-t.Height / 2));
        if (style.StrokeAlignment != 1) canvas.ClipPath(path, style.StrokeAlignment == 0 ? SKClipOperation.Intersect : SKClipOperation.Difference, true);
        using var paint = new SKPaint { Color = new SKColor(color), IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(style.StrokeWidth * (style.StrokeAlignment == 1 ? 1 : 2)), StrokeJoin = SKStrokeJoin.Round };
        canvas.DrawPath(path, paint); canvas.Restore();
    }
}
