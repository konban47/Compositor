using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class ImageWorkspaceTests
{
    private static ImportedImage Gradient(int bits = 16)
    {
        var bitmap = Bitmaps.Allocate(DeepPixels.Info(1024, 2)); var p = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan());
        for (var i = 0; i < p.Length; i += 4) { p[i] = .1f + (i / 4 % 1024) / 2048f; p[i + 1] = .37123f; p[i + 2] = .76342f; p[i + 3] = 1; }
        return ImportedImage.CreateDeep(bitmap, "precision", bits);
    }
    [Fact] public void Png16RoundTripRetainsMoreThan256Values()
    {
        using var original = Gradient(); var bytes = DeepImageCodec.EncodePng(original.Deep!);
        Assert.Equal(16, PngCodec.ReadHeader(bytes).BitDepth);
        using var loaded = DeepImageCodec.Decode(bytes, "decoded"); Assert.Equal(16, loaded.BitDepth);
        var a = MemoryMarshal.Cast<byte, float>(original.Deep!.GetPixelSpan()); var b = MemoryMarshal.Cast<byte, float>(loaded.Deep!.GetPixelSpan());
        for (var i = 0; i < a.Length; i++) Assert.InRange(Math.Abs(a[i] - b[i]), 0, 2f / 65535);
        Assert.True(b.ToArray().Where((_, i) => i % 4 == 0).Distinct().Count() > 900);
    }
    [Fact] public void FloatProjectPreservesHdrAndMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Compositor-precision-" + Guid.NewGuid() + ".comp");
        using var doc = ImageImporter.NewDocument(Gradient(32)); doc.Image = new ImageSettings { Depth = 32 };
        var p = MemoryMarshal.Cast<byte, float>(doc.Layers[0].Asset!.Deep!.GetPixelSpan()); p[0] = 3.1234567f;
        doc.MeasurementScale = new MeasurementScale(10, 2, "mm"); doc.Variables.Add(new("name", doc.Layers[0].ID, VariableKind.Visibility));
        try
        {
            ProjectStore.Save(ProjectSnapshot.FromDocument(doc), directory);
            using var snapshot = ProjectStore.Load(directory); using var restored = snapshot.ToDocument();
            Assert.Equal(18, snapshot.Manifest.Version); Assert.Equal(doc.Image, restored.Image);
            Assert.Equal(doc.MeasurementScale, restored.MeasurementScale); Assert.Equal(doc.Variables, restored.Variables);
            Assert.Equal(3.1234567f, MemoryMarshal.Cast<byte, float>(restored.Layers[0].Asset!.Deep!.GetPixelSpan())[0]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public void NewAdjustmentsRespectSelectionAndKeepPrecision()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0]; var old = layer.Asset!;
        using var path = new SKPathBuilder(); path.AddRect(new SKRect(0, 0, 512, 2)); doc.Selection = DocumentSelection.FromPath(path.Detach());
        var settings = new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.BrightnessContrast, Values = new() { ["Brightness"] = 20 } };
        Assert.True(ImageAdjustments.Apply(doc, layer.ID, settings)); Assert.Equal(16, layer.Asset!.BitDepth);
        var before = MemoryMarshal.Cast<byte, float>(old.Deep!.GetPixelSpan()); var after = MemoryMarshal.Cast<byte, float>(layer.Asset.Deep!.GetPixelSpan());
        Assert.True(after[0] > before[0]); Assert.Equal(before[800 * 4], after[800 * 4]);
        Assert.True(after.ToArray().Take(512 * 4).Where((_, i) => i % 4 == 0).Distinct().Count() > 500);
        var history = new DocumentHistory(); history.Begin("Exposure", doc, layer.ID); var retained = layer.Asset;
        Assert.True(ImageAdjustments.Apply(doc, layer.ID, settings)); history.End(doc, layer.ID);
        Assert.True(history.RetainedBytes(doc) >= retained!.Deep!.ByteCount + retained.Image.ByteCount + retained.Thumbnail.ByteCount);
        layer.Locks = LayerLocks.Pixels; Assert.False(ImageAdjustments.Apply(doc, layer.ID, settings));
    }
    [Fact] public void CubeInterpolatesAndRejectsMalformedInput()
    {
        var cube = ColorCube.Parse("LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        var color = new Color3(.24, .67, .89); var result = cube.Apply(color);
        Assert.Equal(color.R, result.R, 10); Assert.Equal(color.G, result.G, 10); Assert.Equal(color.B, result.B, 10);
        Assert.Throws<InvalidDataException>(() => ColorCube.Parse("LUT_3D_SIZE 2\n0 0 0"));
    }

    [Theory] [InlineData(ImageMode.CMYK)] [InlineData(ImageMode.Lab)] [InlineData(ImageMode.Grayscale)]
    public void TiffWritesNativeChannelsAndReimportsPrecision(ImageMode mode)
    {
        using var doc = ImageImporter.NewDocument(Gradient());
        Assert.True(ImageModeEdits.Convert(doc, new ImageSettings { Mode = mode, Depth = 16 }));
        using var exported = ExportImage.Encode(doc, new ExportOptions(ExportFormat.Tiff, doc.Width, doc.Height, NativeColorMode: true));
        var metadata = new ImageMagick.MagickImageInfo(exported.Bytes);
        Assert.Equal(mode == ImageMode.CMYK ? ImageMagick.ColorSpace.CMYK : mode == ImageMode.Lab ? ImageMagick.ColorSpace.Lab : ImageMagick.ColorSpace.Gray, metadata.ColorSpace);
        using var loaded = DeepImageCodec.Decode(exported.Bytes, "round trip"); Assert.Equal(16, loaded.BitDepth); Assert.Equal(mode, loaded.ColorMode);
        Assert.True(MemoryMarshal.Cast<byte, float>(loaded.Deep!.GetPixelSpan()).ToArray().Where((_, i) => i % 4 == 0).Distinct().Count() > 500);
    }
    [Theory] [InlineData(AdjustmentKind.Levels)] [InlineData(AdjustmentKind.Curves)] [InlineData(AdjustmentKind.Exposure)] [InlineData(AdjustmentKind.HueSaturation)]
    public void UpstreamAdjustmentsPreserveDeepTonalSteps(AdjustmentKind kind)
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0];
        var settings = new LayerAdjustment { Kind = kind, ExposureSettings = new ExposureSettings { Exposure = .1 } };
        settings.Levels.Ranges[0].Gamma = 1.1;
        Assert.True(FilterEdits.ApplyAdjustment(doc, layer.ID, settings)); Assert.Equal(16, layer.Asset!.BitDepth);
        var values = MemoryMarshal.Cast<byte, float>(layer.Asset.Deep!.GetPixelSpan()).ToArray().Where((_, i) => i % 4 == 0);
        Assert.True(values.Distinct().Count() > 500);
    }
    [Theory] [InlineData(WarpMode.Liquify)] [InlineData(WarpMode.Smudge)]
    public void WarpRetainsUnpaintedDeepPixels(WarpMode mode)
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var old = doc.Layers[0].Asset!; var layer = doc.Layers[0];
        Assert.True(WarpEdits.Warp(doc, layer.ID, [new(100, 1), new(150, 1)], mode, new BrushSettings(Diameter: 20, Hardness: .8, Opacity: .8)));
        Assert.Equal(16, layer.Asset!.BitDepth);
        var before = MemoryMarshal.Cast<byte, float>(old.Deep!.GetPixelSpan()); var after = MemoryMarshal.Cast<byte, float>(layer.Asset.Deep!.GetPixelSpan());
        Assert.Equal(before[900 * 4], after[900 * 4]); Assert.NotEqual(before[130 * 4], after[130 * 4]);
    }
    [Fact] public void BlurPreservesAlphaAndDoesNotReduceTonalSteps()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0];
        Assert.True(FilterEdits.Apply(doc, layer.ID, FilterKind.GaussianBlur, new FilterSettings { BlurRadius = 5, PreserveTransparency = true }));
        Assert.Equal(16, layer.Asset!.BitDepth); var p = MemoryMarshal.Cast<byte, float>(layer.Asset.Deep!.GetPixelSpan());
        Assert.True(p.ToArray().Where((_, i) => i % 4 == 0).Distinct().Count() > 500); Assert.Equal(1f, p[3]);
    }
    [Fact] public void CanvasBackgroundAndDeepExportAreRendered()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); doc.Image = doc.Image with { Background = 0xFF102030 };
        using var png = ExportImage.Encode(doc, new ExportOptions(ExportFormat.Png, doc.Width, doc.Height)); Assert.Equal(16, PngCodec.ReadHeader(png.Bytes).BitDepth);
        doc.Layers.Clear(); using var output = Compositor.Core.Rendering.DocumentRenderer.Render(doc); Assert.Equal(new SKColor(16, 32, 48), output.GetPixel(0, 0));
    }
    [Fact] public void TypographyLayoutChangesGeometryAndPersistsVersion18()
    {
        var original = new LayerTextStyle { Content = "Typography\nParagraph", FontName = "Arial", FontSize = 40 };
        var before = TextEdits.BoxSize(original);
        original.Layout = new TextLayoutOptions { HorizontalScale = 150, VerticalScale = 120, SpaceBefore = 10, SpaceAfter = 15, LeftIndent = 20 };
        var after = TextEdits.BoxSize(original); Assert.True(after.Width > before.Width); Assert.True(after.Height > before.Height + 30);
        using var doc = new CanvasDocument(Guid.NewGuid(), 800, 600); Assert.NotNull(TextEdits.Add(doc, original, new SKPoint(0, 0)));
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(18, snapshot.Manifest.Version);
        Assert.Equal(original.Layout, snapshot.Manifest.Layers[0].Text?.Layout);
        Assert.False((original.Layout with { HorizontalScale = double.NaN }).IsValid);
    }
    [Fact] public void DataSetsAreQuotedAndAtomicWhenLaterVariablesFail()
    {
        var sets = ImageVariables.ParseCsv("name,title,visible\r\nFirst,\"Hello, world\",true\r\n"); Assert.Equal("Hello, world", sets[0].Values["title"]);
        using var doc = new CanvasDocument(Guid.NewGuid(), 100, 100); var id = TextEdits.Add(doc, new LayerTextStyle { Content = "Before", FontSize = 12 }, new(0, 0))!.Value;
        doc.Variables.Add(new("title", id, VariableKind.Text)); doc.Variables.Add(new("visible", id, VariableKind.Visibility));
        Assert.Throws<InvalidDataException>(() => ImageVariables.Apply(doc, new("bad", new() { ["title"] = "After", ["visible"] = "invalid" }), Path.GetTempPath()));
        Assert.Equal("Before", doc.Layers[0].LiveText!.Content);
        Assert.True(ImageVariables.Apply(doc, sets[0], Path.GetTempPath())); Assert.Equal("Hello, world", doc.Layers[0].LiveText!.Content);
    }
    [Fact] public void ModeConversionAndChannelCalculationProduceDifferentPixels()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0];
        Assert.True(ImageModeEdits.Convert(doc, new ImageSettings { Mode = ImageMode.Grayscale, Depth = 16 }));
        var p = MemoryMarshal.Cast<byte, float>(layer.Asset!.Deep!.GetPixelSpan()); Assert.Equal(p[0], p[1]); Assert.Equal(p[1], p[2]);
        using var calculated = ChannelCalculations.Calculate(new(doc, null, ImageChannel.Red), new(doc, null, ImageChannel.Red), new CalculationSettings(ChannelBlend.Multiply));
        var result = MemoryMarshal.Cast<byte, float>(calculated.GetPixelSpan()); Assert.InRange(result[0], p[0] * p[0] - .0001f, p[0] * p[0] + .0001f);
        Assert.True(ImageModeEdits.Convert(doc, new ImageSettings { Mode = ImageMode.Bitmap }, dither: false)); Assert.Contains(doc.Layers[0].Asset!.Image.GetPixel(0, 0).Red, new byte[] { 0, 255 });
    }
    [Fact] public void ResizingKeepsDeepSamplesInsteadOfQuantizingToEightBits()
    {
        using var doc = ImageImporter.NewDocument(Gradient());
        Assert.True(ImageEdits.Resize(doc, 1500, 3, 144));
        var asset = doc.Layers[0].Asset!; Assert.Equal(16, asset.BitDepth);
        Assert.True(MemoryMarshal.Cast<byte, float>(asset.Deep!.GetPixelSpan()).ToArray().Where((_, i) => i % 4 == 0).Distinct().Count() > 900);
    }
    [Fact] public void FloatTiffPreservesHdrValues()
    {
        using var original = Gradient(32); var pixels = MemoryMarshal.Cast<byte, float>(original.Deep!.GetPixelSpan()); pixels[0] = 2.125f;
        var bytes = DeepImageCodec.EncodeMode(original.Deep, new ImageSettings { Depth = 32 }, true, 300);
        using var decoded = DeepImageCodec.Decode(bytes, "HDR"); Assert.Equal(32, decoded.BitDepth);
        Assert.InRange(MemoryMarshal.Cast<byte, float>(decoded.Deep!.GetPixelSpan())[0], 2.1249f, 2.1251f);
    }
    [Fact] public void RawRecoveryNoiseAndLensChangePixelsWithoutReducingDepth()
    {
        using var source = Gradient();
        using var recovered = RawDevelop.Apply(source, new RawDevelopSettings(HighlightRecovery: 80));
        Assert.True(MemoryMarshal.Cast<byte, float>(recovered.Deep!.GetPixelSpan())[2] < MemoryMarshal.Cast<byte, float>(source.Deep!.GetPixelSpan())[2]);
        using var lens = RawDevelop.Apply(source, new RawDevelopSettings(LensCorrection: 80));
        Assert.NotEqual(MemoryMarshal.Cast<byte, float>(source.Deep.GetPixelSpan())[800 * 4], MemoryMarshal.Cast<byte, float>(lens.Deep!.GetPixelSpan())[800 * 4]);
        var noisy = Bitmaps.Allocate(DeepPixels.Info(32, 32)); var data = MemoryMarshal.Cast<byte, float>(noisy.GetPixelSpan());
        for (var i = 0; i < data.Length; i += 4) { data[i] = data[i + 1] = data[i + 2] = i % 8 == 0 ? .49f : .51f; data[i + 3] = 1; }
        using var input = ImportedImage.CreateDeep(noisy, "noise", 16); using var clean = RawDevelop.Apply(input, new RawDevelopSettings(NoiseReduction: 100));
        Assert.Equal(16, clean.BitDepth); Assert.True(MemoryMarshal.Cast<byte, float>(clean.Deep!.GetPixelSpan())[0] > .49f);
        Assert.Throws<ArgumentException>(() => RawDevelop.Apply(source, new RawDevelopSettings(Exposure: double.NaN)));
    }
    [Fact] public void ChannelMaskAndTransparencyControlTheAppliedPixels()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); using var maskDoc = new CanvasDocument(Guid.NewGuid(), doc.Width, doc.Height);
        maskDoc.Image = maskDoc.Image with { Background = 0xFF000000 }; var layer = doc.Layers[0]; var before = layer.Asset!;
        Assert.True(ChannelCalculations.Apply(doc, layer.ID, new(doc, null, ImageChannel.Red, true), new CalculationSettings(), new(maskDoc, null, ImageChannel.Gray)));
        Assert.Equal(before.Deep!.GetPixelSpan().ToArray(), layer.Asset!.Deep!.GetPixelSpan().ToArray());
        Assert.Throws<ArgumentException>(() => ChannelCalculations.Calculate(new(doc, null, ImageChannel.Red), new(doc, null, ImageChannel.Red), new CalculationSettings(Opacity: double.NaN)));
    }
    [Fact] public void AnalysisUsesSelectionAndCalibratedUnitsAndRotationIsReversible()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 100, 60); doc.MeasurementScale = new MeasurementScale(10, 1, "mm");
        SelectionEdits.Select(doc, new SKRectI(10, 10, 30, 40));
        var measurement = ImageAnalysis.Measure(doc, "Example, A"); Assert.Equal(6, measurement.Area, 4); Assert.Equal(10, measurement.Perimeter, 4); Assert.Equal(2, measurement.Width); Assert.Equal(3, measurement.Height);
        doc.Measurements.Add(measurement); Assert.Contains("\"Example, A\"", ImageAnalysis.Csv(doc));
        Assert.True(ImageAnalysis.ScaleMarker(doc, 3, "3 mm", false));
        Assert.True(ImageGeometry.Rotate(doc, 90)); Assert.Equal(60, doc.Width); Assert.Equal(100, doc.Height);
        Assert.True(ImageGeometry.Rotate(doc, -90)); Assert.Equal(100, doc.Width); Assert.Equal(60, doc.Height);
        Assert.Equal(new SKRect(10, 10, 30, 40), doc.Selection.Path!.Bounds);
    }

    [Fact] public void DeepAdjustmentLayerPreservesCompositeAlpha()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0];
        var pixels = MemoryMarshal.Cast<byte, float>(layer.Asset!.Deep!.GetPixelSpan()); for (var i = 3; i < pixels.Length; i += 4) pixels[i] = .5f;
        var adjustment = new ImageLayer(Guid.NewGuid(), null, layer.Transform, "Invert") { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Invert }, Opacity = .5 };
        doc.Layers.Add(adjustment); using var rendered = Compositor.Core.Rendering.DocumentRenderer.RenderDeep(doc);
        var result = MemoryMarshal.Cast<byte, float>(rendered.GetPixelSpan()); Assert.InRange(result[3], .4999f, .5001f); Assert.InRange(result[0], .4999f, .5001f);
    }

    [Fact] public void LayerDepthConversionAndDeepGradientAreUndoableAndSmooth()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0]; var history = new DocumentHistory();
        history.Begin("Convert Layer to 8-Bit", doc, layer.ID); Assert.True(DeepPaint.ConvertLayer(doc, layer.ID, 8)); history.End(doc, layer.ID);
        Assert.Equal(8, layer.Asset!.BitDepth); Assert.Equal(16, history.Undo()!.Value.Document!.Layers[0].Asset!.BitDepth);
        Assert.True(DeepPaint.ConvertLayer(doc, layer.ID, 16));
        Assert.True(GradientEdits.Fill(doc, layer.ID, false, new(0, 0), new(1024, 0), SKColors.Black, SKColors.White, 1, GradientShape.Linear));
        Assert.True(MemoryMarshal.Cast<byte, float>(layer.Asset!.Deep!.GetPixelSpan()).ToArray().Where((_, i) => i % 4 == 0).Distinct().Count() >= 1000);
        var untouched = MemoryMarshal.Cast<byte, float>(layer.Asset.Deep.GetPixelSpan())[900 * 4]; SelectionEdits.Select(doc, new SKRectI(0, 0, 100, 2));
        Assert.True(FillEdits.Fill(doc, layer.ID, SKColors.Red)); Assert.Equal(untouched, MemoryMarshal.Cast<byte, float>(layer.Asset!.Deep!.GetPixelSpan())[900 * 4]);
        Assert.True(FillEdits.Clear(doc, layer.ID)); Assert.Equal(0, MemoryMarshal.Cast<byte, float>(layer.Asset!.Deep!.GetPixelSpan())[3]);
    }
    [Fact] public void CameraRawRetainsDeepStepsAndChannelMixing()
    {
        using var doc = ImageImporter.NewDocument(Gradient()); var layer = doc.Layers[0];
        Assert.True(CameraRawEdits.Apply(doc, layer.ID, new CameraRawSettings { Exposure = .3, Temperature = 10, Vibrance = 25 }));
        Assert.Equal(16, layer.Asset!.BitDepth);
        Assert.True(MemoryMarshal.Cast<byte, float>(layer.Asset.Deep!.GetPixelSpan()).ToArray().Where((_, i) => i % 4 == 0).Distinct().Count() > 700);
    }

}
