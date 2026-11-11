using Compositor.Core.Format;
using LayerTransform = Compositor.Core.Model.LayerTransform;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class ContentFillOptionsTests
{
    private static (CanvasDocument Document, ImageLayer Layer) Painted(int width, int height, Func<int, int, SKColor> color)
    {
        var pixels = new SKBitmap(Bitmaps.ColorInfo(width, height));
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) pixels.SetPixel(x, y, color(x, y));
        var document = new CanvasDocument(Guid.NewGuid(), width, height);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Picture"), new LayerTransform(0, 0, width, height), "Picture");
        document.Layers.Add(layer); return (document, layer);
    }
    private static byte[] Area(int width, int height, Func<int, int, bool> inside)
    {
        var area = new byte[width * height]; for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (inside(x, y)) area[y * width + x] = 255; return area;
    }

    [Fact]
    public void TheFillOnlyCopiesFromTheSamplingArea()
    {
        var (document, layer) = Painted(60, 40, (x, _) => x < 30 ? new SKColor(220, 30, 30) : new SKColor(30, 30, 220));
        using var _ = document;
        SelectionEdits.Select(document, SKRectI.Create(24, 14, 12, 12));
        Assert.True(ContentFillEdits.Apply(document, layer.ID, new ContentFillOptions { Sampling = Area(60, 40, (x, _) => x >= 40), ColorAdaptation = 0 }));
        var image = layer.Asset!.Image;
        for (var y = 15; y < 25; y++) for (var x = 25; x < 35; x++) Assert.True(image.GetPixel(x, y).Blue > 150, $"({x},{y}) is {image.GetPixel(x, y)}");
    }

    [Fact]
    public void ColorAdaptationPullsTheFillTowardItsSurroundings()
    {
        // A grey field with a bright patch in the corner; the hole sits in the grey, and only the bright patch may be copied.
        static SKColor Field(int x, int y) => x >= 44 && y >= 28 ? new SKColor(210, 210, 210) : new SKColor(90, 90, 90);
        double Mean(int adaptation)
        {
            var (document, layer) = Painted(64, 48, Field); using var _ = document;
            SelectionEdits.Select(document, SKRectI.Create(10, 10, 12, 12));
            Assert.True(ContentFillEdits.Apply(document, layer.ID, new ContentFillOptions { Sampling = Area(64, 48, (x, y) => x >= 44 && y >= 28), ColorAdaptation = adaptation }));
            var image = layer.Asset!.Image; double sum = 0; for (var y = 12; y < 20; y++) for (var x = 12; x < 20; x++) sum += image.GetPixel(x, y).Red; return sum / 64;
        }
        var none = Mean(0); var high = Mean(2);
        Assert.InRange(none, 180, 255);
        Assert.True(high < none - 60, $"adapted {high}, not adapted {none}");
    }

    [Fact]
    public void ASourcePictureFillsANewLayerOnlyInsideTheSelection()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 40, 40); using var _ = document;
        var empty = new SKBitmap(Bitmaps.ColorInfo(40, 40)); empty.Erase(SKColors.Transparent);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(empty, "Fill"), new LayerTransform(0, 0, 40, 40), "Fill"); document.Layers.Add(layer);
        using var source = new SKBitmap(Bitmaps.ColorInfo(40, 40)); source.Erase(new SKColor(20, 160, 60));
        SelectionEdits.Select(document, SKRectI.Create(15, 15, 10, 10));
        Assert.True(ContentFillEdits.Apply(document, layer.ID, new ContentFillOptions { Source = source, ColorAdaptation = 0 }));
        var image = layer.Asset!.Image;
        Assert.True(image.GetPixel(20, 20).Green > 120 && image.GetPixel(20, 20).Alpha == 255);
        Assert.Equal(0, image.GetPixel(5, 5).Alpha);
    }

    [Fact]
    public void ANewFillLayerOverAPictureLeavesThePictureShowingAroundTheFill()
    {
        var (document, layer) = Painted(100, 100, (x, y) => new SKColor(30, (byte)(120 + x % 20), 40)); using var _ = document;
        SelectionEdits.Select(document, SKRectI.Create(40, 40, 20, 20));
        using var source = Rendering.DocumentRenderer.Render(document);
        var empty = new SKBitmap(Bitmaps.ColorInfo(100, 100)); empty.Erase(SKColors.Transparent);
        var added = LayerPlacement.AddImage(document, empty, SKRectI.Create(100, 100), "Fill", layer.ID)!.Value;
        Assert.True(ContentFillEdits.Apply(document, added, new ContentFillOptions { Source = source, Sampling = ContentFillEdits.Sampling(document, false, source) }));
        using var region = Rendering.DocumentRenderer.RenderRegion(document, SKRectI.Create(25, 25, 50, 50));
        Assert.True(region.GetPixel(2, 2).Green > 100, $"corner {region.GetPixel(2, 2)}");
        Assert.True(region.GetPixel(25, 25).Green > 100, $"centre {region.GetPixel(25, 25)}");
    }

    [Fact]
    public void RotationScaleAndMirrorStillFillTheHole()
    {
        var (document, layer) = Painted(48, 48, (x, y) => (x / 6 + y / 6) % 2 == 0 ? new SKColor(200, 200, 60) : new SKColor(40, 80, 160));
        using var _ = document;
        SelectionEdits.Select(document, SKRectI.Create(18, 18, 12, 12));
        Assert.True(ContentFillEdits.Apply(document, layer.ID, new ContentFillOptions { RotationAdaptation = 4, Scale = true, Mirror = true }));
        Assert.Equal(255, layer.Asset!.Image.GetPixel(24, 24).Alpha);
        Assert.Equal(10, Pixels.ContentFill.Copies(48, 48, 4, true, true).Count);
    }

    [Fact]
    public void AutoSamplingIsABandAroundTheSelectionAndNeverTheSelection()
    {
        var (document, _) = Painted(100, 100, (_, _) => SKColors.Gray); using var __ = document;
        SelectionEdits.Select(document, SKRectI.Create(40, 40, 20, 20));
        var auto = ContentFillEdits.Sampling(document, false); var box = ContentFillEdits.Sampling(document, true);
        Assert.Equal(0, auto[50 * 100 + 50]); Assert.Equal(255, auto[50 * 100 + 35]); Assert.Equal(0, auto[2 * 100 + 2]);
        Assert.Equal(255, box[50 * 100 + 30]); Assert.Equal(0, box[50 * 100 + 50]);
        // Measured against a layer that is transparent near the hole, the band widens toward pixels it can copy.
        using var layerAlone = new SKBitmap(Bitmaps.ColorInfo(100, 100)); layerAlone.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(layerAlone)) canvas.DrawRect(0, 0, 100, 10, new SKPaint { Color = SKColors.Red });
        using var opaque = new SKBitmap(Bitmaps.ColorInfo(100, 100)); opaque.Erase(SKColors.Gray);
        var plenty = ContentFillEdits.Sampling(document, false, opaque); Assert.Equal(0, plenty[2 * 100 + 2]); Assert.Equal(255, plenty[50 * 100 + 35]);
        var widened = ContentFillEdits.Sampling(document, false, layerAlone);
        Assert.Equal(255, widened[5 * 100 + 50]); Assert.Equal(0, widened[50 * 100 + 50]);
    }
}

public sealed class ContentFillSamplingShapeTests
{
    [Fact]
    public void AWideDocumentKeepsItsFarEdgesOutOfTheAutoBand()
    {
        var pixels = new SKBitmap(Bitmaps.ColorInfo(240, 160)); pixels.Erase(SKColors.Gray);
        using var document = new CanvasDocument(Guid.NewGuid(), 240, 160);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "P"), new LayerTransform(0, 0, 240, 160), "P"));
        SelectionEdits.Select(document, SKRectI.Create(90, 60, 50, 40));
        using var opaque = new SKBitmap(Bitmaps.ColorInfo(240, 160)); opaque.Erase(SKColors.Gray);
        var band = ContentFillEdits.Sampling(document, false, opaque);
        Assert.Equal(0, band[80 * 240 + 5]); Assert.Equal(255, band[80 * 240 + 70]);
        // The same area given as raster coverage, as the workspace hands it over, makes the same band.
        var gray = new SKBitmap(new SKImageInfo(240, 160, SKColorType.Gray8, SKAlphaType.Opaque)); var span = gray.GetPixelSpan();
        for (var y = 60; y < 100; y++) for (var x = 90; x < 140; x++) span[y * gray.RowBytes + x] = 255;
        document.Selection = DocumentSelection.FromCoverage(gray);
        var raster = ContentFillEdits.Sampling(document, false, opaque);
        Assert.Equal(0, raster[80 * 240 + 5]); Assert.Equal(255, raster[80 * 240 + 70]); Assert.Equal(0, raster[80 * 240 + 100]);
    }
}

public sealed class ContentFillQualityTests
{
    [Fact]
    public void AHoleInRegularStripesIsFilledCloseToTheStripesThatWereThere()
    {
        static SKColor Stripe(int x, int y) => (x / 6 + y / 9) % 2 == 0 ? new SKColor(210, 170, 70) : new SKColor(50, 100, 150);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(96, 72));
        for (var y = 0; y < 72; y++) for (var x = 0; x < 96; x++) pixels.SetPixel(x, y, Stripe(x, y));
        using var document = new CanvasDocument(Guid.NewGuid(), 96, 72);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Stripes"), new LayerTransform(0, 0, 96, 72), "Stripes"); document.Layers.Add(layer);
        SelectionEdits.Select(document, SKRectI.Create(36, 24, 24, 24));
        Assert.True(ContentFillEdits.Apply(document, layer.ID, new ContentFillOptions()));
        double error = 0; var image = layer.Asset!.Image;
        for (var y = 24; y < 48; y++) for (var x = 36; x < 60; x++) { var a = image.GetPixel(x, y); var b = Stripe(x, y); error += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue); }
        Assert.InRange(error / (24 * 24 * 3), 0, 30);
    }
}

public sealed class PhotoshopModeAndStackTests
{
    private static byte[] Pixel(byte r, byte g, byte b, byte a) => [r, g, b, a];

    [Fact]
    public void DarkerAndLighterColorKeepTheWholeColorWithLowerOrHigherLuminosity()
    {
        var source = Pixel(255, 0, 0, 255); var backdrop = Pixel(0, 0, 255, 255); var result = new byte[4];
        Compositor.Core.Pixels.BlendModes.CompositePixel(Compositor.Core.Pixels.BlendMode.DarkerColor, source, backdrop, result);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, result);
        Compositor.Core.Pixels.BlendModes.CompositePixel(Compositor.Core.Pixels.BlendMode.LighterColor, source, backdrop, result);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, result);
    }

    [Fact]
    public void DissolveTurnsHalfCoverageIntoOpaqueDotsAboutHalfTheTime()
    {
        const int size = 64; var source = new byte[size * size * 4]; var backdrop = new byte[size * size * 4]; var output = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++) { source[i * 4] = 128; source[i * 4 + 3] = 128; backdrop[i * 4 + 2] = 255; backdrop[i * 4 + 3] = 255; }
        Compositor.Core.Pixels.BlendModes.Composite(Compositor.Core.Pixels.BlendMode.Dissolve, source, backdrop, output, size, size, size * 4);
        var red = 0;
        for (var i = 0; i < size * size; i++) { Assert.True(output[i * 4] == 255 && output[i * 4 + 2] == 0 || output[i * 4] == 0 && output[i * 4 + 2] == 255); if (output[i * 4] == 255) red++; }
        Assert.InRange(red, size * size * 35 / 100, size * size * 65 / 100);
    }

    [Fact]
    public void StackModesComputeTheirStatisticPerPixel()
    {
        Assert.Equal(20, SmartObjectStacks.Statistic("Median", new double[] { 10, 20, 250 }));
        Assert.Equal(40, SmartObjectStacks.Statistic("Mean", new double[] { 10, 20, 90 }));
        Assert.Equal(80, SmartObjectStacks.Statistic("Range", new double[] { 10, 20, 90 }));
        Assert.Equal(90, SmartObjectStacks.Statistic("Maximum", new double[] { 10, 20, 90 }));
        // Median of three exposures drops the one that differs: a passer-by is removed.
        using var contents = new CanvasDocument(Guid.NewGuid(), 4, 4);
        foreach (var value in new byte[] { 50, 50, 250 })
        {
            var pixels = new SKBitmap(Bitmaps.ColorInfo(4, 4)); pixels.Erase(new SKColor(value, value, value));
            contents.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Shot"), new LayerTransform(0, 0, 4, 4), "Shot"));
        }
        using var median = SmartObjectStacks.Render(contents, "Median");
        Assert.Equal(50, median.GetPixel(2, 2).Red);
    }

    [Fact]
    public void ANewStyleOptionOrModeNeedsFormat34AndRoundTrips()
    {
        var effect = StyleEffect.PhotoshopDefault(StyleEffectKind.OuterGlow);
        Assert.True(effect.NeedsVersion34 && effect.IsValid);
        var stops = effect.GradientAt(0.5); Assert.InRange(stops.Alpha, 0.45, 0.55);
        var (document, layer) = (new CanvasDocument(Guid.NewGuid(), 8, 8), (ImageLayer?)null);
        using var _ = document;
        var pixels = new SKBitmap(Bitmaps.ColorInfo(8, 8)); pixels.Erase(SKColors.Red);
        layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "L"), new LayerTransform(0, 0, 8, 8), "L") { BlendMode = LayerBlendMode.DarkerColor, Effects = new LayerEffects { Items = [effect] } };
        document.Layers.Add(layer);
        var snapshot = Compositor.Core.IO.ProjectSnapshot.FromDocument(document);
        Assert.Equal(34, snapshot.Manifest.Version);
        Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version = 33;
        Assert.Throws<Compositor.Core.IO.ProjectException>(() => Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest));
    }
}
