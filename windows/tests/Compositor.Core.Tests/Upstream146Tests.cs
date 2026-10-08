using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class Upstream146Tests
{
    // Same six source pixels and expectations as CompositorTests/CanvasRotationTests.swift.
    [Theory]
    [InlineData(true, new byte[] { 40, 10, 50, 20, 60, 30 })]
    [InlineData(false, new byte[] { 30, 60, 20, 50, 10, 40 })]
    public void CanvasQuarterTurnPreservesSourceAndTurnsEveryPixel(bool clockwise, byte[] expected)
    {
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(3, 2));
        for (var i = 0; i < 6; i++) image.SetPixel(i % 3, i / 3, new SKColor((byte)((i + 1) * 10), 0, 0));
        using var document = ImageImporter.NewDocument(ImportedImage.Create(image, "pixels"));
        Assert.True(CanvasRotation.Rotate(document, clockwise));
        Assert.Same(image, document.Layers[0].Asset!.Image);
        using var result = DocumentRenderer.Render(document);
        Assert.Equal(2, result.Width); Assert.Equal(3, result.Height);
        Assert.Equal(expected, Enumerable.Range(0, 6).Select(i => result.GetPixel(i % 2, i / 2).Red));
    }

    [Fact]
    public void CanvasRotationTurnsGuidesAndSelectionAndDoesNotMutateHistoryMask()
    {
        using var document = LayerPlacement.NewDocument(100, 60)!;
        document.Guides.Add(new() { ID = Guid.NewGuid(), Axis = GuideAxis.Vertical, Position = 30 });
        document.Guides.Add(new() { ID = Guid.NewGuid(), Axis = GuideAxis.Horizontal, Position = 10 });
        document.Selection = DocumentSelection.Rectangular(SKRectI.Create(20, 10), 100, 60);
        var mask = Compositor.Core.Model.LayerMask.Solid(true)!;
        mask.Placement = new Compositor.Core.Model.LayerTransform(1, 2, 10, 20);
        document.Layers[0].Mask = mask;
        CanvasRotation.Rotate(document, true);
        Assert.Equal(GuideAxis.Horizontal, document.Guides[0].Axis);
        Assert.Equal(30, document.Guides[0].Position);
        Assert.Equal(GuideAxis.Vertical, document.Guides[1].Axis);
        Assert.Equal(50, document.Guides[1].Position);
        Assert.Equal(SKRect.Create(50, 0, 10, 20), document.Selection.Path!.Bounds);
        Assert.Equal(0, mask.Placement!.Value.Rotation);
        Assert.Equal(90, document.Layers[0].Mask!.Placement!.Value.Rotation);
    }

    [Fact]
    public void ColorNoiseReductionRemovesHueGrainAndKeepsBrightness()
    {
        var pixels = new byte[120 * 120 * 4]; uint seed = 12345;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++) { seed = unchecked(seed * 1664525 + 1013904223); pixels[i + c] = (byte)(64 + (seed >> 25)); }
            pixels[i + 3] = 255;
        }
        static (double Color, double Brightness) Measure(byte[] data)
        {
            double color = 0, brightness = 0;
            for (var y = 10; y < 110; y++) for (var x = 10; x < 110; x++)
            {
                var p = (y * 120 + x) * 4;
                var r = data[p]; var g = data[p + 1]; var b = data[p + 2];
                color += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                brightness += .2126 * r + .7152 * g + .0722 * b;
            }
            return (color / 10000, brightness / 10000);
        }
        var before = Measure(pixels);
        AdjustPixels.CameraRawDetail(pixels, 120, 120, 480, 0, 1, 25, 0, 0, 50, 0, 100, 50, 50, 1);
        var after = Measure(pixels);
        Assert.True(after.Color < before.Color * .25);
        Assert.True(Math.Abs(after.Brightness - before.Brightness) < 1);
    }

    [Fact]
    public void ColorOverlayDoesNotIncreaseTranslucentAlpha()
    {
        using var bitmap = Bitmaps.Allocate(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(new SKColor(255, 0, 0, 128));
        using var result = EffectRasterizer.Render(bitmap, new LayerEffects
        { ColorOverlay = new ColorOverlayEffect { Blue = 1, Opacity = 1 } })!;
        var pixel = result.Pixels.GetPixel(-result.OffsetX, -result.OffsetY);
        Assert.Equal(128, pixel.Alpha); Assert.Equal(0, pixel.Red); Assert.Equal(255, pixel.Blue);
    }

    [Fact]
    public void WindowsUpdatesIgnoreMacReleasesAndForeignLinks()
    {
        Assert.Null(WindowsReleases.Newest("[{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://example.com\"}]"));
        Assert.Null(WindowsReleases.Newest("[{\"tag_name\":\"windows-v2.0.0\",\"html_url\":\"https://example.com\"}]"));
        var release = WindowsReleases.Newest("[{\"tag_name\":\"windows-v1.4.6.1\",\"prerelease\":true,\"html_url\":\"" + WindowsReleases.Page + "/tag/windows-v1.4.6.1\"}]");
        Assert.Equal(new Version(1, 4, 6, 1), release!.Version);
    }
}
