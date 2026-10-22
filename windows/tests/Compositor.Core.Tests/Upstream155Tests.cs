using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public class Upstream155Tests
{
    [Theory]
    [InlineData("Solid", "Square")]
    [InlineData("Dotted", "Square")]
    [InlineData("Dashed", "Round")]
    [InlineData("Long Dash", "Round")]
    public void LinePatternAndCapsSurviveSavingAndRedrawing(string pattern, string cap)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 100, 10);
        var style = new LayerShapeStyle { Kind = ShapeKind.Line, Red = 1, LineWidth = 1, LineStyle = pattern, LineCap = cap,
            Start = new JsonPoint(.05, .5), End = new JsonPoint(.95, .5) };
        ShapeEdits.Add(document, style, SKRectI.Create(100, 10), null);
        var svg = LayerWorkflow.Svg(document, [document.Layers[0].ID]);
        Assert.Contains("<path", svg); Assert.DoesNotContain("<image", svg);
        var snapshot = ProjectSnapshot.FromDocument(document);
        Assert.Equal(25, snapshot.Manifest.Version);
        snapshot.Manifest.Version = 15; ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version = 14; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        snapshot.Manifest.Version = 25;
        var path = Path.Combine(Path.GetTempPath(), $"line-{Guid.NewGuid()}.comp");
        try
        {
            ProjectStore.Save(snapshot, path);
            using var restored = ProjectStore.Load(path);
            var loaded = Assert.Single(restored.Manifest.Layers).Shape!;
            Assert.Equal(pattern, loaded.LineStyle); Assert.Equal(cap, loaded.LineCap);
            using var pixels = ShapeEdits.Image(loaded, 100, 10)!;
            Assert.True(document.Layers[0].Asset!.Image.GetPixelSpan().SequenceEqual(pixels.GetPixelSpan()));
            Assert.Contains(Enumerable.Range(0, 100), x => pixels.GetPixel(x, 5).Alpha > 0);
            Assert.Equal(0, pixels.GetPixel(50, 4).Alpha); Assert.Equal(0, pixels.GetPixel(50, 6).Alpha);
            if (pattern == "Solid") Assert.Equal(255, pixels.GetPixel(50, 5).Alpha);
            else Assert.Contains(Enumerable.Range(10, 80), x => pixels.GetPixel(x, 5).Alpha == 0);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public void OldLineMetadataRemainsLegacyAndInvalidPatternsAreRejected()
    {
        var style = new LayerShapeStyle { Kind = ShapeKind.Line, LineWidth = 3 };
        Assert.True(style.IsValid); Assert.Null(style.LineCap);
        style.Kind = ShapeKind.Rectangle; style.LineCap = "Round"; Assert.False(style.IsValid);
        style.Kind = ShapeKind.Line; style.LineStyle = "Unknown"; Assert.False(style.IsValid);
        style.LineStyle = "Solid"; style.LineWidth = double.NaN; Assert.False(style.IsValid);
        Assert.True(ShapeLineStroke.Reach(new(0, 0), new(10, 10), 8, "Square") > 4);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MouseAlwaysRetainsOriginalBrushPixels(bool size, bool opacity)
    {
        using var expected = Paint(new BrushSettings(Diameter: 16, Hardness: .5));
        using var actual = Paint(new BrushSettings(Diameter: 16, Hardness: .5, PressureSize: size, PressureOpacity: opacity));
        Assert.True(expected.Layers[0].Asset!.Image.GetPixelSpan().SequenceEqual(actual.Layers[0].Asset!.Image.GetPixelSpan()));
    }

    [Fact]
    public void PenPressureInterpolatesDiameterAndOpacityAndDoesNotSaveSamples()
    {
        var brush = new BrushSettings(Diameter: 20, PressureSize: true, PressureOpacity: true, Pressures: [0.2f, 1]);
        using var doc = Paint(brush);
        var bitmap = doc.Layers[0].Asset!.Image;
        Assert.InRange(bitmap.GetPixel(20, 32).Alpha, 30, 100);
        Assert.True(bitmap.GetPixel(100, 32).Alpha > 230);
        Assert.Equal(0, bitmap.GetPixel(20, 38).Alpha);
        Assert.True(bitmap.GetPixel(100, 38).Alpha > 0);
        Assert.DoesNotContain("Pressures", System.Text.Json.JsonSerializer.Serialize(brush));
        Assert.Equal((1d, 1d), brush.Dynamics(double.NaN));
        Assert.Equal((.05, 0d), brush.Dynamics(0));
    }

    [Fact]
    public void UniformProgressiveBlurMatchesOneGaussianPass()
    {
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(48, 32)); source.Erase(SKColors.Blue);
        source.SetPixel(24, 16, SKColors.Red);
        using var expected = DeepAdjustments.Blur(source, 3, false);
        using var actual = ProgressiveBlur.Apply(source, new FilterSettings { ProgressiveFrom = 3, ProgressiveTo = 3 });
        Assert.True(expected.GetPixelSpan().SequenceEqual(actual.GetPixelSpan()));
    }

    [Fact]
    public void LowPressureUsesSmallDabSpacingEvenWithALargeBrush()
    {
        using var doc = Paint(new BrushSettings(Diameter: 2000, PressureSize: true, Pressures: [.001f, .001f]));
        for (var x = 21; x < 104; x++) Assert.True(doc.Layers[0].Asset!.Image.GetPixel(x, 32).Alpha > 0);
    }

    private static CanvasDocument Paint(BrushSettings brush)
    {
        var doc = new CanvasDocument(Guid.NewGuid(), 128, 64);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(128, 64)); image.Erase(SKColors.Transparent);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Paint"), new LayerTransform(0, 0, 128, 64), "Paint");
        doc.Layers.Add(layer); BrushEdits.Paint(doc, layer.ID, [new(20, 32), new(105, 32)], brush);
        return doc;
    }
}
