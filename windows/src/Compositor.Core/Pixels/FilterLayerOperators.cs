using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Pixels;
internal static class FilterLayerOperators
{
    internal static void Apply(LayerAdjustment adjustment, Span<byte> rgba, int width, int height, int stride, long ox, long oy, int canvasWidth, int canvasHeight)
    {
        var f = adjustment.FilterLayer;
        if (canvasWidth < 1) canvasWidth = width; if (canvasHeight < 1) canvasHeight = height;
        f = f with { ProgressiveStart = f.ProgressiveStart ?? new JsonPoint(canvasWidth * .5, canvasHeight * .35), ProgressiveEnd = f.ProgressiveEnd ?? new JsonPoint(canvasWidth * .5, canvasHeight), RgbSplitCenter = f.RgbSplitCenter ?? new JsonPoint(canvasWidth * .5, canvasHeight * .5) };
        var settings = f.Local(width, height, ox, oy);
        using var image = Bitmaps.Allocate(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++) rgba.Slice(y * stride, width * 4).CopyTo(image.GetPixelSpan().Slice(y * image.RowBytes, width * 4));
        if (adjustment.Kind == AdjustmentKind.BloomGlow)
        {
            using var glow = image.Copy(); BloomPixels.Lift(glow.GetPixelSpan(), glow.GetPixelSpan(), width, height, glow.RowBytes, f.BloomAmount / 50);
            GaussianBlur.ZeroPadded(glow.GetPixelSpan(), width, height, 4, glow.RowBytes, f.BloomRadius);
            BloomPixels.Screen(image.GetPixelSpan(), glow.GetPixelSpan(), width, height, image.RowBytes, f.BloomAmount / 50);
        }
        else
        {
            using var filtered = adjustment.Kind == AdjustmentKind.ProgressiveBlur ? ProgressiveBlur.Apply(image, settings) : SpectralDispersion.Apply(image, settings, ox, oy, canvasWidth, canvasHeight);
            using var canvas = new SKCanvas(image); canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(filtered, new SKPoint(), new SKSamplingOptions(SKFilterMode.Nearest)); canvas.Flush();
        }
        for (var y = 0; y < height; y++) image.GetPixelSpan().Slice(y * image.RowBytes, width * 4).CopyTo(rgba.Slice(y * stride, width * 4));
    }
}
