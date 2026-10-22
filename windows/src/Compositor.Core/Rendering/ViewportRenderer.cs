using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Rendering;
/// <summary>Screen-sized CPU output assembled from exact tiles with the renderer's existing halos.</summary>
public static class ViewportRenderer
{
    public static SKBitmap Render(CanvasDocument document, SKRectI region, int maximum,
        Action<SKBitmap, SKRectI>? transform = null, CancellationToken cancellation = default, ViewportTileCache? tileCache = null)
    {
        var scale = Math.Min(1, Math.Max(1, maximum) / (double)Math.Max(region.Width, region.Height));
        var output = Bitmaps.Allocate(new SKImageInfo(Math.Max(1, (int)Math.Ceiling(region.Width * scale)), Math.Max(1, (int)Math.Ceiling(region.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var cache = new RenderCache(); using var canvas = new SKCanvas(output); canvas.Clear();
            var deep = document.Layers.Any(l => l.Asset?.Deep is not null) && DocumentRenderer.SupportsDeepComposite(document);
            // Batch adjacent cold tiles so masks, composition setup and effect halos are evaluated once.
            // Warm tiles still draw directly; low memory profiles cap each working batch at 1 megapixel.
            const int batch = 1024;
            for (var by = Math.Max(0, region.Top / batch * batch); by < Math.Min(document.Height, region.Bottom); by += batch)
            for (var bx = Math.Max(0, region.Left / batch * batch); bx < Math.Min(document.Width, region.Right); bx += batch)
            {
                cancellation.ThrowIfCancellationRequested();
                var pending = new List<SKRectI>();
                for (var y = by; y < Math.Min(by + batch, document.Height); y += 512)
                for (var x = bx; x < Math.Min(bx + batch, document.Width); x += 512)
                {
                    var tile = SKRectI.Create(x, y, Math.Min(512, document.Width - x), Math.Min(512, document.Height - y));
                    if (!tile.IntersectsWith(region)) continue;
                    using var hit = tileCache?.TryAcquire(tile);
                    if (hit is null) pending.Add(tile); else Draw(hit.Pixels, tile);
                }
                if (pending.Count == 0) continue;
                var bounds = pending.Aggregate(SKRectI.Union);
                using var floats = deep ? DocumentRenderer.RenderDeep(document, bounds) : null;
                using var rendered = floats is null ? DocumentRenderer.RenderRegion(document, bounds) : DeepPixels.Preview(floats);
                foreach (var tile in pending)
                {
                    cancellation.ThrowIfCancellationRequested();
                    SKBitmap Extract()
                    {
                        var pixels = Bitmaps.Allocate(rendered.Info.WithSize(tile.Width, tile.Height));
                        for (var row = 0; row < tile.Height; row++) rendered.GetPixelSpan().Slice((tile.Top - bounds.Top + row) * rendered.RowBytes + (tile.Left - bounds.Left) * 4, tile.Width * 4).CopyTo(pixels.GetPixelSpan().Slice(row * pixels.RowBytes, tile.Width * 4));
                        try { transform?.Invoke(pixels, tile); return pixels; } catch { pixels.Dispose(); throw; }
                    }
                    using var kept = tileCache?.Acquire(tile, Extract); using var owned = kept is null ? Extract() : null; Draw(kept?.Pixels ?? owned!, tile);
                }
            }
            return output;
            void Draw(SKBitmap pixels, SKRectI tile) => canvas.DrawBitmap(pixels, new SKRect((float)((tile.Left - region.Left) * scale), (float)((tile.Top - region.Top) * scale), (float)((tile.Right - region.Left) * scale), (float)((tile.Bottom - region.Top) * scale)), new SKSamplingOptions(SKFilterMode.Linear));
        }
        catch { output.Dispose(); throw; }
    }
}
