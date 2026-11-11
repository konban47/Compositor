using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>The Image menu's additions in 1.6.6.4: gradient-map stops, the fourth auto algorithm, legacy Brightness/Contrast,
/// HDR Toning's methods, Replace Color's shifts, the new channel blends, pixel-replacement placement and the full data points.</summary>
public sealed class ImageMenuTests
{
    private static (CanvasDocument Document, ImageLayer Layer) Picture(Func<int, int, SKColor> color, int width = 8, int height = 8)
    {
        var document = new CanvasDocument(Guid.NewGuid(), width, height);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(width, height));
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) pixels.SetPixel(x, y, color(x, y));
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "L"), new LayerTransform(0, 0, width, height), "L");
        document.Layers.Add(layer);
        return (document, layer);
    }

    [Fact]
    public void AGradientMapOfThreeStopsSendsMiddleGrayToTheMiddleColorAndNeedsFormat35()
    {
        var map = new GradientMapSettings { Stops = [new(0, 0xff000000), new(.5, 0xffff0000), new(1, 0xffffffff)], Method = GradientMethod.Linear };
        Assert.True(map.IsValid && map.NeedsVersion35);
        var middle = map.Sample(.5); Assert.InRange(middle.Red, .99, 1); Assert.InRange(middle.Green, 0, .01);
        var rgba = new byte[] { 128, 128, 128, 255 };
        AdjustmentOperators.Apply(new LayerAdjustment { Kind = AdjustmentKind.GradientMap, GradientMapSettings = map }, 1, rgba, 1, 1, 4);
        Assert.True(rgba[0] > 240 && rgba[1] < 20, $"{rgba[0]},{rgba[1]},{rgba[2]}");
        // Two ends with no method keep the straight line older projects saved.
        Assert.False(new GradientMapSettings().NeedsVersion35);

        var (document, layer) = Picture((_, _) => SKColors.Gray);
        using var _ = document;
        var adjustment = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 8, 8), "Map") { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GradientMap, GradientMapSettings = map } };
        document.Layers.Add(adjustment);
        var snapshot = ProjectSnapshot.FromDocument(document);
        Assert.Equal(35, snapshot.Manifest.Version);
        ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version = 34;
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }

    [Fact]
    public void EnhanceBrightnessAndContrastLiftsADarkPictureWithTheMasterGamma()
    {
        var (document, layer) = Picture((x, y) => new SKColor((byte)(10 + x * 8), (byte)(10 + y * 8), 30));
        using var _ = document;
        var levels = LevelsEdits.Automatic(layer.Asset!.Image, LevelsAuto.Brightness);
        Assert.True(levels.Ranges[0].Gamma > 1, $"gamma {levels.Ranges[0].Gamma}");
        Assert.True(levels.Ranges[1].Black == 0 && levels.Ranges[1].White == 255, "Per-channel ranges should stay untouched.");
        // A larger clip share moves the black and white points in.
        var clipped = LevelsEdits.Automatic(layer.Asset.Image, LevelsAuto.Contrast, .05, .05);
        var plain = LevelsEdits.Automatic(layer.Asset.Image, LevelsAuto.Contrast, 0, 0);
        Assert.True(clipped.Ranges[0].Black >= plain.Ranges[0].Black && clipped.Ranges[0].White <= plain.Ranges[0].White);
    }

    [Fact]
    public void LegacyBrightnessShiftsEveryLevelByTheSameAmount()
    {
        var settings = new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.BrightnessContrast, Values = { ["Brightness"] = 51, ["Legacy"] = 1 } };
        var shadow = ImageAdjustments.Adjust(new Color3(.1, .1, .1), settings); var middle = ImageAdjustments.Adjust(new Color3(.5, .5, .5), settings);
        Assert.Equal(.3, shadow.R, 3); Assert.Equal(.7, middle.R, 3);
        // Without Use Legacy the same brightness keeps black from lifting as far.
        settings.Values["Legacy"] = 0;
        Assert.True(ImageAdjustments.Adjust(new Color3(.1, .1, .1), settings).R < .5);
    }

    [Fact]
    public void HdrToningMethodsDifferAndTheToningCurveIsApplied()
    {
        var gray = new Color3(.25, .25, .25);
        var exposure = new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.HdrToning, Values = { ["Method"] = 0, ["Exposure"] = 1, ["Gamma"] = 1 } };
        Assert.Equal(.5, ImageAdjustments.Adjust(gray, exposure).R, 3);
        var compression = new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.HdrToning, Values = { ["Method"] = 1 } };
        Assert.Equal(.4, ImageAdjustments.Adjust(gray, compression).R, 2);
        // A curve that inverts: the toning curve runs last.
        exposure.Values["CurveCount"] = 2; exposure.Values["Curve0X"] = 0; exposure.Values["Curve0Y"] = 100; exposure.Values["Curve1X"] = 100; exposure.Values["Curve1Y"] = 0;
        Assert.Equal(.5, ImageAdjustments.Adjust(gray, exposure).R, 3);
        Assert.Equal(.25, ImageAdjustments.Adjust(new Color3(.125, .125, .125), exposure).R is var inverted ? 1 - inverted : 0, 2);
    }

    [Fact]
    public void ReplaceColorShiftsTheHueOfTheSampledColorsOnly()
    {
        var settings = new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.ReplaceColor, Color = 0xffff0000, Values = { ["Shift"] = 1, ["Hue"] = 120, ["Fuzziness"] = 40 } };
        var red = ImageAdjustments.Adjust(new Color3(1, 0, 0), settings); Assert.True(red.G > .9 && red.R < .1, $"{red}");
        var blue = ImageAdjustments.Adjust(new Color3(0, 0, 1), settings); Assert.Equal(1, blue.B, 3);
        var same = ImageAdjustments.ShiftHsl(new Color3(.2, .4, .6), 0, 0, 0); Assert.Equal(.2, same.R, 3); Assert.Equal(.6, same.B, 3);
    }

    [Fact]
    public void LinearDodgeAddsAndHardMixPostersizesTwoSources()
    {
        var (document, _) = Picture((_, _) => new SKColor(64, 64, 64));
        using var __ = document;
        var source = new ChannelSource(document, null, ImageChannel.Red);
        using var added = ChannelCalculations.Calculate(source, source, new CalculationSettings(ChannelBlend.LinearDodge));
        Assert.InRange(added.GetPixel(0, 0).Red, 125, 131);
        using var mixed = ChannelCalculations.Calculate(source, source, new CalculationSettings(ChannelBlend.HardMix));
        Assert.Equal(0, mixed.GetPixel(0, 0).Red);
    }

    [Fact]
    public void FitPlacesAReplacementInsideTheLayerBoxAtItsAlignment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Compositor-variables-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using (var wide = new SKBitmap(Bitmaps.ColorInfo(4, 2))) { wide.Erase(SKColors.Blue); using var data = wide.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(Path.Combine(directory, "wide.png"), data.ToArray()); }
            var (document, layer) = Picture((_, _) => SKColors.White);
            using var _ = document;
            document.Variables.Add(new ImageVariable("picture", layer.ID, VariableKind.PixelReplacement, PixelMethod.Fit, 7));
            Assert.True(ImageVariables.Apply(document, new ImageDataSet("one", new() { ["picture"] = "wide.png" }), directory));
            var placed = document.Layers.Single().Transform;
            // 4 x 2 fitted to 8 x 8 is 8 x 4, at the bottom middle.
            Assert.Equal(8, placed.Width, 3); Assert.Equal(4, placed.Height, 3); Assert.Equal(4, placed.Y, 3);
            var snapshot = ProjectSnapshot.FromDocument(document);
            Assert.Equal(35, snapshot.Manifest.Version); snapshot.Manifest.Version = 34;
            Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ARecordedMeasurementCarriesThePhotoshopDataPoints()
    {
        var (document, _) = Picture((x, _) => new SKColor((byte)(x * 30), (byte)(x * 30), (byte)(x * 30)));
        using var __ = document;
        var record = ImageAnalysis.Measure(document, "M", "doc.comp");
        Assert.True(record.GrayMinimum <= record.GrayMedian && record.GrayMedian <= record.GrayMaximum);
        Assert.Equal(64, record.Histogram!.Sum());
        Assert.Equal("doc.comp", ImageAnalysis.Value(document, record, "Document"));
        Assert.Equal("0.785398", ImageAnalysis.Value(document, record, "Circularity"));
        Assert.Equal(23, ImageAnalysis.DataPoints.Length);
        document.Measurements.Add(record); document.MeasurementFields = ImageAnalysis.DataPoints;
        var snapshot = ProjectSnapshot.FromDocument(document);
        Assert.Equal(35, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version = 34; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        Assert.Contains("Integrated Density", ImageAnalysis.Tsv(document, [record]));
    }
}
