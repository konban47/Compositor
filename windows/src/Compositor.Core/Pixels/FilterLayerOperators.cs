using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Pixels;
internal static class FilterLayerOperators
{
    /// <summary>The largest picture whose Reduce Noise is kept: the input and the result, each this many bytes.</summary>
    private const long KeptBytes = 512L << 20;
    private static readonly object KeptGate = new();
    private static (byte[] Input, byte[] Output, int Width, double Strength, double Details, double Color)? _kept;

    /// <summary>
    /// Reduce Noise is too slow to work out again for every tile and pan, and the tiles of one frame all hand it the same
    /// whole picture, so the last result is kept with exactly what it was made from: what's under the layer changing, or
    /// its settings, works it out afresh. As upstream keeps a Reduce Noise layer's picture while what's under it matches.
    /// </summary>
    private static void KeptReduceNoise(SKBitmap image, double strength, double details, double color)
    {
        var pixels = image.GetPixelSpan(); var width = image.Width; var stride = image.RowBytes;
        lock (KeptGate)
        {
            if (_kept is { } kept && kept.Width == width && kept.Strength == strength && kept.Details == details && kept.Color == color && pixels.SequenceEqual(kept.Input))
            {
                kept.Output.CopyTo(pixels);
                return;
            }
            var input = pixels.Length <= KeptBytes ? pixels.ToArray() : null;
            AdjustPixels.ReduceNoise(pixels, width, image.Height, stride, strength, details, color);
            _kept = input is null ? null : (input, pixels.ToArray(), width, strength, details, color);
        }
    }

    internal static void Apply(LayerAdjustment adjustment, Span<byte> rgba, int width, int height, int stride, long ox, long oy, int canvasWidth, int canvasHeight)
    {
        var f = adjustment.FilterLayer;
        if (canvasWidth < 1) canvasWidth = width; if (canvasHeight < 1) canvasHeight = height;
        f = f with { ProgressiveStart = f.ProgressiveStart ?? new JsonPoint(canvasWidth * .5, canvasHeight * .35), ProgressiveEnd = f.ProgressiveEnd ?? new JsonPoint(canvasWidth * .5, canvasHeight), RgbSplitCenter = f.RgbSplitCenter ?? new JsonPoint(canvasWidth * .5, canvasHeight * .5) };
        var settings = f.Local(width, height, ox, oy);
        using var image = Bitmaps.Allocate(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++) rgba.Slice(y * stride, width * 4).CopyTo(image.GetPixelSpan().Slice(y * image.RowBytes, width * 4));
        if (adjustment.Kind == AdjustmentKind.Sharpen)
            SharpenPixels.Apply(image, f.SharpenAmount, f.SharpenRadius);
        else if (adjustment.Kind == AdjustmentKind.ReduceNoise)
            KeptReduceNoise(image, f.ReduceNoiseStrength, f.ReduceNoiseDetails, f.ReduceNoiseColor);
        else if (adjustment.Kind == AdjustmentKind.Vignette)
            AdjustPixels.ColoredVignette(image.GetPixelSpan(), width, height, image.RowBytes, -ox, -oy, canvasWidth, canvasHeight, false,
                f.VignetteAmount, f.VignetteMidpoint, f.VignetteRoundness, f.VignetteFeather, f.VignetteHighlights, f.VignetteRed, f.VignetteGreen, f.VignetteBlue);
        else if (adjustment.Kind == AdjustmentKind.TonalContrast)
        {
            using var blurred = image.Copy();
            GaussianBlur.Clamped(blurred.GetPixelSpan(), width, height, 4, blurred.RowBytes, f.TonalRadius);
            AdjustPixels.TonalContrast(image.GetPixelSpan(), blurred.GetPixelSpan(), width, height, image.RowBytes, blurred.RowBytes,
                f.TonalAmount, f.TonalShadows, f.TonalMidtones, f.TonalHighlights);
        }
        else if (adjustment.Kind == AdjustmentKind.LensEffects)
        {
            using var filtered = LensEffects.Apply(image, f.Lens);
            using var canvas = new SKCanvas(image); canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(filtered, new SKPoint(), new SKSamplingOptions(SKFilterMode.Nearest)); canvas.Flush();
        }
        else if (adjustment.Kind == AdjustmentKind.BloomGlow)
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
