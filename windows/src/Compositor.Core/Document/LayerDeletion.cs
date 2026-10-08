using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum ClippingDeleteChoice { Cancel, Bake, Release }

/// <summary>Atomic multi-layer deletion, including dependencies on layers being removed.</summary>
public static class LayerDeletion
{
    private static HashSet<Guid> Removed(CanvasDocument document, IReadOnlyCollection<Guid> selected)
    {
        var removed = selected.ToHashSet();
        foreach (var id in selected) removed.UnionWith(document.Descendants(id));
        return removed;
    }

    public static bool HasDependents(CanvasDocument document, IReadOnlyCollection<Guid> selected)
    {
        var removed = Removed(document, selected);
        return document.Layers.Any(layer => !removed.Contains(layer.ID) && layer.MaskSourceID is { } source && removed.Contains(source));
    }

    public static bool Delete(CanvasDocument document, IReadOnlyCollection<Guid> selected, ClippingDeleteChoice choice)
    {
        var removed = Removed(document, selected);
        if (!document.Layers.Any(layer => removed.Contains(layer.ID))) return false;
        var dependents = document.Layers.Where(layer => !removed.Contains(layer.ID)
            && layer.MaskSourceID is { } source && removed.Contains(source)).ToArray();
        if (dependents.Length > 0 && choice == ClippingDeleteChoice.Cancel) return false;
        var baked = new Dictionary<Guid, ImportedImage>();
        try
        {
            // Prepare every replacement before changing the document, so allocation failure is atomic.
            if (choice == ClippingDeleteChoice.Bake)
                foreach (var layer in dependents)
                    if (layer.Asset is not null) baked.Add(layer.ID, Bake(document, layer));
            document.Layers.RemoveAll(layer => removed.Contains(layer.ID));
            foreach (var layer in dependents)
            {
                layer.MaskSourceID = null;
                if (!baked.TryGetValue(layer.ID, out var pixels)) continue;
                layer.Asset = pixels;
                // Baking makes editable geometry/text raster content; its own mask and appearance stay.
                layer.Text = null;
                layer.Shape = null;
            }
            baked.Clear(); // Ownership has moved to the document/history.
            return true;
        }
        finally { foreach (var pixels in baked.Values) pixels.Dispose(); }
    }

    /// <summary>Port of LiveMaskBaker.swift: bake only the live dependency, in target pixel coordinates.</summary>
    private static ImportedImage Bake(CanvasDocument document, ImageLayer target)
    {
        var asset = target.Asset!;
        var targetMatrix = BrushEdits.PixelToDocument(target.Transform, asset.Width, asset.Height);
        if (!targetMatrix.TryInvert(out var inverse)) throw new InvalidOperationException("Cannot invert the target layer transform.");
        var byID = document.Layers.ToDictionary(layer => layer.ID);
        var coverage = new byte[checked(asset.Width * asset.Height)];
        Array.Fill(coverage, (byte)255);
        using var sampled = Bitmaps.Allocate(Bitmaps.ColorInfo(asset.Width, asset.Height));
        using var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(asset.Width, asset.Height));
        var seen = new HashSet<Guid> { target.ID };
        var sourceID = target.MaskSourceID;
        while (sourceID is { } id)
        {
            if (!seen.Add(id) || !byID.TryGetValue(id, out var source) || source.Asset is not { } sourceAsset)
                throw new InvalidOperationException("Invalid clipping source.");
            sampled.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(sampled))
            {
                canvas.SetMatrix(SKMatrix.Concat(inverse, BrushEdits.PixelToDocument(source.Transform, sourceAsset.Width, sourceAsset.Height)));
                using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(source.EffectiveOpacity(byID) * 255), 0, 255)) };
                canvas.DrawBitmap(sourceAsset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
            mask.Erase(SKColors.White);
            if (source.Mask is { IsEnabled: true } own)
            {
                mask.Erase(SKColors.Black);
                using var canvas = new SKCanvas(mask);
                canvas.SetMatrix(SKMatrix.Concat(inverse, BrushEdits.PixelToDocument(source.MaskTransform, own.Asset.Width, own.Asset.Height)));
                canvas.DrawBitmap(own.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
            }
            var rgba = sampled.GetPixelSpan();
            var gray = mask.GetPixelSpan();
            for (var y = 0; y < asset.Height; y++)
                for (var x = 0; x < asset.Width; x++)
                {
                    var index = y * asset.Width + x;
                    coverage[index] = (byte)((coverage[index] * rgba[y * sampled.RowBytes + x * 4 + 3] * gray[y * mask.RowBytes + x] + 32512) / 65025);
                }
            sourceID = source.MaskSourceID;
        }
        var result = asset.Image.Copy();
        var pixels = result.GetPixelSpan();
        for (var y = 0; y < asset.Height; y++)
            for (var x = 0; x < asset.Width; x++)
            {
                var offset = y * result.RowBytes + x * 4 + 3;
                pixels[offset] = (byte)((pixels[offset] * coverage[y * asset.Width + x] + 127) / 255);
            }
        return ImportedImage.Create(result, asset.Name);
    }
}
