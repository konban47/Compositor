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

    [Fact]
    public void EditingSmallLayerReusesOnlyUnaffectedTilesAndPreservesBlurHalo()
    {
        using var document = Scene();
        var small = Bitmaps.Allocate(Bitmaps.ColorInfo(32, 32)); small.Erase(SKColors.Gold);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(small, "Small"), new Model.LayerTransform(20, 20, 32, 32), "Small");
        document.Layers.Insert(1, layer);
        using var original = document.Clone(); using var oldCache = new ViewportTileCache();
        var region = SKRectI.Create(document.Width, document.Height);
        using var first = ViewportRenderer.Render(document, region, 2048, tileCache: oldCache);
        layer.Transform = layer.Transform with { X = 480 };
        var damage = ViewportDamage.Between(original, document);
        Assert.NotNull(damage);
        using var next = oldCache.Fork(damage); oldCache.Dispose();
        using var actual = ViewportRenderer.Render(document, region, 2048, tileCache: next);
        using var expected = DocumentRenderer.Render(document);
        Assert.True(expected.GetPixelSpan().SequenceEqual(actual.GetPixelSpan()));
        Assert.Equal(2, next.Hits); Assert.Equal(2, next.RenderedTiles);
        using var unchanged = document.Clone();
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Note, X = 10, Y = 10 });
        Assert.Equal(SKRectI.Empty, ViewportDamage.Between(unchanged, document));
        document.Layers[^1].Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 8 };
        Assert.Null(ViewportDamage.Between(unchanged, document));
    }

    [Fact]
    public async Task OldRevisionFinishingLateCannotInsertStalePixelsIntoFork()
    {
        using var previous = new ViewportTileCache();
        var region = SKRectI.Create(2, 2);
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var pending = Task.Run(() => previous.Acquire(region, () => {
            entered.Set(); finish.Wait(); var b = Bitmaps.Allocate(Bitmaps.ColorInfo(2, 2)); b.Erase(SKColors.Red); return b;
        }));
        entered.Wait(); using var next = previous.Fork(region); previous.Dispose(); finish.Set();
        using var old = await pending;
        using var current = next.Acquire(region, () => { var b = Bitmaps.Allocate(Bitmaps.ColorInfo(2, 2)); b.Erase(SKColors.Blue); return b; });
        Assert.Equal(SKColors.Red, old.Pixels.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, current.Pixels.GetPixel(0, 0));
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
