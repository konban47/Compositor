using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class EditWorkspaceTests
{
    private static CanvasDocument Solid(SKColor color, int width = 32, int height = 24)
    { var image = new SKBitmap(Bitmaps.ColorInfo(width, height)); image.Erase(color); return ImageImporter.NewDocument(ImportedImage.Create(image, "Layer")); }
    private static SKBitmap Hdr(float red = 3, float green = 1.5f, float blue = .3f)
    {
        var image = Bitmaps.Allocate(DeepPixels.Info(16, 8)); var values = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan());
        for (var i = 0; i < values.Length; i += 4) { values[i] = red; values[i + 1] = green; values[i + 2] = blue; values[i + 3] = 1; }
        return image;
    }
    [Fact] public void ExrRoundTripKeepsAboveWhiteAndAlpha()
    {
        using var source = Hdr(); var values = MemoryMarshal.Cast<byte, float>(source.GetPixelSpan()); values[3] = .4f;
        using var read = DeepImageCodec.Decode(DeepImageCodec.EncodeExr(source), "HDR");
        Assert.Equal(32, read.BitDepth); var pixels = MemoryMarshal.Cast<byte, float>(read.Deep!.GetPixelSpan());
        Assert.InRange(pixels[0], 2.98f, 3.02f); Assert.InRange(pixels[1], 1.48f, 1.52f); Assert.InRange(pixels[3], .399f, .401f);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExternalLinearHdrFilesDecodeToExtendedSrgb(bool radiance)
    {
        using var values = Hdr(.21404114f, .5f, 2f);
        // Read raw linear values as RGB without asking Magick to gamma-convert them.
        using var linear = new ImageMagick.MagickImage(); linear.Settings.ColorSpace = ImageMagick.ColorSpace.RGB;
        linear.ReadPixels(values.GetPixelSpan().ToArray(), new ImageMagick.PixelReadSettings(16, 8, ImageMagick.StorageType.Float, ImageMagick.PixelMapping.RGBA));
        // A hand-authored Radiance RGBE fixture: (14,32,128)*2^(130-128)/256 = (.21875,.5,2).
        var radianceBytes = System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 4\n")
            .Concat(Enumerable.Range(0, 4).SelectMany(_ => new byte[] { 14, 32, 128, 130 })).ToArray();
        using var decoded = DeepImageCodec.Decode(radiance ? radianceBytes : linear.ToByteArray(ImageMagick.MagickFormat.Exr), "Linear");
        var p = MemoryMarshal.Cast<byte, float>(decoded.Deep!.GetPixelSpan());
        Assert.InRange(p[0], .49f, .51f); Assert.InRange(p[1], .73f, .74f); Assert.True(p[2] > 1.3f); Assert.Equal(32, decoded.BitDepth);
    }
    [Fact] public void ExrSdrIsImportedAs16Bit()
    { using var source = Hdr(.9f, .4f, .2f); using var read = DeepImageCodec.Decode(DeepImageCodec.EncodeExr(source), "SDR"); Assert.Equal(16, read.BitDepth); }
    [Fact] public void HdrIdentityAdjustmentDoesNotClip()
    {
        using var source = Hdr(); DeepAdjustments.Adjust(source, new LayerAdjustment { Kind = AdjustmentKind.Levels });
        var pixels = MemoryMarshal.Cast<byte, float>(source.GetPixelSpan()); Assert.InRange(pixels[0], 2.99f, 3.01f); Assert.InRange(pixels[1], 1.49f, 1.51f);
    }
    [Fact] public void HdrConvert16ClampsAndQuantizes()
    {
        using var doc = ImageImporter.NewDocument(ImportedImage.CreateDeep(Hdr(), "HDR", 32));
        Assert.True(DeepPaint.ConvertLayer(doc, doc.Layers[0].ID, 16));
        var p = MemoryMarshal.Cast<byte, float>(doc.Layers[0].Asset!.Deep!.GetPixelSpan()); Assert.Equal(1, p[0]); Assert.InRange(Math.Abs(p[2] * 65535 - Math.Round(p[2] * 65535)), 0, .001);
    }
    [Fact] public void UpstreamExrProjectLoadsAndRejectsAmbiguousSidecar()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Compositor-EXR-" + Guid.NewGuid() + ".comp");
        using var doc = ImageImporter.NewDocument(ImportedImage.CreateDeep(Hdr(), "HDR", 32));
        try
        {
            var snapshot = ProjectSnapshot.FromDocument(doc); ProjectStore.Save(snapshot, folder);
            var path = Path.Combine(folder, "manifest.json"); var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            node["version"] = 13; node.AsObject().Remove("imageSettings"); var layer = node["layers"]![0]!;
            var filename = Path.ChangeExtension(layer["imageFile"]!.GetValue<string>(), "exr"); layer["imageFile"] = filename; layer.AsObject().Remove("floatImageFile");
            File.WriteAllBytes(Path.Combine(folder, "images", filename), DeepImageCodec.EncodeExr(doc.Layers[0].Asset!.Deep!)); File.WriteAllText(path, node.ToJsonString());
            using var loaded = ProjectStore.Load(folder); Assert.Equal(32, loaded.Images[doc.Layers[0].ID].BitDepth);
            ProjectStore.Save(loaded, folder);
            using var saved = ProjectStore.Load(folder); Assert.Equal(32, saved.Images[doc.Layers[0].ID].BitDepth);
            Assert.InRange(MemoryMarshal.Cast<byte, float>(saved.Images[doc.Layers[0].ID].Deep!.GetPixelSpan())[0], 2.97f, 3.03f);
            node["version"] = 18; layer["floatImageFile"] = FloatPixels.FileName(doc.Layers[0].ID); File.WriteAllText(path, node.ToJsonString());
            Assert.Throws<ProjectException>(() => ProjectStore.Load(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Theory] [InlineData(false, 128)] [InlineData(true, 127)]
    public void PasteMaskKeepsSoftCoverage(bool outside, byte expected)
    {
        using var doc = Solid(SKColors.Red); var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(doc.Width, doc.Height)); mask.Erase(new SKColor(128, 128, 128));
        Assert.True(EditWorkspace.MaskPasted(doc, [doc.Layers[0].ID], DocumentSelection.FromCoverage(mask), outside));
        var made = doc.Layers[0].Mask!; Assert.False(made.IsLinked); Assert.Equal(expected, made.Asset.Image.GetPixel(5, 5).Red); Assert.Null(doc.Selection.Path);
    }
    [Fact] public void PasteMaskMultipliesExistingMaskAndDoesNotMutateItsAsset()
    {
        using var doc = Solid(SKColors.Red); var old = Bitmaps.Allocate(Bitmaps.MaskInfo(32, 24)); old.Erase(new SKColor(128, 128, 128)); doc.Layers[0].Mask = Compositor.Core.Model.LayerMask.AssetFrom(old);
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(32, 24)); mask.Erase(new SKColor(128, 128, 128));
        Assert.True(EditWorkspace.MaskPasted(doc, [doc.Layers[0].ID], DocumentSelection.FromCoverage(mask), false));
        Assert.InRange(doc.Layers[0].Mask!.Asset.Image.GetPixel(10, 10).Red, 63, 65); Assert.Equal(128, old.GetPixel(10, 10).Red);
    }
    [Fact] public void FillRespectsSelectionChannelsAndLocks()
    {
        using var doc = Solid(SKColors.Black); var layer = doc.Layers[0]; doc.Selection = DocumentSelection.Rectangular(new(0, 0, 16, 24), 32, 24); doc.EditChannels = ColorChannels.Red;
        Assert.True(EditWorkspace.Paint(doc, layer.ID, SKColors.White, 1, SKBlendMode.SrcOver, false));
        Assert.Equal(SKColors.Red, layer.Asset!.Image.GetPixel(5, 5)); Assert.Equal(SKColors.Black, layer.Asset.Image.GetPixel(24, 5));
        layer.Locks = LayerLocks.Pixels; Assert.False(EditWorkspace.Paint(doc, layer.ID, SKColors.Blue, 1, SKBlendMode.SrcOver, false));
    }
    [Fact] public void StrokeInsideLeavesTheExteriorUnchanged()
    {
        using var doc = Solid(SKColors.Transparent); doc.Selection = DocumentSelection.Rectangular(new(8, 6, 24, 18), 32, 24);
        Assert.True(EditWorkspace.Paint(doc, doc.Layers[0].ID, SKColors.Red, 1, SKBlendMode.SrcOver, false, strokeWidth: 2, strokeLocation: 0));
        Assert.Equal(0, doc.Layers[0].Asset!.Image.GetPixel(6, 10).Alpha); Assert.True(doc.Layers[0].Asset!.Image.GetPixel(9, 10).Alpha > 240);
        Assert.Equal(0, doc.Layers[0].Asset!.Image.GetPixel(16, 12).Alpha);
    }
    [Fact] public void FadeBlendsPreviousAndCurrentPixels()
    {
        using var doc = Solid(SKColors.Black); var before = doc.Clone(); var id = doc.Layers[0].ID;
        EditWorkspace.Paint(doc, id, SKColors.White, 1, SKBlendMode.SrcOver, false); Assert.True(EditWorkspace.Fade(doc, before, id, .25));
        Assert.InRange(doc.Layers[0].Asset!.Image.GetPixel(5, 5).Red, 63, 65);
    }
    [Fact] public void LiquidRescaleChangesDimensions()
    { using var image = new SKBitmap(Bitmaps.ColorInfo(40, 20)); image.Erase(SKColors.Red); using var result = AdvancedEdit.ContentAwareResize(image, 32, 24); Assert.Equal(32, result.Width); Assert.Equal(24, result.Height); }
    [Fact] public void AutoAlignFindsOffsetOnTexturedImage()
    {
        using var first = new SKBitmap(Bitmaps.ColorInfo(80, 64)); using var second = new SKBitmap(Bitmaps.ColorInfo(80, 64));
        first.Erase(SKColors.Transparent); second.Erase(SKColors.Transparent); var random = new Random(23);
        for (var y = 10; y < 50; y++) for (var x = 10; x < 60; x++) { var c = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)); first.SetPixel(x, y, c); second.SetPixel(x + 6, y + 4, c); }
        var delta = AdvancedEdit.AlignTranslation(first, second, 10); Assert.Equal(-6, delta.X); Assert.Equal(-4, delta.Y);
    }
    [Fact] public void AverageStackPreservesHdr()
    { using var a = Hdr(4, 2, 0); using var b = Hdr(2, 0, 2); using var result = AdvancedEdit.Blend([a, b], false); var p = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()); Assert.Equal(3, p[0]); Assert.Equal(1, p[1]); Assert.Equal(1, p[2]); }
    [Fact] public void PresetsRoundTripAndRejectMalformedImages()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var preset = new EditorPreset(Guid.NewGuid(), "细画笔", "Brush", Diameter: 17, Hardness: .4); EditorPresets.Save(path, [preset]); Assert.Equal(preset, Assert.Single(EditorPresets.Load(path)));
            Assert.Throws<InvalidDataException>(() => EditorPresets.Save(path, [preset with { Image = "not-base64" }]));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void CustomBrushUsesImageLuminanceForCoverage()
    {
        using var doc = Solid(SKColors.Transparent); using var tip = new SKBitmap(Bitmaps.ColorInfo(4, 4)); tip.Erase(SKColors.White); tip.SetPixel(1, 1, SKColors.Black);
        Assert.True(BrushEdits.Paint(doc, doc.Layers[0].ID, [new(10, 10)], new BrushSettings(Diameter: 4, Red: 1, TipImage: tip)));
        Assert.True(doc.Layers[0].Asset!.Image.GetPixel(9, 9).Alpha > 240); Assert.Equal(0, doc.Layers[0].Asset!.Image.GetPixel(11, 11).Alpha);
    }
}
