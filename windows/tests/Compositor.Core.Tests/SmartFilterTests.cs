using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class SmartFilterTests : ProjectTestBase
{
    private static (CanvasDocument Document, Guid Smart, byte[] Plain) Striped()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 60, 40);
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(48, 32));
        for (var y = 0; y < 32; y++) for (var x = 0; x < 48; x++) bitmap.SetPixel(x, y, x / 4 % 2 == 0 ? SKColors.Black : SKColors.White);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Stripes"), new LayerTransform(4, 4, 48, 32), "Stripes"));
        var smart = LayerWorkflow.ConvertToSmartObject(document, [document.Layers[0].ID])!.Value;
        return (document, smart, document.Layers.Single(l => l.ID == smart).Asset!.Image.Bytes.ToArray());
    }

    [Fact]
    public void FiltersRedrawTheContentsAndComeOffAgain()
    {
        var (document, smart, plain) = Striped();
        using var _ = document;
        ImageLayer Layer() => document.Layers.Single(l => l.ID == smart);
        Assert.True(SmartFilterEdits.Add(document, smart, FilterKind.GaussianBlur, new FilterSettings { BlurRadius = 3 }));
        Assert.True(SmartFilterEdits.Add(document, smart, FilterKind.AddNoise, new FilterSettings()));
        Assert.Equal([FilterKind.GaussianBlur, FilterKind.AddNoise], Layer().SmartFilters!.Items.Select(f => f.Kind));
        Assert.False(Layer().Asset!.Image.Bytes.AsSpan().SequenceEqual(plain));

        // Off as a whole shows the contents untouched; each change is a new stack, so undo keeps the old one.
        var before = Layer().SmartFilters;
        Assert.True(SmartFilterEdits.SetStackEnabled(document, smart, false));
        Assert.NotSame(before, Layer().SmartFilters);
        Assert.True(Layer().Asset!.Image.Bytes.AsSpan().SequenceEqual(plain));
        Assert.True(SmartFilterEdits.SetStackEnabled(document, smart, true));
        Assert.True(SmartFilterEdits.SetBlending(document, smart, 0, LayerBlendMode.Multiply, .5));
        Assert.Equal((LayerBlendMode.Multiply, .5), (Layer().SmartFilters!.Items[0].Mode, Layer().SmartFilters!.Items[0].Opacity));
        Assert.True(SmartFilterEdits.Move(document, smart, 1, 0));
        Assert.Equal(FilterKind.AddNoise, Layer().SmartFilters!.Items[0].Kind);
        Assert.True(SmartFilterEdits.Delete(document, smart, 0));
        Assert.True(SmartFilterEdits.Delete(document, smart, 0));
        Assert.Null(Layer().SmartFilters);
        Assert.True(Layer().Asset!.Image.Bytes.AsSpan().SequenceEqual(plain));
    }

    [Fact]
    public void LightingFiltersAndPlainLayersAreRefused()
    {
        var (document, smart, _) = Striped();
        using var _ = document;
        Assert.False(SmartFilterEdits.Add(document, smart, FilterKind.RimLight, new FilterSettings()));
        Assert.False(SmartFilterEdits.Add(document, smart, FilterKind.VolumetricLight, new FilterSettings()));
        var plain = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Bitmaps.Allocate(Bitmaps.ColorInfo(4, 4)), "Plain"), new LayerTransform(0, 0, 4, 4), "Plain");
        document.Layers.Add(plain);
        Assert.False(SmartFilterEdits.Add(document, plain.ID, FilterKind.GaussianBlur, new FilterSettings()));
    }

    [Fact]
    public void FiltersAreSavedAsVersion36AndLoadBack()
    {
        var (document, smart, _) = Striped();
        using var _ = document;
        Assert.True(SmartFilterEdits.Add(document, smart, FilterKind.GaussianBlur, new FilterSettings { BlurRadius = 7 }));
        Assert.True(SmartFilterEdits.SetMaskEnabled(document, smart, false));
        var snapshot = ProjectSnapshot.FromDocument(document);
        Assert.Equal(36, snapshot.Manifest.Version);
        var path = PathIn("filters.comp"); ProjectStore.Save(snapshot, path);
        using var loaded = ProjectStore.Load(path).ToDocument();
        var stack = loaded.Layers.Single(l => l.ID == smart).SmartFilters!;
        Assert.Equal(FilterKind.GaussianBlur, stack.Items.Single().Kind);
        Assert.Equal(7, stack.Items[0].Settings.BlurRadius);
        Assert.False(stack.MaskEnabled);

        // Without filters the file stays at the version it was before.
        Assert.True(SmartFilterEdits.Clear(document, smart));
        Assert.True(ProjectSnapshot.FromDocument(document).Manifest.Version < 36);
    }

    [Fact]
    public void IntoCmykClipsColorsOutsideThePrintGamut()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(8, 8)); bitmap.Erase(new SKColor(0, 255, 0));
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Green"), new LayerTransform(0, 0, 8, 8), "Green"));
        Assert.True(ImageModeEdits.Convert(document, document.Image with { Mode = ImageMode.CMYK }));
        var converted = document.Layers[0].Asset!.Image.GetPixel(3, 3);
        Assert.Equal(ImageMode.CMYK, document.Image.Mode);
        Assert.True(converted.Green < 250 || converted.Red > 5 || converted.Blue > 5, $"Pure green was not brought into the CMYK gamut: {converted}");
    }
}
