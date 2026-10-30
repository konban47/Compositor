using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Pixels taken off the canvas: the image, and where on the document it came from.</summary>
public sealed class ClipboardImage(SKBitmap image, SKRectI region) : IDisposable
{
    private SKBitmap? _image = image;

    public SKBitmap Image => _image ?? throw new InvalidOperationException("These pixels have been handed over.");

    /// <summary>The document rectangle it was taken from, which is where a paste puts it back.</summary>
    public SKRectI Region { get; } = region;

    /// <summary>The same pixels at 16 or 32 bits (RgbaF32), when they were copied from a layer that deep, so a
    /// paste keeps them; null for 8-bit pixels.</summary>
    public SKBitmap? Deep { get; init; }
    public int BitDepth { get; init; } = 8;

    /// <summary>
    /// Hands the pixels over to whoever asked, so this no longer owns them — what putting them into a layer
    /// does, since the layer keeps the bitmap itself.
    /// </summary>
    public SKBitmap Take()
    {
        var taken = _image ?? throw new InvalidOperationException("These pixels have already been handed over.");
        _image = null;
        return taken;
    }

    public void Dispose() { _image?.Dispose(); Deep?.Dispose(); }
}

/// <summary>
/// The selection clipboard: the pixels inside a selection, taken off the canvas, cut away, and put back as a
/// layer. What is copied is clipped by the selection, so a paste carries exactly what was inside it.
/// </summary>
public static class SelectionClipboard
{
    /// <summary>
    /// The selected pixels of one layer, or null when there is no selection or the layer has no pixels of its
    /// own. A layer's content comes from its own grid, so what is copied is what the layer holds there.
    /// </summary>
    public static ClipboardImage? Copy(CanvasDocument document, Guid layerID)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { Asset: { } asset } layer) return null;
        if (layer.IsGroup) return null;
        var region = Region(document);
        if (region is not { } box) return null;
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(box.Width, box.Height));
        using (var canvas = new SKCanvas(image))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(-box.Left, -box.Top);
            DocumentRenderer.DrawLayerPixels(canvas, layer);
            canvas.Flush();
        }
        if (!Clip(image, document, box))
        {
            image.Dispose();
            return null;
        }
        return new ClipboardImage(image, box) { Deep = asset.Deep is null ? null : DeepRegion(document, layer, asset, box), BitDepth = asset.BitDepth };
    }

    /// <summary>A deep layer's own samples over <paramref name="box"/>, clipped by the selection as the 8-bit copy is.</summary>
    private static SKBitmap? DeepRegion(CanvasDocument document, ImageLayer layer, ImportedImage asset, SKRectI box)
    {
        using var drawn = Bitmaps.Allocate(DeepPixels.Info(box.Width, box.Height).WithAlphaType(SKAlphaType.Premul));
        using (var canvas = new SKCanvas(drawn))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(-box.Left, -box.Top);
            canvas.Concat(BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height));
            // Whole pixels landing on whole pixels are copied as they are; anything else is resampled.
            var t = layer.Transform;
            var exact = t.Rotation == 0 && !t.FlipX && !t.FlipY && t.Width == asset.Width && t.Height == asset.Height && t.X == Math.Round(t.X) && t.Y == Math.Round(t.Y);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawBitmap(asset.Deep!, 0, 0, new SKSamplingOptions(exact ? SKFilterMode.Nearest : SKFilterMode.Linear), paint);
        }
        var deep = DeepPixels.From(drawn);
        SKBitmap? coverage;
        try { coverage = document.Selection.Coverage(box); }
        catch (InvalidOperationException) { deep.Dispose(); return null; }
        using var _ = coverage;
        if (coverage is null) { deep.Dispose(); return null; }
        var clip = coverage.GetPixelSpan();
        var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan());
        for (var y = 0; y < box.Height; y++)
            for (var x = 0; x < box.Width; x++)
                floats[y * deep.RowBytes / 4 + x * 4 + 3] *= clip[y * coverage.RowBytes + x] / 255f;
        return deep;
    }

    /// <summary>The selected pixels of everything that is drawn, flattened.</summary>
    public static ClipboardImage? CopyMerged(CanvasDocument document)
    {
        if (Region(document) is not { } box) return null;
        var image = DocumentRenderer.RenderRegion(document, box);
        if (!Clip(image, document, box))
        {
            image.Dispose();
            return null;
        }
        return new ClipboardImage(image, box);
    }

    /// <summary>
    /// Takes the selected pixels of a layer and clears them from it — a copy followed by an erase, which is
    /// what the Mac build's cut does. The copy is given back whether or not anything was cleared.
    /// </summary>
    public static bool Cut(CanvasDocument document, Guid layerID, out ClipboardImage? copied)
    {
        copied = Copy(document, layerID);
        if (copied is null) return false;
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { Asset: not null } layer) return false;
        if (!FilterSurface.Begin(layer, 0, out var work, out var placement)) return false;
        using var _ = work;
        using var was = FilterSurface.Copy(work);
        // Everything the selection covers goes to nothing; outside it the pixels are what they were. The
        // mixing writes into the buffer it is given, so that is the one the layer then keeps.
        using var cleared = FilterSurface.Allocate(work.Width, work.Height);
        FilterSurface.Keep(document, was, cleared, placement);
        FilterSurface.Finish(layer, cleared, placement);
        return true;
    }

    /// <summary>The selected pixels of a layer as a new layer above it, at the same place on the document.</summary>
    public static Guid? LayerViaCopy(CanvasDocument document, Guid layerID, Guid? activeID)
    {
        using var copied = Copy(document, layerID);
        if (copied is null) return null;
        // The layer keeps the bitmap, so it is handed over rather than disposed with the copy.
        return Deepen(document, LayerPlacement.AddImage(document, copied.Take(), copied.Region, "Layer", activeID), copied);
    }

    /// <summary>A layer just made from <paramref name="clipboard"/> given its deep samples, so pixels copied from a
    /// 16-bit or HDR layer stay at that depth.</summary>
    private static Guid? Deepen(CanvasDocument document, Guid? made, ClipboardImage clipboard)
    {
        if (made is { } id && clipboard.Deep is { } deep && document.Layers.FirstOrDefault(layer => layer.ID == id) is { Asset: { } asset } layer
            && deep.Width == asset.Width && deep.Height == asset.Height)
            layer.Asset = ImportedImage.CreateDeep(deep.Copy(), asset.Name, clipboard.BitDepth);
        return made;
    }

    /// <summary>The clipboard's pixels as a new layer, where on the document they came from.</summary>
    public static Guid? Paste(CanvasDocument document, ClipboardImage clipboard, Guid? activeID)
    {
        // A copy: the clipboard keeps its own pixels for the next paste.
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(clipboard.Region.Width, clipboard.Region.Height));
        using (var canvas = new SKCanvas(image))
        {
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawBitmap(clipboard.Image, SKRect.Create(0, 0, image.Width, image.Height),
                new SKSamplingOptions(SKFilterMode.Nearest), paint);
        }
        return Deepen(document, LayerPlacement.AddImage(document, image, clipboard.Region, "Pasted Layer", activeID), clipboard);
    }

    /// <summary>The selection's bounds on the document, or null when nothing is selected.</summary>
    private static SKRectI? Region(CanvasDocument document)
    {
        if (document.Selection.Path is null) return null;
        var box = document.Selection.CoverageRect(document.Width, document.Height);
        return box.Width > 0 && box.Height > 0 ? box : null;
    }

    /// <summary>
    /// Scales the alpha of every pixel by how much the selection covers it, so what was not selected is not
    /// copied. The colour is left standing: an alpha-weighted copy keeps its colour, as a mask does.
    /// </summary>
    private static bool Clip(SKBitmap image, CanvasDocument document, SKRectI region)
    {
        SKBitmap? coverage;
        try
        {
            coverage = document.Selection.Coverage(region);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        using var _ = coverage;
        if (coverage is null) return false;
        var clip = coverage.GetPixelSpan();
        var pixels = image.GetPixelSpan();
        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                var amount = clip[y * coverage.RowBytes + x] / 255.0;
                if (amount >= 1) continue;
                var at = y * image.RowBytes + x * 4;
                pixels[at + 3] = (byte)Math.Clamp(Math.Round(pixels[at + 3] * amount), 0, 255);
            }
        }
        return true;
    }
}
