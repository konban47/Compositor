using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private ImportedImage? _adjustmentOverlaySource;
    private WriteableBitmap? _adjustmentOverlayImage;
    private void DrawAdjustmentBrushOverlay(DrawingContext context, Func<SKPoint, Point> screen)
    {
        if (_tool != Tool.AdjustmentBrush || !_options.AdjustmentOverlay || PropertyLayer is not { Adjustment: not null, Mask: { } mask } layer) return;
        if (!ReferenceEquals(_adjustmentOverlaySource, mask.Asset))
        {
            _adjustmentOverlayImage?.Dispose(); _adjustmentOverlaySource = mask.Asset;
            var scale = Math.Min(1d, 1024d / Math.Max(mask.Asset.Width, mask.Asset.Height));
            var w = Math.Max(1, (int)(mask.Asset.Width * scale)); var h = Math.Max(1, (int)(mask.Asset.Height * scale));
            using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(w, h));
            using (var canvas = new SKCanvas(source)) canvas.DrawBitmap(mask.Asset.Image, SKRect.Create(w, h), new SKSamplingOptions(SKFilterMode.Linear));
            using var tint = Bitmaps.Allocate(Bitmaps.ColorInfo(w, h));
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) { var color = source.GetPixel(x, y); tint.SetPixel(x, y, new SKColor(255, 48, 72, (byte)(color.Red * .45))); }
            _adjustmentOverlayImage = CanvasView.ToImage(tint);
        }
        if (_adjustmentOverlayImage is not { } image) return;
        var map = BrushEdits.PixelToDocument(layer.MaskTransform, image.PixelSize.Width, image.PixelSize.Height);
        var a = screen(map.MapPoint(0, 0)); var b = screen(map.MapPoint(1, 0)); var c = screen(map.MapPoint(0, 1));
        using (context.PushTransform(new Matrix(b.X - a.X, b.Y - a.Y, c.X - a.X, c.Y - a.Y, a.X, a.Y))) context.DrawImage(image, new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height));
    }
}
