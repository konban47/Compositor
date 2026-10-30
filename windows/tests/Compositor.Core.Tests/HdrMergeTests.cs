using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>Merge to HDR Pro on bracketed exposures of a made-up scene whose light is known.</summary>
public sealed class HdrMergeTests
{
    private const int Width = 240, Height = 60;

    /// <summary>The scene's light: rising from deep shadow to ten times white across the frame, a little warm.</summary>
    private static (double R, double G, double B) Light(int x, int y)
    {
        var l = .002 * Math.Pow(5000, x / (double)(Width - 1));
        return (l * 1.1, l, l * .85);
    }

    private static double Gamma(double v) => v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
    private static double Linear(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);

    private static SKBitmap Exposure(double exposure, Func<int, int, (double, double, double)?>? replace = null)
    {
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(Width, Height));
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        {
            var (r, g, b) = replace?.Invoke(x, y) ?? Light(x, y);
            byte Value(double v) => (byte)Math.Clamp(Math.Round(Gamma(Math.Min(1, v * exposure)) * 255), 0, 255);
            image.SetPixel(x, y, new SKColor(Value(r), Value(g), Value(b)));
        }
        return image;
    }

    private static float[] Pixel(ImportedImage image, int x, int y)
    {
        var deep = image.Deep!;
        return MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan()).Slice(y * deep.RowBytes / 4 + x * 4, 4).ToArray();
    }

    [Fact]
    public void ExposuresAreWorkedOutFromThePhotos()
    {
        using var dark = Exposure(.125); using var middle = Exposure(1); using var bright = Exposure(8);
        var result = HdrMerge.Merge([("Middle", middle), ("Dark", dark), ("Bright", bright)], new HdrOptions(HdrMode.Bits32, AutoAlign: false));
        using var _ = result.Document;
        Assert.Equal(1, result.Exposures[1], 3);
        Assert.InRange(result.Exposures[0], 8 * .85, 8 * 1.15);
        Assert.InRange(result.Exposures[2], 64 * .8, 64 * 1.2);
    }

    [Fact]
    public void ThirtyTwoBitsKeepTheLightPastWhite()
    {
        using var dark = Exposure(.125); using var middle = Exposure(1); using var bright = Exposure(8);
        var result = HdrMerge.Merge([("Dark", dark), ("Middle", middle), ("Bright", bright)], new HdrOptions(HdrMode.Bits32, AutoAlign: false));
        using var document = result.Document;
        Assert.Equal(32, document.Image.Depth);
        var asset = document.Layers.Single().Asset!;
        Assert.Equal(32, asset.BitDepth);
        foreach (var x in new[] { 30, 90, 150, 200, 230 })
        {
            var expected = Light(x, 30).G;
            var read = Linear(Pixel(asset, x, 30)[1]);
            Assert.InRange(read / expected, .88, 1.12);
        }
        Assert.True(Linear(Pixel(asset, 235, 30)[1]) > 4, "ten times white stays past white");
    }

    [Fact]
    public void EachToningKeepsBothEndsAndTheirOrder()
    {
        using var dark = Exposure(.125); using var middle = Exposure(1); using var bright = Exposure(8);
        foreach (var method in Enum.GetValues<HdrToneMethod>())
        {
            var result = HdrMerge.Merge([("Dark", dark), ("Middle", middle), ("Bright", bright)],
                new HdrOptions(HdrMode.Bits16, AutoAlign: false, Toning: new HdrToning { Method = method, Gamma = 1, Exposure = method == HdrToneMethod.ExposureAndGamma ? -3 : 0 }));
            using var document = result.Document;
            var asset = document.Layers.Single().Asset!;
            Assert.Equal(16, asset.BitDepth);
            var left = Pixel(asset, 3, 30)[1]; var mid = Pixel(asset, 120, 30)[1]; var right = Pixel(asset, 236, 30)[1];
            Assert.True(left < mid && mid < right, $"{method}: {left:0.000} {mid:0.000} {right:0.000}");
            if (method != HdrToneMethod.ExposureAndGamma) Assert.True(right < .999f && left > .005f, $"{method} clipped: {left:0.000} {right:0.000}");
        }
    }

    [Fact]
    public void RemoveGhostsKeepsTheMiddleExposuresReading()
    {
        // Something bright walked through the frame while only the bright exposure was taken.
        (double, double, double)? Walker(int x, int y) => x is >= 100 and < 130 ? (.5, .05, .05) : null;
        using var dark = Exposure(.125); using var middle = Exposure(1); using var bright = Exposure(8, Walker);
        double RedAt(bool ghosts)
        {
            var result = HdrMerge.Merge([("Dark", dark), ("Middle", middle), ("Bright", bright)], new HdrOptions(HdrMode.Bits32, RemoveGhosts: ghosts, AutoAlign: false));
            using var document = result.Document;
            var pixel = Pixel(document.Layers.Single().Asset!, 115, 30);
            return Linear(pixel[0]) / Math.Max(1e-6, Linear(pixel[1]));
        }
        var expected = Light(115, 30).R / Light(115, 30).G;
        Assert.True(Math.Abs(RedAt(true) - expected) < Math.Abs(RedAt(false) - expected) * .5, $"{RedAt(true):0.00} vs {RedAt(false):0.00}, scene {expected:0.00}");
    }

    [Fact]
    public void AutoAlignPutsAShiftedExposureBack()
    {
        using var dark = Exposure(.125); using var middle = Exposure(1); using var bright = Exposure(8);
        // A dark bar to line up on, and the bright exposure taken four pixels to the right.
        foreach (var image in new[] { dark, middle, bright }) for (var y = 0; y < Height; y++) for (var x = 60; x < 64; x++) image.SetPixel(x, y, SKColors.Black);
        using var moved = Bitmaps.Allocate(bright.Info);
        using (var canvas = new SKCanvas(moved)) { canvas.Clear(SKColors.Black); canvas.DrawBitmap(bright, 4, 0, new SKSamplingOptions(SKFilterMode.Nearest)); }
        var result = HdrMerge.Merge([("Dark", dark), ("Middle", middle), ("Bright", moved)], new HdrOptions(HdrMode.Bits32, AutoAlign: true));
        using var document = result.Document;
        var asset = document.Layers.Single().Asset!;
        Assert.True(Pixel(asset, 62, 30)[1] < .02f && Pixel(asset, 66, 30)[1] > Pixel(asset, 62, 30)[1] + .02f, "the bar stays one bar");
    }
}
