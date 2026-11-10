using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// Filter ▸ Content-Aware Fill: whatever the selection covers is made up out of the pixels around it. The
/// layer reaches as far as the selection does, so a fill over a region bigger than the layer grows it, and
/// the fill's job is done by the PatchMatch-style kernel ported from the Mac build's C.
/// </summary>
public static class ContentFillEdits
{
    /// <summary>
    /// Fills the selected part of a layer from its unselected, opaque pixels. False when there is no
    /// selection, when the layer cannot take it, or when there is nothing to fill from — in which case the
    /// layer is left exactly as it was.
    /// </summary>
    public static bool Apply(CanvasDocument document, Guid layerID) => Apply(document, layerID, null);

    /// <summary>The fill with Edit ▸ Content-Aware Fill's settings; null fills exactly as Filter ▸ Content-Aware Fill always has.</summary>
    public static bool Apply(CanvasDocument document, Guid layerID, ContentFillOptions? options)
    {
        if (document.Selection.Path is null) return false;
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { } layer
            || layer.Asset is not { } asset || layer.IsGroup)
        {
            return false;
        }
        var maskPlacement = layer.MaskTransform;

        // The grid to work over: the layer's own pixels, widened to cover the selection's bounding box where
        // that reaches past them. A fill is not a blur, so nothing is cut away again afterwards.
        var bounds = document.Selection.CoverageRect(document.Width, document.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        var toDocument = BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height);
        if (!toDocument.TryInvert(out var toLayer)) return false;
        var corners = new[]
        {
            toLayer.MapPoint(bounds.Left, bounds.Top),
            toLayer.MapPoint(bounds.Right, bounds.Top),
            toLayer.MapPoint(bounds.Left, bounds.Bottom),
            toLayer.MapPoint(bounds.Right, bounds.Bottom),
        };
        var wanted = SKRectI.Create(
            (int)Math.Floor(corners.Min(point => point.X)),
            (int)Math.Floor(corners.Min(point => point.Y)),
            (int)Math.Ceiling(corners.Max(point => point.X)) - (int)Math.Floor(corners.Min(point => point.X)),
            (int)Math.Ceiling(corners.Max(point => point.Y)) - (int)Math.Floor(corners.Min(point => point.Y)));
        var whole = SKRectI.Create(0, 0, asset.Width, asset.Height);
        var grid = SKRectI.Union(whole, wanted);
        if (grid.Width <= 0 || grid.Height <= 0) return false;

        if (!FilterSurface.Begin(layer, grid, out var work, out var placement)) return false;
        using var _ = work;

        // What to fill: one byte a pixel, set where the selection covers the layer pixel under it.
        var mask = new byte[work.Width * work.Height];
        if (!BuildMask(document, placement, work.Width, work.Height, mask)) return false;

        var pixels = work.GetPixelSpan();
        try
        {
            if (options is null ? ContentFill.Fill(pixels, work.RowBytes, mask, work.Width, work.Width, work.Height) != 1 : !FillWith(document, options, placement, work, mask)) return false;
        }
        catch (OutOfMemoryException)
        {
            return false;
        }

        FilterSurface.Finish(layer, work, placement);
        if (FilterSurface.PlaceMask(layer, maskPlacement, placement) is { } carried)
        {
            var mask2 = LayerMask.AssetFrom(carried);
            mask2.IsEnabled = layer.Mask!.IsEnabled;
            mask2.IsLinked = layer.Mask.IsLinked;
            layer.Mask = mask2;
        }
        return true;
    }

    /// <summary>
    /// The fill on the part of the grid that matters: the hole and the sampling area around it. Where a picture to sample is given
    /// (Sample All Layers, or a new layer), its pixels stand in for the layer's while the fill runs, and only the hole keeps them.
    /// </summary>
    private static bool FillWith(CanvasDocument document, ContentFillOptions options, LayerTransform placement, SKBitmap work, byte[] mask)
    {
        int width = work.Width, height = work.Height, stride = work.RowBytes;
        var toDocument = BrushEdits.PixelToDocument(placement, width, height);
        int DocumentIndex(int x, int y)
        {
            var at = toDocument.MapPoint(x + .5f, y + .5f); int dx = (int)Math.Floor(at.X), dy = (int)Math.Floor(at.Y);
            return dx < 0 || dy < 0 || dx >= document.Width || dy >= document.Height ? -1 : dy * document.Width + dx;
        }
        // The crop: the hole, widened by the sampling area's own extent or, without one, by the hole's size again.
        int left = width, top = height, right = -1, bottom = -1;
        void Include(int x, int y) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (mask[y * width + x] != 0) Include(x, y);
        if (right < 0) return false;
        if (options.Sampling is { } sampling)
        {
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (DocumentIndex(x, y) is var i and >= 0 && sampling[i] != 0) Include(x, y);
        }
        else
        {
            var grow = Math.Max(right - left, bottom - top) + 16; left -= grow; top -= grow; right += grow; bottom += grow;
        }
        left = Math.Max(0, left - 2); top = Math.Max(0, top - 2); right = Math.Min(width - 1, right + 2); bottom = Math.Min(height - 1, bottom + 2);
        int cropWidth = right - left + 1, cropHeight = bottom - top + 1;
        var cropMask = new byte[cropWidth * cropHeight]; byte[] sources = options.Sampling is null ? [] : new byte[cropWidth * cropHeight];
        for (var y = 0; y < cropHeight; y++)
            for (var x = 0; x < cropWidth; x++)
            {
                cropMask[y * cropWidth + x] = mask[(y + top) * width + x + left];
                if (options.Sampling is { } s && DocumentIndex(x + left, y + top) is var i and >= 0) sources[y * cropWidth + x] = s[i];
            }
        var pixels = work.GetPixelSpan();
        // Another picture to sample: its pixels go in for the fill, and the layer's own come back outside the hole.
        byte[]? kept = null;
        if (options.Source is { } source)
        {
            kept = new byte[cropWidth * cropHeight * 4]; var from = source.GetPixelSpan();
            for (var y = 0; y < cropHeight; y++)
                for (var x = 0; x < cropWidth; x++)
                {
                    var at = (y + top) * stride + (x + left) * 4; pixels.Slice(at, 4).CopyTo(kept.AsSpan((y * cropWidth + x) * 4, 4));
                    if (cropMask[y * cropWidth + x] != 0) continue;
                    var i = DocumentIndex(x + left, y + top);
                    if (i < 0) pixels.Slice(at, 4).Clear(); else from.Slice(i / document.Width * source.RowBytes + i % document.Width * 4, 4).CopyTo(pixels.Slice(at, 4));
                }
        }
        var copies = ContentFill.Copies(cropWidth, cropHeight, options.RotationAdaptation, options.Scale, options.Mirror);
        var filled = ContentFill.Fill(pixels[(top * stride + left * 4)..], stride, cropMask, cropWidth, cropWidth, cropHeight, sources, options.ColorAdaptation, copies) == 1;
        if (kept is not null)
            for (var y = 0; y < cropHeight; y++)
                for (var x = 0; x < cropWidth; x++)
                    if (cropMask[y * cropWidth + x] == 0) kept.AsSpan((y * cropWidth + x) * 4, 4).CopyTo(pixels.Slice((y + top) * stride + (x + left) * 4, 4));
        return filled;
    }

    /// <summary>
    /// The Auto and Rectangular sampling areas of Edit ▸ Content-Aware Fill, one byte a document pixel: Auto is a band around the
    /// selection about half the selection wide, Rectangular the box around the selection grown by as much; the selection is never in
    /// it. Given the picture the fill will copy from, the area widens until it holds three times as many opaque pixels as it fills.
    /// </summary>
    public static byte[] Sampling(CanvasDocument document, bool rectangular, SKBitmap? usable = null)
    {
        int width = document.Width, height = document.Height; var result = new byte[width * height];
        var region = document.Selection.CoverageRect(width, height); if (region.Width <= 0 || region.Height <= 0) return result;
        using var coverage = document.Selection.Coverage(region); if (coverage is null) return result;
        var selected = new float[width * height]; var span = coverage.GetPixelSpan(); var filled = 0;
        for (var y = 0; y < region.Height; y++) for (var x = 0; x < region.Width; x++)
            if (y + region.Top is var dy and >= 0 && dy < height && x + region.Left is var dx and >= 0 && dx < width && span[y * coverage.RowBytes + x] is var c and > 0) { selected[dy * width + dx] = c / 255f; filled++; }
        var alpha = usable is null ? null : new byte[width * height];
        if (usable is not null) { var pixels = usable.GetPixelSpan(); for (var i = 0; i < alpha!.Length; i++) alpha[i] = pixels[i / width * usable.RowBytes + i % width * 4 + 3]; }
        bool Usable(int i) => alpha is null || alpha[i] == 255;
        // The selection's own box, from its pixels: a raster selection reports the whole canvas as its bounds.
        int left = width, top = height, right = -1, bottom = -1;
        for (var i = 0; i < selected.Length; i++) if (selected[i] > 0) { int x = i % width, y = i / width; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < 0) return result;
        region = SKRectI.Create(left, top, right - left + 1, bottom - top + 1);
        var reach = (int)(Math.Max(region.Width, region.Height) * .5) + 16;
        var distance = rectangular ? null : Distances(selected, width, height);
        for (var attempt = 0; attempt < 5; attempt++, reach *= 2)
        {
            Array.Clear(result);
            if (rectangular)
            {
                for (var y = Math.Max(0, region.Top - reach); y < Math.Min(height, region.Bottom + reach); y++)
                    for (var x = Math.Max(0, region.Left - reach); x < Math.Min(width, region.Right + reach); x++) result[y * width + x] = 255;
            }
            else
            {
                // Anything within the reach of the selection is near enough to sample.
                for (var i = 0; i < distance!.Length; i++) if (distance[i] <= reach) result[i] = 255;
            }
            var count = 0;
            for (var i = 0; i < result.Length; i++) { if (selected[i] > 0) result[i] = 0; else if (result[i] != 0 && Usable(i)) count++; }
            if (usable is null || count >= filled * 3) break;
        }
        return result;
    }

    /// <summary>How far each pixel is from the nearest selected one, by a two-pass chamfer (3-4) distance, in pixels.</summary>
    private static float[] Distances(float[] selected, int width, int height)
    {
        var d = new float[selected.Length]; const float straight = 1, diagonal = 1.4142f;
        for (var i = 0; i < d.Length; i++) d[i] = selected[i] > 0 ? 0 : float.MaxValue / 4;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = y * width + x; var v = d[i];
            if (x > 0) v = Math.Min(v, d[i - 1] + straight);
            if (y > 0) { v = Math.Min(v, d[i - width] + straight); if (x > 0) v = Math.Min(v, d[i - width - 1] + diagonal); if (x + 1 < width) v = Math.Min(v, d[i - width + 1] + diagonal); }
            d[i] = v;
        }
        for (var y = height - 1; y >= 0; y--) for (var x = width - 1; x >= 0; x--)
        {
            var i = y * width + x; var v = d[i];
            if (x + 1 < width) v = Math.Min(v, d[i + 1] + straight);
            if (y + 1 < height) { v = Math.Min(v, d[i + width] + straight); if (x + 1 < width) v = Math.Min(v, d[i + width + 1] + diagonal); if (x > 0) v = Math.Min(v, d[i + width - 1] + diagonal); }
            d[i] = v;
        }
        return d;
    }

    /// <summary>
    /// The selection as the kernel wants it: set wherever it covers the layer pixel whose middle maps there.
    /// False when the selection cannot be read, or covers none of the grid, which leaves the layer alone.
    /// </summary>
    private static bool BuildMask(CanvasDocument document, LayerTransform placement,
        int width, int height, Span<byte> mask)
    {
        var region = document.Selection.CoverageRect(document.Width, document.Height);
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
        var toDocument = BrushEdits.PixelToDocument(placement, width, height);
        var any = false;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = toDocument.MapPoint(x + 0.5f, y + 0.5f);
                var column = (int)Math.Floor(at.X) - region.Left;
                var row = (int)Math.Floor(at.Y) - region.Top;
                if (column < 0 || row < 0 || column >= region.Width || row >= region.Height) continue;
                // Anything the selection touches at all is filled, as a filled path leaves its edge.
                if (clip[row * coverage.RowBytes + column] == 0) continue;
                mask[y * width + x] = 1;
                any = true;
            }
        }
        return any;
    }
}

/// <summary>Edit ▸ Content-Aware Fill's settings, as the workspace's panel holds them.</summary>
public sealed record ContentFillOptions
{
    /// <summary>One byte a document pixel: where the fill may copy from. Null lets it copy from anywhere.</summary>
    public byte[]? Sampling { get; init; }
    /// <summary>A document-sized picture to copy from instead of the layer: the composite for Sample All Layers.</summary>
    public SKBitmap? Source { get; init; }
    /// <summary>0 None, 1 Default, 2 High, 3 Very High.</summary>
    public int ColorAdaptation { get; init; } = 1;
    /// <summary>0 None, 1 Low, 2 Medium, 3 High, 4 Full.</summary>
    public int RotationAdaptation { get; init; }
    public bool Scale { get; init; }
    public bool Mirror { get; init; }
}
