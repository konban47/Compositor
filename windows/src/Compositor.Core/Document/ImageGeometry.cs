using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class ImageGeometry
{
    public static bool Rotate(CanvasDocument document, double degrees)
    {
        if (!double.IsFinite(degrees) || Math.Abs(degrees) > 36000 || degrees % 360 == 0) return false;
        var angle = degrees * Math.PI / 180; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var width = (int)Math.Ceiling(Math.Abs(document.Width * cos) + Math.Abs(document.Height * sin) - 1e-8);
        var height = (int)Math.Ceiling(Math.Abs(document.Height * cos) + Math.Abs(document.Width * sin) - 1e-8);
        if (width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide || (long)width * height > DocumentLimits.MaxSurfacePixels) return false;
        var rotation = SKMatrix.CreateRotationDegrees((float)(degrees % 360));
        if (degrees % 90 == 0)
        { rotation.ScaleX = rotation.ScaleY = (float)Math.Round(cos); rotation.SkewX = (float)-Math.Round(sin); rotation.SkewY = (float)Math.Round(sin); }
        var matrix = SKMatrix.Concat(SKMatrix.CreateTranslation(width / 2f, height / 2f),
            SKMatrix.Concat(rotation, SKMatrix.CreateTranslation(-document.Width / 2f, -document.Height / 2f)));
        Model.LayerTransform Transform(Model.LayerTransform t) { var p = matrix.MapPoint((float)t.CenterX, (float)t.CenterY); return t with { X = p.X - t.Width / 2, Y = p.Y - t.Height / 2, Rotation = (t.Rotation + degrees) % 360 }; }
        if (document.Layers.Any(l => !Transform(l.Transform).IsValid || l.Mask?.Placement is { } p && !Transform(p).IsValid)) return false;
        foreach (var l in document.Layers)
        {
            l.Transform = Transform(l.Transform);
            if (l.Mask is { Placement: { } p } mask) { l.Mask = mask.Replacing(mask.Asset); l.Mask.Placement = Transform(p); }
        }
        ChannelEdits.Transform(document, width, height, matrix); DocumentMarks.Transform(document, matrix);
        document.Selection = document.Selection.Transformed(matrix, width, height);
        if (Math.Abs(degrees % 90) > 1e-8) document.Guides.Clear();
        else foreach (var guide in document.Guides)
        {
            var vertical = guide.Axis == GuideAxis.Vertical; var p = matrix.MapPoint(vertical ? (float)guide.Position : 0, vertical ? 0 : (float)guide.Position);
            if (Math.Abs(degrees % 180) > 1e-8) guide.Axis = vertical ? GuideAxis.Horizontal : GuideAxis.Vertical;
            guide.Position = guide.Axis == GuideAxis.Vertical ? p.X : p.Y;
        }
        document.Width = width; document.Height = height; return true;
    }
    public static bool RevealAll(CanvasDocument document)
    {
        var bounds = new SKRect(0, 0, document.Width, document.Height);
        foreach (var layer in document.Layers.Where(l => l.Asset is not null))
        {
            var matrix = BrushEdits.PixelToDocument(layer.Transform, layer.Asset!.Width, layer.Asset.Height);
            var box = matrix.MapRect(new SKRect(0, 0, layer.Asset.Width, layer.Asset.Height)); bounds = SKRect.Union(bounds, box);
        }
        return CanvasEdits.Crop(document, new SKRectI((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top), (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom)));
    }
}
