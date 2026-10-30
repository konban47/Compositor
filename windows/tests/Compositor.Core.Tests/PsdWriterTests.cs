using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO.PSD;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Writing a layered PSD, read back by the app's own PSD reader and by ImageMagick.</summary>
public sealed class PsdWriterTests
{
    private static ImageLayer Solid(string name, SKRectI rect, SKColor color, int width = 80, int height = 60)
    {
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(rect.Width, rect.Height)); bitmap.Erase(color);
        return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, name), new LayerTransform(rect.Left, rect.Top, rect.Width, rect.Height), name);
    }

    private static CanvasDocument Layered(out ImageLayer back, out ImageLayer folder, out ImageLayer inside, out ImageLayer clip, out ImageLayer hidden)
    {
        var document = new CanvasDocument(Guid.NewGuid(), 80, 60);
        back = Solid("Back", SKRectI.Create(80, 60), new SKColor(20, 40, 200));
        folder = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 80, 60), "Folder") { IsGroup = true };
        inside = Solid("Inside", SKRectI.Create(10, 10, 30, 20), new SKColor(220, 30, 30));
        inside.ParentID = folder.ID; inside.Opacity = .5; inside.BlendMode = LayerBlendMode.Multiply;
        clip = Solid("Clip", SKRectI.Create(0, 0, 80, 15), new SKColor(30, 200, 30));
        clip.ParentID = folder.ID; clip.MaskSourceID = inside.ID;
        hidden = Solid("Hidden “quoted”", SKRectI.Create(40, 30, 30, 20), SKColors.White);
        hidden.IsVisible = false;
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(30, 20)); mask.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(mask)) using (var paint = new SKPaint { Color = SKColors.White }) canvas.DrawRect(0, 0, 15, 20, paint);
        hidden.Mask = new Model.LayerMask(ImportedImage.Create(mask, "Mask"));
        document.Layers.AddRange([back, folder, inside, clip, hidden]);
        return document;
    }

    [Fact]
    public void LayersGroupsAndTheirSettingsReadBack()
    {
        using var document = Layered(out _, out _, out _, out _, out _);
        var path = Path.Combine(Path.GetTempPath(), $"psd-writer-{Guid.NewGuid():N}.psd");
        try
        {
            var notes = PsdWriter.Write(document, path);
            Assert.Empty(notes);
            var read = PsdImporter.Read(path);
            try
            {
                var layers = read.Manifest.Layers;
                // Groups come after their children, as the app lists them.
                Assert.Equal(["Back", "Inside", "Clip", "Folder", "Hidden “quoted”"], layers.Select(l => l.Name));
                var folder = layers.Single(l => l.Name == "Folder");
                Assert.True(folder.IsGroup);
                Assert.All(layers.Where(l => l.Name is "Inside" or "Clip"), l => Assert.Equal(folder.ID, l.ParentID));
                var inside = layers.Single(l => l.Name == "Inside");
                Assert.Equal(.5, inside.Opacity!.Value, 2);
                Assert.Equal(LayerBlendMode.Multiply, inside.BlendMode);
                Assert.Equal(inside.ID, layers.Single(l => l.Name == "Clip").MaskSourceID);
                var hidden = layers.Single(l => l.Name.StartsWith("Hidden"));
                Assert.False(hidden.IsVisible);
                Assert.NotNull(hidden.MaskFile);
                var mask = read.Masks[hidden.ID].Image;
                Assert.True(mask.GetPixel(5, 10).Red > 200 && mask.GetPixel(25, 10).Red < 50, "the mask's two halves");
                var pixels = read.Images[inside.ID].Image;
                Assert.Equal((30, 20), (pixels.Width, pixels.Height));
                Assert.Equal(new SKColor(220, 30, 30), pixels.GetPixel(15, 10));
            }
            finally { foreach (var image in read.Images.Values.Concat(read.Masks.Values)) image.Dispose(); }
            // The composite, for readers without layers, is the document as it is drawn.
            using var composite = new MagickImage(path);
            using var rendered = Rendering.DocumentRenderer.Render(document);
            var pixel = composite.GetPixels().GetPixel(20, 12).ToColor()!;
            var expected = rendered.GetPixel(20, 12);
            Assert.InRange(Math.Abs(pixel.R / 257 - expected.Red), 0, 2);
            Assert.InRange(Math.Abs(pixel.G / 257 - expected.Green), 0, 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ADeepDocumentIsWrittenAtSixteenBits()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 20, 10);
        var deep = Bitmaps.Allocate(DeepPixels.Info(20, 10)); var p = MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan());
        for (var i = 0; i < p.Length; i += 4) { p[i] = 1000f / 65535; p[i + 1] = .5f; p[i + 2] = .25f; p[i + 3] = 1; }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.CreateDeep(deep, "Deep"), new LayerTransform(0, 0, 20, 10), "Deep"));
        using var stream = new MemoryStream();
        PsdWriter.Write(document, stream);
        var bytes = stream.ToArray();
        Assert.Equal(16, bytes[22] << 8 | bytes[23]);
        using var read = new MagickImage(bytes);
        Assert.Equal(16u, read.Depth);
        // Between two 8-bit levels, which only 16 bits keep.
        Assert.InRange(read.GetPixels().GetPixel(3, 3).ToColor()!.R, 990, 1010);
    }

    [Fact]
    public void WhatPhotoshopKeepsEditableIsListedWhenWrittenAsPixels()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 20, 10);
        var layer = Solid("Styled", SKRectI.Create(20, 10), SKColors.Gray);
        layer.Effects = new LayerEffects();
        layer.FillOpacity = .5;
        document.Layers.Add(layer);
        using var stream = new MemoryStream();
        var notes = PsdWriter.Write(document, stream);
        Assert.Contains(notes, n => n.Layer == "Styled" && n.Message.Contains("styles"));
        Assert.Contains(notes, n => n.Layer == "Styled" && n.Message.Contains("Fill opacity"));
        Assert.All(Enum.GetValues<LayerBlendMode>(), mode => Assert.NotNull(PsdWriter.Key(mode)));
    }

    [Fact]
    public void PackBitsRoundTrips()
    {
        var random = new Random(3);
        foreach (var length in new[] { 1, 2, 3, 127, 128, 129, 300 })
        {
            var row = Enumerable.Range(0, length).Select(i => (byte)(i % 7 < 3 ? 9 : random.Next(256))).ToArray();
            var packed = PsdWriter.PackBits(row);
            var unpacked = new List<byte>();
            for (var i = 0; i < packed.Length;)
            {
                var n = (sbyte)packed[i++];
                if (n >= 0) { unpacked.AddRange(packed.AsSpan(i, n + 1).ToArray()); i += n + 1; }
                else if (n != -128) { unpacked.AddRange(Enumerable.Repeat(packed[i], 1 - n)); i++; }
            }
            Assert.Equal(row, unpacked);
        }
    }
}
