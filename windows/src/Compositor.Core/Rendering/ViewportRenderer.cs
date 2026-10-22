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
        var output = Bitmaps.Allocate(new SKImageInfo(Math.Max(1, (int)Math.Ceiling(region.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(region.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var cache = new RenderCache();
            using var canvas = new SKCanvas(output); canvas.Clear();
            var deep = document.Layers.Any(l => l.Asset?.Deep is not null) && DocumentRenderer.SupportsDeepComposite(document);
            for (var y = Math.Max(0, region.Top / 512 * 512); y < Math.Min(document.Height, region.Bottom); y += 512)
                for (var x = Math.Max(0, region.Left / 512 * 512); x < Math.Min(document.Width, region.Right); x += 512)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var tileRegion = SKRectI.Create(x, y, Math.Min(512, document.Width - x), Math.Min(512, document.Height - y));
                    SKBitmap RenderTile()
                    {
                        using var floats = deep ? DocumentRenderer.RenderDeep(document, tileRegion) : null;
                        var made = floats is null ? DocumentRenderer.RenderRegion(document, tileRegion) : DeepPixels.Preview(floats);
                        try { transform?.Invoke(made, tileRegion); return made; }
                        catch { made.Dispose(); throw; }
                    }
                    using var retained = tileCache?.Acquire(tileRegion, RenderTile);
                    using var owned = retained is null ? RenderTile() : null;
                    var tile = retained?.Pixels ?? owned!;
                    canvas.DrawBitmap(tile, new SKRect((float)((x - region.Left) * scale), (float)((y - region.Top) * scale),
                        (float)((tileRegion.Right - region.Left) * scale), (float)((tileRegion.Bottom - region.Top) * scale)), new SKSamplingOptions(SKFilterMode.Linear));
                }
            return output;
        }
        catch { output.Dispose(); throw; }
    }
}
