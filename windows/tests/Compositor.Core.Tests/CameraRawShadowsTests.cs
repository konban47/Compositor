using Compositor.Core.Document;
using Compositor.Core.Pixels;

namespace Compositor.Core.Tests;

public class CameraRawShadowsTests
{
    // Same deterministic landscape and Photoshop reference measurements as upstream's 1.5.2 tests.
    private static double[] Field(int width, int height, ulong state)
    {
        double Unit() { state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL); return (state >> 11) / (double)(1UL << 53); }
        var result = new double[width * height]; double amplitude = 1, total = 0;
        for (var octave = 0; octave < 7; octave++)
        {
            var cell = Math.Max(1, Math.Max(width, height) >> (octave + 2)); var gw = width / cell + 2; var gh = height / cell + 2;
            var grid = Enumerable.Range(0, gw * gh).Select(_ => Unit()).ToArray();
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var fx = x / (double)cell; var fy = y / (double)cell; var x0 = (int)fx; var y0 = (int)fy; var tx = fx - x0; var ty = fy - y0;
                var top = grid[y0 * gw + x0] * (1 - tx) + grid[y0 * gw + x0 + 1] * tx;
                var bottom = grid[(y0 + 1) * gw + x0] * (1 - tx) + grid[(y0 + 1) * gw + x0 + 1] * tx;
                result[y * width + x] += amplitude * (top * (1 - ty) + bottom * ty);
            }
            total += amplitude; amplitude *= .6;
        }
        return result.Select(v => v / total).ToArray();
    }
    private static byte[] Landscape(int width)
    {
        var height = width * 2 / 3; var clouds = Field(width, height, 1); var lay = Field(width, height, 2); var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = y * width + x; var horizon = height * (.5 + .25 * (lay[x] - .5));
            var sky = 140 + 80 * (1 - y / horizon) + 60 * (clouds[i] - .5);
            var ground = 4 + 70 * Math.Pow(clouds[i], 2.2) + 20 * (lay[i] - .5);
            var value = (byte)Math.Clamp(Math.Round(y < horizon ? sky : ground), 0, 255);
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = value; pixels[i * 4 + 3] = 255;
        }
        return pixels;
    }
    [Theory]
    [InlineData(1200, 100, 59, 205.9, 4)]
    [InlineData(1200, 50, 34.9, 205.3, 4)]
    [InlineData(1200, -100, 4.2, 203.6, 4)]
    [InlineData(600, 100, 51, 205.8, 10)]
    [InlineData(600, 50, 32.2, 205.2, 4)]
    [InlineData(600, -100, 5.5, 203.6, 4)]
    public void LandscapeMatchesUpstreamReference(int width, double amount, double ground, double sky, double tolerance)
    {
        var pixels = Landscape(width); var height = width * 2 / 3;
        AdjustPixels.CameraRawMeasured(pixels, width, height, width * 4, new CameraRawSettings { Shadows = amount });
        double Mean(int first, int last) => Enumerable.Range(first * width, (last - first) * width).Average(i => pixels[i * 4 + 1]);
        Assert.InRange(Mean(height * 4 / 5, height), ground - tolerance, ground + tolerance);
        Assert.InRange(Mean(0, height / 5), sky - 2, sky + 2);
    }
    [Theory]
    [InlineData(100, 0)] [InlineData(-100, 0)] [InlineData(0, 100)] [InlineData(0, -100)]
    public void ShadowsRemainMonotonicAndHighlightsStayWithinTableQuantization(double shadows, double highlights)
    {
        var pixels = Enumerable.Range(0, 256).SelectMany(i => new byte[] { (byte)i, (byte)i, (byte)i, 255 }).ToArray();
        AdjustPixels.CameraRawMeasured(pixels, 256, 1, 1024, new CameraRawSettings { Shadows = shadows, Highlights = highlights });
        // The unchanged compressed Highlights LUT has a one-code-value wobble near clipped white;
        // Shadows is a separate region pass and must remain strictly monotonic.
        for (var x = 1; x < 256; x++) Assert.True(pixels[x * 4] + (highlights > 0 ? 1 : 0) >= pixels[(x - 1) * 4], $"Reversed tone at {x}");
        Assert.InRange(pixels[0], 0, 2); Assert.InRange(pixels[1020], highlights < 0 ? 235 : 253, 255);
        if (shadows == 100) Assert.True(pixels[45 * 4] >= 57);
    }
    [Fact]
    public void ReducedPreviewUsesOriginalLayerSizeAndKeepsAlpha()
    {
        var small = Landscape(300); var preview = (byte[])small.Clone(); var full = new byte[1200 * 800 * 4];
        for (var y = 0; y < 800; y++) for (var x = 0; x < 1200; x++) Array.Copy(small, ((y / 4) * 300 + x / 4) * 4, full, (y * 1200 + x) * 4, 4);
        CameraRawShadows.Apply(small, 300, 200, 1200, 100);
        CameraRawShadows.Apply(preview, 300, 200, 1200, 100, .25);
        CameraRawShadows.Apply(full, 1200, 800, 4800, 100);
        double error = 0; for (var y = 0; y < 200; y++) for (var x = 0; x < 300; x++) error += Math.Abs(preview[(y * 300 + x) * 4] - full[((y * 4 + 2) * 1200 + x * 4 + 2) * 4]);
        Assert.True(error / 60000 < 2); Assert.False(preview.SequenceEqual(small));
        byte[] transparent = [0, 0, 0, 0, 20, 30, 40, 80, 80, 90, 100, 120];
        CameraRawShadows.Apply(transparent, 3, 1, 12, 100);
        Assert.Equal(new byte[] { 0, 80, 120 }, new[] { transparent[3], transparent[7], transparent[11] });
        for (var i = 0; i < 3; i++) Assert.True(transparent[i * 4] <= transparent[i * 4 + 3] && transparent[i * 4 + 1] <= transparent[i * 4 + 3] && transparent[i * 4 + 2] <= transparent[i * 4 + 3]);
    }
}
