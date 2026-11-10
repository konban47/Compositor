using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class SkyReplacementTests
{
    /// <summary>A landscape: a pale blue sky over the top 40%, dark green ground below.</summary>
    private static SKBitmap Landscape()
    {
        var image = new SKBitmap(new SKImageInfo(200, 120, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 120; y++) for (var x = 0; x < 200; x++)
            image.SetPixel(x, y, y < 48 ? new SKColor((byte)(110 + y), (byte)(170 + y / 2), 235) : new SKColor(40, (byte)(90 + (x * 7 + y * 3) % 20), 35));
        return image;
    }

    [Fact]
    public void DetectFindsTheSkyAboveTheGround()
    {
        using var image = Landscape();
        var mask = SkyReplacement.Detect(image);
        Assert.True(mask[10 * 200 + 100] > 200, "the sky was not found");
        Assert.True(mask[100 * 200 + 100] < 40, "the ground was taken for sky");
    }

    [Fact]
    public void ShiftEdgeGrowsAndShrinksTheSky()
    {
        var mask = new byte[100 * 100]; for (var i = 0; i < 50 * 100; i++) mask[i] = 255;
        int Sky(byte[] m) => m.Count(v => v > 127);
        Assert.True(Sky(SkyReplacement.Refine(mask, 100, 100, 100, 0)) > Sky(mask));
        Assert.True(Sky(SkyReplacement.Refine(mask, 100, 100, -100, 0)) < Sky(mask));
    }

    [Fact]
    public void BuildPutsTheNewSkyInTheSkyAndLightsTheForeground()
    {
        using var image = Landscape();
        using var sky = new SKBitmap(64, 40); sky.Erase(new SKColor(250, 120, 40));
        var mask = SkyReplacement.Detect(image);
        var settings = new SkySettings { FadeEdge = 0 };
        using var layers = SkyReplacement.Build(image, mask, sky, settings);
        using var result = SkyReplacement.Composite(image, layers, settings);
        var top = result.GetPixel(100, 5); var bottom = result.GetPixel(100, 110);
        Assert.True(top.Red > 200 && top.Blue < 120, $"sky pixel is {top}");
        Assert.True(bottom.Green > bottom.Red, $"ground pixel is {bottom}");
        Assert.True(layers.Lighting.GetPixel(100, 110).Alpha > 0, "the foreground took no light from the new sky");
        Assert.Equal(0, layers.Lighting.GetPixel(100, 5).Alpha);
    }

    [Fact]
    public void FlipMirrorsTheSkyAndBrightnessLiftsIt()
    {
        using var sky = new SKBitmap(100, 50);
        for (var y = 0; y < 50; y++) for (var x = 0; x < 100; x++) sky.SetPixel(x, y, x < 50 ? new SKColor(200, 0, 0) : new SKColor(0, 0, 100));
        using var plain = SkyReplacement.PlaceSky(sky, 100, 50, new SkySettings());
        using var flipped = SkyReplacement.PlaceSky(sky, 100, 50, new SkySettings { Flip = true });
        using var bright = SkyReplacement.PlaceSky(sky, 100, 50, new SkySettings { Brightness = 100 });
        Assert.True(plain.GetPixel(10, 25).Red > 150 && flipped.GetPixel(10, 25).Blue > 60);
        Assert.True(bright.GetPixel(90, 25).Blue > plain.GetPixel(90, 25).Blue);
    }
}
