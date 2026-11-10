using Compositor.Core.Format;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>Upstream #335: a stroke effect reaches the same distance in every direction, so a circle's stroke is even all the way round.</summary>
public sealed class StrokeReachTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACirclesStrokeIsEvenAllTheWayRound(bool inside)
    {
        using var circle = new SKBitmap(new SKImageInfo(80, 80, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(circle)) { canvas.Clear(SKColors.Transparent); using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true }; canvas.DrawCircle(40, 40, 30, paint); }
        using var raster = EffectRasterizer.Render(circle, new LayerEffects { Stroke = new StrokeEffect { Size = 6, Inside = inside, Opacity = 1 } })!;
        var pixels = raster.Pixels; var center = pixels.Width / 2.0;
        double Ring(double dx, double dy)
        {
            var length = Math.Sqrt(dx * dx + dy * dy); var count = 0.0;
            for (var step = 0.0; step < center; step += .25)
            {
                var x = (int)(center + step * dx / length); var y = (int)(center + step * dy / length);
                if (x < 0 || y < 0 || x >= pixels.Width || y >= pixels.Height) break;
                var c = pixels.GetPixel(x, y); if (c.Alpha > 200 && c.Red < 60) count += .25;
            }
            return count;
        }
        Assert.InRange(Ring(1, 0), 4.5, 7.5);
        Assert.InRange(Math.Abs(Ring(1, 0) - Ring(1, 1)), 0, 1.25);
    }
}