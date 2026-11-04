using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>Upstream 1.6.0–1.6.3 as they reach Windows: Reduce Noise as a filter and a layer, distances, Keep Subject
/// Clear, and undo stopping at what started a document.</summary>
public sealed class Upstream163Tests
{
    /// <summary>A gray field split down the middle by an edge, with even grain over it.</summary>
    private static SKBitmap Noisy(int width = 96, int height = 72, int grain = 24)
    {
        var random = new Random(7);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height));
        var bytes = image.GetPixelSpan();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = (x < width / 2 ? 80 : 180) + random.Next(-grain, grain + 1);
                var i = y * image.RowBytes + x * 4;
                bytes[i] = bytes[i + 1] = bytes[i + 2] = (byte)Math.Clamp(value, 0, 255);
                bytes[i + 3] = 255;
            }
        return image;
    }

    private static double Deviation(SKBitmap image, int left, int right)
    {
        var values = new List<double>();
        for (var y = 8; y < image.Height - 8; y++) for (var x = left; x < right; x++) values.Add(image.GetPixel(x, y).Red);
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    [Fact]
    public void ReduceNoiseSmoothsGrainAndKeepsTheEdge()
    {
        using var image = Noisy();
        var before = Deviation(image, 8, 40);
        AdjustPixels.ReduceNoise(image.GetPixelSpan(), image.Width, image.Height, image.RowBytes, 80, 50, 0);
        var after = Deviation(image, 8, 40);
        Assert.True(after < before * .6, $"grain {before:0.0} → {after:0.0}");
        // The edge stays sharp: a few pixels either side of it are still on their own sides.
        Assert.True(image.GetPixel(44, 36).Red < 110 && image.GetPixel(52, 36).Red > 150, $"{image.GetPixel(44, 36).Red} and {image.GetPixel(52, 36).Red}");
    }

    [Fact]
    public void ACleanPictureIsLeftAlone()
    {
        using var image = Noisy(grain: 0);
        using var original = image.Copy();
        AdjustPixels.ReduceNoise(image.GetPixelSpan(), image.Width, image.Height, image.RowBytes, 100, 50, 0);
        Assert.True(Math.Abs(image.GetPixel(20, 20).Red - original.GetPixel(20, 20).Red) <= 1);
        Assert.Equal(original.GetPixel(70, 30).Red, image.GetPixel(70, 30).Red);
    }

    [Fact]
    public void ReduceNoiseAsAFilterFollowsItsSettings()
    {
        Assert.True(new FilterSettings().IsValid(FilterKind.ReduceNoise));
        Assert.True(new FilterSettings().DoesAnything(FilterKind.ReduceNoise));
        Assert.False(new FilterSettings { ReduceNoiseLuminance = 0, ReduceNoiseColor = 0 }.DoesAnything(FilterKind.ReduceNoise));
        Assert.False(new FilterSettings { ReduceNoiseDetail = 101 }.IsValid(FilterKind.ReduceNoise));
        using var document = new CanvasDocument(Guid.NewGuid(), 96, 72);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Noisy(), "Photo"), new Model.LayerTransform(0, 0, 96, 72), "Photo");
        document.Layers.Add(layer);
        var before = Deviation(layer.Asset!.Image, 8, 40);
        Assert.True(FilterEdits.Apply(document, layer.ID, FilterKind.ReduceNoise, new FilterSettings()));
        Assert.True(Deviation(document.Layers[0].Asset!.Image, 8, 40) < before);
    }

    [Fact]
    public void ReduceNoiseAsALayerSmoothsWhatsUnderIt()
    {
        var adjustment = new LayerAdjustment { Kind = AdjustmentKind.ReduceNoise, FilterLayerSettings = FilterLayerSettings.From(new FilterSettings { ReduceNoiseLuminance = 80 }, 96, 72) };
        Assert.True(adjustment.IsFilterLayer && adjustment.NeedsVersion33 && adjustment.IsValid);
        Assert.Equal(24, adjustment.SamplingMargin);
        Assert.Equal("Reduce Noise", LayerPlacement.Name(AdjustmentKind.ReduceNoise));
        using var image = Noisy();
        var before = Deviation(image, 8, 40);
        AdjustmentOperators.Apply(adjustment, 1, image.GetPixelSpan(), image.Width, image.Height, image.RowBytes);
        Assert.True(Deviation(image, 8, 40) < before * .6);
    }

    [Fact]
    public void ReduceNoiseLayerSettingsReadAsUpstreamWritesThem()
    {
        // As docs/project-format.md has it: a `reduceNoise` object inside `filterLayerSettings`.
        var written = JsonSerializer.Serialize(FilterLayerSettings.From(new FilterSettings { ReduceNoiseLuminance = 30, ReduceNoiseDetail = 40, ReduceNoiseColor = 60 }, 10, 10), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"reduceNoise\":{\"strength\":30,\"details\":40,\"color\":60}", written);
        var local = new FilterLayerSettings { ReduceNoise = new() { Strength = 30, Details = 40, Color = 60 } }.Local(10, 10);
        Assert.Equal((30, 40, 60), (local.ReduceNoiseLuminance, local.ReduceNoiseDetail, local.ReduceNoiseColor));
        Assert.False(new FilterLayerSettings { ReduceNoise = new() { Color = 200 } }.IsValid);
    }

    /// <summary>Its strength comes from the noise measured over the whole picture, so tiles have to agree with it.</summary>
    [Fact]
    public void AReduceNoiseLayerRendersIdenticallyInTiles()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 96, 72);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Noisy(), "Photo"), new Model.LayerTransform(0, 0, 96, 72), "Photo"));
        LayerPlacement.AddAdjustment(document, AdjustmentKind.ReduceNoise, document.Layers[0].ID);
        using var whole = Compositor.Core.Rendering.DocumentRenderer.Render(document);
        foreach (var region in new[] { SKRectI.Create(0, 0, 48, 72), SKRectI.Create(48, 0, 48, 72) })
        {
            using var tile = Compositor.Core.Rendering.DocumentRenderer.RenderRegion(document, region);
            for (var y = 0; y < region.Height; y++) for (var x = 0; x < region.Width; x++) Assert.Equal(whole.GetPixel(x + region.Left, y), tile.GetPixel(x, y));
        }
    }

    /// <summary>A kept result never stands in for a different picture or different settings.</summary>
    [Fact]
    public void AKeptResultIsOnlyUsedForTheSamePictureAndSettings()
    {
        LayerAdjustment Layer(double strength) => new() { Kind = AdjustmentKind.ReduceNoise, FilterLayerSettings = FilterLayerSettings.From(new FilterSettings { ReduceNoiseLuminance = strength }, 96, 72) };
        byte[] Run(LayerAdjustment layer, SKBitmap source)
        {
            using var copy = source.Copy();
            AdjustmentOperators.Apply(layer, 1, copy.GetPixelSpan(), copy.Width, copy.Height, copy.RowBytes);
            return copy.GetPixelSpan().ToArray();
        }
        using var first = Noisy(); using var second = Noisy(grain: 12);
        var strong = Run(Layer(90), first);
        Assert.Equal(strong, Run(Layer(90), first));
        Assert.NotEqual(strong, Run(Layer(20), first));
        Assert.NotEqual(strong, Run(Layer(90), second));
        Assert.Equal(strong, Run(Layer(90), first));
    }

    [Fact]
    public void AReduceNoiseLayerNeedsTheVersionThatHasIt()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 16, 16);
        LayerPlacement.AddAdjustment(document, AdjustmentKind.ReduceNoise, null);
        using var snapshot = Compositor.Core.IO.ProjectSnapshot.FromDocument(document);
        Assert.Equal(33, snapshot.Manifest.Version);
        Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest);
        using var loaded = snapshot.ToDocument();
        Assert.Equal(AdjustmentKind.ReduceNoise, loaded.Layers[0].Adjustment!.Kind);
        // Upstream's own version for it reads too; one that predates it doesn't.
        snapshot.Manifest.Version = 21;
        Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest);
        snapshot.Manifest.Version = 29;
        Assert.Throws<Compositor.Core.IO.ProjectException>(() => Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest));
    }

    [Fact]
    public void WhatStartsADocumentIsWhereItsHistoryStarts()
    {
        var history = new DocumentHistory();
        using var document = new CanvasDocument(Guid.NewGuid(), 10, 10);
        history.Begin("Open", null, null);
        history.End(document, null);
        Assert.False(history.CanUndo);
        history.Begin("Edit", document, null);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 10, 10), "Layer"));
        history.End(document, null);
        Assert.Equal(1, history.UndoCount);
        Assert.NotNull(history.Undo()?.Document);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void DistancesWhileMovingReachTheNearestEdgeLevelWithTheBox()
    {
        var box = SKRect.Create(100, 100, 50, 50);
        var level = SKRect.Create(200, 110, 40, 20);
        var above = SKRect.Create(120, 10, 10, 10);
        var off = SKRect.Create(170, 400, 10, 10);
        var lines = LayerDistances.Moving(box, [level, above, off], new SKSize(500, 300));
        Assert.Contains(new DistanceLine(true, 150, 200, 120), lines);
        Assert.Contains(new DistanceLine(true, 0, 100, 125), lines);
        Assert.Contains(new DistanceLine(false, 20, 100, 125), lines);
        Assert.Contains(new DistanceLine(false, 150, 300, 125), lines);
        Assert.Equal(4, lines.Count);
        Assert.Empty(LayerDistances.Moving(SKRect.Create(0, 0, 500, 300), [], new SKSize(500, 300)));
    }

    [Fact]
    public void DistancesBetweenLayersAreTheGapsOrTheInsets()
    {
        var box = SKRect.Create(100, 100, 50, 50);
        Assert.Equal([new DistanceLine(true, 150, 200, 125)], LayerDistances.Between(box, SKRect.Create(200, 110, 40, 30)));
        // One inside the other: each side to the same side.
        var around = LayerDistances.Between(box, SKRect.Create(90, 80, 100, 100));
        Assert.Equal(4, around.Count);
        Assert.Contains(new DistanceLine(true, 90, 100, 125), around);
        Assert.Contains(new DistanceLine(false, 150, 180, 125), around);
    }

    [Fact]
    public void KeepSubjectClearOffLetsTheSubjectTakeItsOwnDepth()
    {
        const int w = 200, h = 200;
        var outline = new float[w * h]; var values = new float[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                outline[y * w + x] = x is >= 80 and < 120 && y is >= 80 and < 120 ? 1 : 0;
                values[y * w + x] = .2f;
            }
        var depth = new DepthMap(values, w, h);
        Assert.True(VolumetricLighting.ComposedDepth(depth, outline, w, h, 400)[100 * w + 100] >= .9f);
        Assert.InRange(VolumetricLighting.ComposedDepth(depth, outline, w, h, 400, keepsSubjectClear: false)[100 * w + 100], .15f, .25f);
    }

    /// <summary>Taking away a box dragged past the canvas leaves no hairline of marching ants along the edge
    /// (upstream 6793557, ead9a0e). Skia's path operations don't leave Core Graphics' slivers; this holds them to that.</summary>
    [Fact]
    public void TakingAwayABoxPastTheCanvasLeavesNoSliver()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 200, 100);
        SelectionEdits.SelectAll(document);
        using var builder = new SKPathBuilder(); builder.AddRect(SKRect.Create(-20, -20, 120, 140)); using var box = builder.Detach();
        SelectionEdits.Apply(document, box, SelectionMode.Subtract);
        var path = document.Selection.Path!;
        Assert.Equal(SKRect.Create(100, 0, 100, 100), path.Bounds);
        using var measure = new SKPathMeasure(path, true);
        var contours = 0;
        do { contours++; } while (measure.NextContour());
        Assert.Equal(1, contours);
    }
}
