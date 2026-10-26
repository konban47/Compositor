using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class Upstream1510Tests
{
    [Fact] public void NativeNestedFilterSettingsUseUpstreamNamesAndKeepNonDefaultValues()
    {
        var settings=System.Text.Json.JsonSerializer.Deserialize<FilterLayerSettings>("""
            {"vignette":{"amount":78,"color":{"red":0.2,"green":0.3,"blue":0.4},"midpoint":25,"roundness":88,"feather":45,"highlights":10},
             "tonal":{"amount":70,"radius":12,"shadows":20,"midtones":80,"highlights":40},"sharpen":{"amount":175,"radius":2},
             "lens":{"effect":"Anamorphic Flare","bokehShape":"Cat's Eye"}}
            """,ManifestJson.Options)!;
        Assert.True(settings.IsValid);Assert.Equal(78,settings.VignetteAmount);Assert.Equal(.3,settings.VignetteGreen);Assert.Equal(12,settings.TonalRadius);Assert.Equal(175,settings.SharpenAmount);
        var json=System.Text.Json.JsonSerializer.Serialize(settings,ManifestJson.Options);var loaded=System.Text.Json.JsonSerializer.Deserialize<FilterLayerSettings>(json,ManifestJson.Options)!;
        Assert.Equal(settings,loaded);Assert.DoesNotContain("vignetteAmount",json);
    }
    [Theory]
    [InlineData(AdjustmentKind.Vignette)]
    [InlineData(AdjustmentKind.TonalContrast)]
    [InlineData(AdjustmentKind.Sharpen)]
    [InlineData(AdjustmentKind.LensEffects)]
    public void FilterLayersRenderIdenticallyInTilesAndSurviveProjects(AdjustmentKind kind)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 48);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(64, 48));
        for (var y = 0; y < 48; y++) for (var x = 0; x < 64; x++) pixels.SetPixel(x, y, new((byte)(x * 4), (byte)(y * 5), (byte)(((x / 7 + y / 5) % 2) * 220), 255));
        document.Layers.Add(new(Guid.NewGuid(), ImportedImage.Create(pixels, "Image"), new(0, 0, 64, 48), "Image"));
        using var before = DocumentRenderer.Render(document);
        LayerPlacement.AddAdjustment(document, kind, document.Layers[0].ID);
        var adjustment = document.Layers.Single(l => l.Adjustment is not null).Adjustment!;
        adjustment.FilterLayerSettings = FilterLayerSettings.From(new FilterSettings { Lens = new() { Effect = LensEffect.Mist }, SharpenAmount = 150 }, 64, 48);
        using var whole = DocumentRenderer.Render(document);
        Assert.False(before.GetPixelSpan().SequenceEqual(whole.GetPixelSpan()));
        foreach (var region in new[] { SKRectI.Create(0, 0, 32, 48), SKRectI.Create(32, 0, 32, 48) })
        {
            using var tile = DocumentRenderer.RenderRegion(document, region);
            for (var y = 0; y < region.Height; y++) for (var x = 0; x < region.Width; x++) Assert.Equal(whole.GetPixel(x + region.Left, y), tile.GetPixel(x, y));
        }
        using var snapshot = ProjectSnapshot.FromDocument(document); Assert.Equal(29, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        using var loaded = snapshot.ToDocument(); using var rendered = DocumentRenderer.Render(loaded); Assert.True(whole.GetPixelSpan().SequenceEqual(rendered.GetPixelSpan()));
        snapshot.Manifest.Version = 28; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact] public void UnsharpPreservesAlphaAndRejectsNonfiniteSettings()
    {
        using var image = new SKBitmap(Bitmaps.ColorInfo(3, 1)); image.SetPixel(0, 0, new(80, 80, 80, 128)); image.SetPixel(1, 0, new(150, 150, 150, 128)); image.SetPixel(2, 0, new(170, 170, 170, 128));
        var alpha = Enumerable.Range(0, 3).Select(x => image.GetPixel(x, 0).Alpha).ToArray();
        SharpenPixels.Apply(image, 150, 1);
        Assert.Equal(alpha, Enumerable.Range(0, 3).Select(x => image.GetPixel(x, 0).Alpha));
        Assert.False(new FilterSettings { SharpenRadius = double.NaN }.IsValid(FilterKind.Sharpen));
        Assert.False(new FilterLayerSettings { TonalAmount = double.NaN }.IsValid);
    }
}
