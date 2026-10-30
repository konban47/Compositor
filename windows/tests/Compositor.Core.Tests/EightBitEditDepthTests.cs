using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>
/// Each edit that works on 8-bit pixels, run on a 16-bit layer through the history as the app runs it: the layer
/// stays 16-bit, and a pixel the edit leaves alone keeps its 16-bit sample (upstream 5de396b, path by path).
/// </summary>
public sealed class EightBitEditDepthTests
{
    /// <summary>A value between two 8-bit levels, so a layer that fell to 8 bits would lose it.</summary>
    private const float Between = 1000f / 65535;

    /// <summary>A 16-bit layer of opaque vertical stripes, each pixel's red nudged off the 8-bit grid, on a wider
    /// document, and a selection away from the bottom-left corner the checks read.</summary>
    private static (CanvasDocument Document, ImageLayer Layer) Striped()
    {
        var bitmap = Bitmaps.Allocate(DeepPixels.Info(40, 40)); var p = MemoryMarshal.Cast<byte, float>(bitmap.GetPixelSpan());
        for (var y = 0; y < 40; y++) for (var x = 0; x < 40; x++)
        {
            var i = (y * 40 + x) * 4; var tone = x / 4 % 2 == 0 ? .78f : .24f;
            p[i] = tone + Between; p[i + 1] = tone; p[i + 2] = tone; p[i + 3] = 1;
        }
        var document = new CanvasDocument(Guid.NewGuid(), 60, 40);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.CreateDeep(bitmap, "Stripes"), new LayerTransform(0, 0, 40, 40), "Stripes");
        document.Layers.Add(layer);
        SelectionEdits.Select(document, SKRectI.Create(20, 4, 10, 10));
        return (document, layer);
    }

    /// <summary>The stripes' two reds as a 16-bit layer stores them.</summary>
    private static readonly float Light = Quantized(.78f + Between), Dark = Quantized(.24f + Between);
    private static float Quantized(float value) => MathF.Round(value * 65535) / 65535;

    private static void Run(CanvasDocument document, Guid id, string name, Func<bool> edit)
    {
        var history = new DocumentHistory();
        history.Begin(name, document, id);
        Assert.True(edit(), $"{name} did nothing");
        history.End(document, id);
    }

    private static float Red(ImportedImage image, int x, int y) =>
        MemoryMarshal.Cast<byte, float>(image.Deep!.GetPixelSpan())[y * image.Deep.RowBytes / 4 + x * 4];

    /// <summary>The layer is still 16-bit and the bottom-left corner, which no edit here reaches, is as it was.</summary>
    private static void KeptDepth(CanvasDocument document, Guid id, string name)
    {
        var asset = document.Layers.Single(layer => layer.ID == id).Asset!;
        Assert.True(asset.BitDepth == 16 && asset.Deep is not null, $"{name} made the layer {asset.BitDepth}-bit");
        Assert.Equal(Light, Red(asset, 1, 37));
        Assert.Equal(Dark, Red(asset, 5, 37));
    }

    [Fact]
    public void ContentAwareMoveKeepsDepth()
    {
        var (document, layer) = Striped();
        using var _ = document;
        var captured = ContentMoveEdits.Capture(document, layer.ID, false)!.Value;
        using var pixels = captured.Pixels;
        Run(document, layer.ID, "Content-Aware Move Tool", () => ContentMoveEdits.Apply(document, layer.ID, pixels, captured.Region,
            new LayerTransform(captured.Region.Left + 6, captured.Region.Top + 12, captured.Region.Width, captured.Region.Height), false, false, 4, 5));
        KeptDepth(document, layer.ID, "Content-Aware Move");
    }

    [Fact]
    public void MovingContentAndExtendingKeepDepth()
    {
        foreach (var extend in new[] { false, true })
        {
            var (document, layer) = Striped();
            using var _ = document;
            Run(document, layer.ID, "Content-Aware Move Tool", () => RepairEdits.MoveContent(document, layer.ID, 6, 12, extend));
            KeptDepth(document, layer.ID, extend ? "Extend" : "Move");
        }
    }

    [Fact]
    public void PatchAndCloneKeepDepth()
    {
        foreach (var heal in new[] { true, false })
        {
            var (document, layer) = Striped();
            using var _ = document;
            Run(document, layer.ID, "Patch Tool", () => RepairEdits.Patch(document, layer.ID, new SKPoint(8, 16), heal));
            KeptDepth(document, layer.ID, heal ? "Patch" : "Patch (clone)");
        }
    }

    [Fact]
    public void RedEyeKeepsDepth()
    {
        var (document, layer) = Striped();
        using var _ = document;
        // A red pupil for it to find.
        var deep = MemoryMarshal.Cast<byte, float>(layer.Asset!.Deep!.GetPixelSpan());
        var image = layer.Asset.Image;
        for (var y = 20; y < 26; y++) for (var x = 20; x < 26; x++)
        {
            var i = (y * 40 + x) * 4; deep[i] = .9f; deep[i + 1] = .05f; deep[i + 2] = .05f;
            image.SetPixel(x, y, new SKColor(230, 13, 13));
        }
        SelectionEdits.Deselect(document);
        Run(document, layer.ID, "Red Eye Tool", () => RepairEdits.RedEye(document, layer.ID, SKRect.Create(16, 16, 14, 14), 1, .5));
        KeptDepth(document, layer.ID, "Red Eye");
    }

    [Fact]
    public void DitherHeldToTheSelectionKeepsDepth()
    {
        var (document, layer) = Striped();
        using var _ = document;
        Run(document, layer.ID, "Dither", () => DitherEdits.Apply(document, layer.ID, DitherStyle.FloydSteinberg, new DitherSettings()));
        KeptDepth(document, layer.ID, "Dither");
    }

    [Fact]
    public void PaintBucketKeepsDepth()
    {
        var (document, layer) = Striped();
        using var _ = document;
        SelectionEdits.Deselect(document);
        Run(document, layer.ID, "Paint Bucket", () => BucketEdits.Apply(document, layer.ID, new SKPoint(21, 20), new WandOptions(Radius: 0, Tolerance: 0), SKColors.Red, false));
        KeptDepth(document, layer.ID, "Paint Bucket");
    }

    [Fact]
    public void GradientFillAndClearInTheSelectionKeepDepth()
    {
        var edits = new (string Name, Func<CanvasDocument, Guid, bool> Edit)[]
        {
            ("Gradient", (d, id) => GradientEdits.Fill(d, id, false, new SKPoint(20, 4), new SKPoint(30, 14), SKColors.Red, SKColors.Blue, 1, default)),
            ("Fill", (d, id) => FillEdits.Fill(d, id, SKColors.Green)),
            ("Clear", (d, id) => FillEdits.Clear(d, id)),
            ("Nudge", (d, id) => SelectionEdits.MovePixels(d, id, 5, 5)),
            ("Cut", (d, id) => { var cut = SelectionClipboard.Cut(d, id, out var copied); copied?.Dispose(); return cut; }),
            ("Vignette", (d, id) => FilterEdits.Apply(d, id, FilterKind.Vignette, new FilterSettings())),
            ("Tonal Contrast", (d, id) => FilterEdits.Apply(d, id, FilterKind.TonalContrast, new FilterSettings())),
            ("Content-Aware Fill", (d, id) => ContentFillEdits.Apply(d, id)),
        };
        foreach (var (name, edit) in edits)
        {
            var (document, layer) = Striped();
            using var _ = document;
            Run(document, layer.ID, name, () => edit(document, layer.ID));
            KeptDepth(document, layer.ID, name);
        }
    }

    [Fact]
    public void DistortingKeepsDepth()
    {
        var (document, layer) = Striped();
        using var _ = document;
        SelectionEdits.Deselect(document);
        Run(document, layer.ID, "Distort", () => DistortEdits.Distort(document, layer.ID, [new(0, 0), new(44, 2), new(42, 40), new(-2, 38)]));
        var asset = document.Layers.Single(l => l.ID == layer.ID).Asset!;
        Assert.Equal(16, asset.BitDepth);
    }

    [Fact]
    public void CopyingAndPastingADeepLayerKeepsItsSamples()
    {
        var (document, layer) = Striped();
        using var _ = document;
        SelectionEdits.Select(document, SKRectI.Create(0, 30, 10, 10));
        var made = SelectionClipboard.LayerViaCopy(document, layer.ID, layer.ID);
        Assert.NotNull(made);
        var copied = document.Layers.Single(l => l.ID == made).Asset!;
        Assert.Equal(16, copied.BitDepth);
        Assert.Equal(Light, Red(copied, 1, 7), 5);
        using var clipboard = SelectionClipboard.Copy(document, layer.ID)!;
        var pasted = SelectionClipboard.Paste(document, clipboard, layer.ID);
        var pastedAsset = document.Layers.Single(l => l.ID == pasted).Asset!;
        Assert.Equal(16, pastedAsset.BitDepth);
        Assert.Equal(Dark, Red(pastedAsset, 5, 7), 5);
        // From an 8-bit layer, a paste stays 8-bit.
        var plain = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Bitmaps.Allocate(Bitmaps.ColorInfo(20, 20)), "Plain"), new LayerTransform(0, 0, 20, 20), "Plain");
        document.Layers.Add(plain);
        SelectionEdits.Select(document, SKRectI.Create(0, 0, 10, 10));
        using var flat = SelectionClipboard.Copy(document, plain.ID)!;
        var flatPasted = SelectionClipboard.Paste(document, flat, plain.ID);
        Assert.Equal(8, document.Layers.Single(l => l.ID == flatPasted).Asset!.BitDepth);
    }
}
