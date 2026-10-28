using System.Text.Json;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class PrismFringeTests
{
    [Fact]
    public void NonDefaultFringeRequiresVersion30AndSurvivesProjectRoundTrip()
    {
        using var document = new Compositor.Core.Model.CanvasDocument(Guid.NewGuid(), 16, 16);
        Compositor.Core.Document.LayerPlacement.AddAdjustment(document, Compositor.Core.Format.AdjustmentKind.LensEffects, null);
        document.Layers[0].Adjustment!.FilterLayerSettings = Compositor.Core.Format.FilterLayerSettings.From(
            new FilterSettings { Lens = new() { Effect = LensEffect.Prism, PrismFringe = 72 } }, 16, 16);
        using var snapshot = Compositor.Core.IO.ProjectSnapshot.FromDocument(document);
        Assert.Equal(30, snapshot.Manifest.Version);
        Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest);
        using var loaded = snapshot.ToDocument();
        Assert.Equal(72, loaded.Layers[0].Adjustment!.FilterLayerSettings!.Lens.PrismFringe);
        snapshot.Manifest.Version = 29;
        Assert.Throws<Compositor.Core.IO.ProjectException>(() => Compositor.Core.IO.ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact]
    public void ExistingSettingsRetainTheOriginalFringeAndNewValuesRoundTrip()
    {
        var original = JsonSerializer.Deserialize<LensEffectSettings>("{}")!;
        Assert.Equal(30, original.PrismFringe);
        original.PrismFringe = 72;
        Assert.Equal(72, JsonSerializer.Deserialize<LensEffectSettings>(JsonSerializer.Serialize(original))!.PrismFringe);
    }

    [Theory]
    [InlineData(-1)] [InlineData(101)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidFringeIsRejected(double fringe) => Assert.False(new LensEffectSettings { PrismFringe = fringe }.IsValid);

    [Fact]
    public void ZeroFringePreservesAchromaticColorsWhileFringeSeparatesChannels()
    {
        using var source = new SKBitmap(256, 128); source.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(source)) { using var paint = new SKPaint { Color = SKColors.White }; canvas.DrawRect(100, 30, 12, 60, paint); }
        var settings = new LensEffectSettings { Effect = LensEffect.Prism, PrismFringe = 0, PrismBlur = 0, PrismAngle = 0 };
        using var crisp = LensEffects.Apply(source, settings);
        Assert.All(crisp.Pixels, color => { Assert.Equal(color.Red, color.Green); Assert.Equal(color.Red, color.Blue); });
        using var separated = LensEffects.Apply(source, settings with { PrismFringe = 100 });
        Assert.Contains(separated.Pixels, color => color.Red != color.Blue);
    }
}
