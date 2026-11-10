using Compositor.Core.IO;
using ImageMagick;

namespace Compositor.Core.Tests;

[Collection("Color globals")]
public sealed class CustomColorProfilesTests
{
    private static MagickImage Colors(params byte[][] rgb)
    {
        var image = new MagickImage(); image.ReadPixels(rgb.SelectMany(c => c).ToArray(), new PixelReadSettings((uint)rgb.Length, 1, StorageType.Char, "RGB")); image.ColorSpace = ColorSpace.sRGB; return image;
    }

    [Theory] [InlineData(ColorEngine.LittleCms)] [InlineData(ColorEngine.AceCompatible)]
    public void CustomRgbLikeSrgbKeepsWhiteAndGrays(ColorEngine engine)
    {
        var bytes = CustomColorProfiles.Rgb("Simplified sRGB", 2.2, .3127, .3290, [.64, .33, .30, .60, .15, .06]);
        Assert.True(IccProfiles.Valid(bytes)); Assert.Equal("RGB", IccProfiles.Model(bytes)); Assert.Equal("Simplified sRGB", IccProfiles.Name(bytes, ""));
        using var input = Colors([255, 255, 255], [128, 128, 128], [200, 40, 40]);
        using var output = ColorTransforms.Convert(input, ColorProfiles.SRGB.ToByteArray(), bytes, new ColorManagementSettings { Engine = engine, Intent = ColorIntent.Relative });
        var pixels = output.GetPixels().ToShortArray("RGB")!.Select(v => v / 257d).ToArray();
        Assert.All(pixels[..3], v => Assert.InRange(v, 250, 255));
        Assert.All(pixels[3..6], v => Assert.InRange(v, 118, 138));
        Assert.True(pixels[6] > pixels[7] + 100);
    }

    [Fact] public void PrimariesThatMissTheWhiteAreRefused() =>
        Assert.Throws<ArgumentException>(() => CustomColorProfiles.Rgb("Bad", 2.2, .9, .05, [.64, .33, .30, .60, .15, .06]));

    [Theory] [InlineData(ColorEngine.LittleCms)] [InlineData(ColorEngine.AceCompatible)]
    public void CustomCmykPrintsPaperWhiteBlankAndKeepsInkLimits(ColorEngine engine)
    {
        var settings = new CustomCmykSettings { TotalLimit = 280, BlackLimit = 95 };
        var bytes = CustomColorProfiles.Cmyk(settings);
        Assert.True(IccProfiles.Valid(bytes)); Assert.Equal("CMYK", IccProfiles.Model(bytes));
        using var input = Colors([255, 255, 255], [0, 0, 0], [128, 128, 128], [230, 30, 40]);
        using var output = ColorTransforms.Convert(input, ColorProfiles.SRGB.ToByteArray(), bytes, new ColorManagementSettings { Engine = engine, Intent = ColorIntent.Relative, BlackPointCompensation = true });
        var ink = output.GetPixels().ToShortArray("CMYK")!.Select(v => v / 65535d).ToArray();
        Assert.All(ink[..4], v => Assert.InRange(v, 0, .03));
        Assert.InRange(ink[4] + ink[5] + ink[6] + ink[7], 2.2, 2.84);
        Assert.InRange(ink[7], .6, .97);
        Assert.True(ink[13] > .6 && ink[14] > .6 && ink[12] < .25, "red should print with magenta and yellow");
    }

    [Fact]
    public void BlackGenerationDecidesHowMuchBlackANeutralGets()
    {
        double MidK(BlackGeneration black) => CustomColorProfiles.GrayRamp(new CustomCmykSettings { Black = black }, 33)[16][3];
        Assert.Equal(0, MidK(BlackGeneration.None), 3);
        Assert.True(MidK(BlackGeneration.Light) < MidK(BlackGeneration.Medium));
        Assert.True(MidK(BlackGeneration.Medium) < MidK(BlackGeneration.Heavy));
        Assert.True(MidK(BlackGeneration.Heavy) < MidK(BlackGeneration.Maximum));
        var ramp = CustomColorProfiles.GrayRamp(new CustomCmykSettings(), 33);
        Assert.All(ramp.Zip(ramp.Skip(1)), p => Assert.True(p.Second[3] >= p.First[3] - .02, "black should climb toward the shadows"));
        Assert.All(ramp.Zip(ramp.Skip(1)), p => Assert.True(p.Second[0] >= p.First[0] - .08, "cyan should not collapse where black comes in"));
    }

    [Fact]
    public void DotGainCurveFillsBlanksBetweenGivenPoints()
    {
        var curve = Enumerable.Repeat(double.NaN, 13).ToArray(); curve[8] = 70;
        var settings = new CustomCmykSettings { GainCurve = curve };
        Assert.Equal(.70, CustomColorProfiles.Gain(settings, .5), 3);
        Assert.InRange(CustomColorProfiles.Gain(settings, .25), .3, .4);
        Assert.Equal(.70, CustomColorProfiles.Gain(new CustomCmykSettings { DotGain = 20 }, .5), 3);
    }
}
