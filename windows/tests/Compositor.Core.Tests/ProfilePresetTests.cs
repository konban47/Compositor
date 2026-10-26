using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;
using Xunit;

namespace Compositor.Core.Tests;
public sealed class ProfilePresetTests
{
    private static CanvasDocument Document()
    {
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(8, 8)); pixels.Erase(new SKColor(145, 95, 65, 190));
        var document = new CanvasDocument(Guid.NewGuid(), 8, 8) { Image = new() { ColorProfile = ColorProfiles.SRGB.ToByteArray() } };
        document.Layers.Add(new(Guid.NewGuid(), ImportedImage.Create(pixels, "sample"), new(0, 0, 8, 8), "sample")); return document;
    }
    [Fact] public void AssignmentPreservesAllLayerDataAndIsReversibleWithoutAccumulatedConversion()
    {
        using var doc = Document(); var asset = doc.Layers[0].Asset; var original = asset!.Image.GetPixelSpan().ToArray(); using var before = DocumentRenderer.Render(doc);
        doc.Image = ProfileAssignment.Assign(doc.Image, ColorProfiles.AdobeRGB1998.ToByteArray(), new());
        using var assigned = DocumentRenderer.Render(doc); Assert.NotEqual(before.GetPixel(3, 3), assigned.GetPixel(3, 3)); Assert.Equal(before.GetPixel(3, 3).Alpha, assigned.GetPixel(3, 3).Alpha);
        Assert.Same(asset, doc.Layers[0].Asset); Assert.Equal(original, asset.Image.GetPixelSpan().ToArray()); Assert.False(DisplaySceneCache.Supports(doc));
        doc.Image = ProfileAssignment.Assign(doc.Image, ColorProfiles.SRGB.ToByteArray(), new()); using var restored = DocumentRenderer.Render(doc); Assert.Equal(before.GetPixelSpan().ToArray(), restored.GetPixelSpan().ToArray());
    }
    [Fact] public void AssignedProfileIsConsistentAcrossTilesDeepPreviewAndPackage()
    {
        using var doc = Document(); doc.Image = ProfileAssignment.Assign(doc.Image, ColorProfiles.AdobeRGB1998.ToByteArray(), new());
        using var whole = DocumentRenderer.Render(doc); using var tile = DocumentRenderer.RenderRegion(doc, SKRectI.Create(2, 2, 3, 3)); Assert.Equal(whole.GetPixel(3, 3), tile.GetPixel(1, 1));
        using var deep = DocumentRenderer.RenderDeep(doc); using var preview = DeepPixels.Preview(deep); Assert.InRange(Math.Abs(whole.GetPixel(3, 3).Red - preview.GetPixel(3, 3).Red), 0, 2);
        using var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(29, snapshot.Manifest.Version); ProjectStore.Validate(snapshot.Manifest);
        using var restored = snapshot.ToDocument(); using var roundtrip = DocumentRenderer.Render(restored); Assert.Equal(whole.GetPixelSpan().ToArray(), roundtrip.GetPixelSpan().ToArray());
        snapshot.Manifest.Version = 28; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
    [Fact] public void UntaggedExportHasNoProfileAndWrongModelIsRejected()
    {
        using var doc = Document(); doc.Image = ProfileAssignment.Assign(doc.Image, null, new()); using var rendered = DocumentRenderer.Render(doc);
        using var encoded = new MagickImage(DeepImageCodec.EncodeMode(rendered, doc.Image, true, 72)); Assert.Null(encoded.GetColorProfile());
        Assert.Throws<InvalidDataException>(() => ProfileAssignment.Assign(doc.Image, ColorProfiles.USWebCoatedSWOP.ToByteArray(), new()));
    }
    [Fact] public void ContourSetsRoundtripRenameDeduplicateAndRejectInvalidSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var defaults = PresetCatalog.Defaults("Contour"); Assert.Equal(12, defaults.Count);
            Assert.Equal(12, PresetCatalog.Append(defaults, defaults).Count);
            var renamed = defaults[2].Rename("My contour"); PresetCatalog.Save("Contour", [renamed], path);
            var loaded = Assert.Single(PresetCatalog.Import("Contour", path)); Assert.Equal(renamed.Name, loaded.Name); Assert.Equal(renamed.Preset!.Contour, loaded.Preset!.Contour);
            Assert.Throws<InvalidDataException>(() => EditorPresets.Save(path, [renamed.Preset with { Contour = [0, double.NaN] }]));
            Assert.Single(PresetCatalog.Import("Contour", path));
            Assert.Throws<InvalidDataException>(() => PresetCatalog.Import("Brush", path));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void ContoursInterpolateAndCannotCarryInvalidRenderData()
    {
        Assert.Equal(.25, ContourLibrary.Evaluate([0, .5, 1], .25), 8); Assert.Equal(1, ContourLibrary.Evaluate([0, 1], 4));
        Assert.False(new StyleEffect { ContourSamples = [0, 2] }.IsValid);
        Assert.True(new StyleEffect { ContourSamples = [0, .5, 1] }.IsValid);
    }
}
