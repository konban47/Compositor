using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class ViewportCacheTests
{
    [Fact]
    public void PanningReusesExactTilesAcrossBlurHalosAndCanvasEdges()
    {
        using var document = Scene(); using var cache = new ViewportTileCache(16 << 20);
        foreach (var region in new[] { SKRectI.Create(0, 0, 800, 600), SKRectI.Create(137, 51, 800, 600), SKRectI.Create(-20, -10, 1000, 800) })
        {
            using var expected = DocumentRenderer.RenderRegion(document, region);
            using var actual = ViewportRenderer.Render(document, region, 2048, tileCache: cache);
            Assert.True(expected.GetPixelSpan().SequenceEqual(actual.GetPixelSpan()));
        }
        Assert.True(cache.Hits > 0); Assert.Equal(4, cache.RenderedTiles);
        Assert.InRange(cache.RetainedBytes, 1, 16 << 20);
    }

    [Fact]
    public void EvictionAndRevisionDisposalKeepActiveReadersAlive()
    {
        using var cache = new ViewportTileCache(16);
        SKBitmap Pixel(SKColor color) { var b = Bitmaps.Allocate(Bitmaps.ColorInfo(2, 2)); b.Erase(color); return b; }
        using var red = cache.Acquire(SKRectI.Create(2, 2), () => Pixel(SKColors.Red));
        using var blue = cache.Acquire(SKRectI.Create(2, 0, 2, 2), () => Pixel(SKColors.Blue));
        Assert.Equal(16, cache.RetainedBytes); Assert.Equal(SKColors.Red, red.Pixels.GetPixel(0, 0));
        cache.Dispose(); Assert.Equal(0, cache.RetainedBytes);
        Assert.Equal(SKColors.Blue, blue.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void FailedAndCanceledTilesDoNotPoisonTheNextRender()
    {
        using var cache = new ViewportTileCache(); using var document = Scene();
        var region = SKRectI.Create(300, 200);
        Assert.Throws<InvalidOperationException>(() => cache.Acquire(region, () => throw new InvalidOperationException()));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => ViewportRenderer.Render(document, region, 2048, cancellation: stop.Token, tileCache: cache));
        Assert.Equal(0, cache.RetainedBytes);
        using var rendered = ViewportRenderer.Render(document, region, 2048, tileCache: cache);
        Assert.True(cache.RetainedBytes > 0);
    }

    private static CanvasDocument Scene()
    {
        var d = new CanvasDocument(Guid.NewGuid(), 960, 720);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(960, 720));
        using (var canvas = new SKCanvas(image))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawCircle(512, 480, 190, paint);
        }
        d.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Pattern"), new Model.LayerTransform(0, 0, 960, 720), "Pattern"));
        d.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 960, 720), "Blur")
        { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 4 } });
        return d;
    }
}
