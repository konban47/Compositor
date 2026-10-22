using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

internal static class ViewportProbe
{
    internal static void Run(string directory, int width, int height)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), width, height);
        for (var i = 0; i < 12; i++)
        {
            var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(128, 128));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(new SKColor((byte)(i * 19), 80, 140));
                using var paint = new SKPaint { Color = SKColors.Gold, IsAntialias = true };
                for (var x = 0; x < 128; x += 11) canvas.DrawCircle(x, (x * 7 + i) % 128, 6, paint);
            }
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Pattern"),
                new LayerTransform(i * 23, i * 17, width - i * 46, height - i * 34), $"Pattern {i}") { Opacity = .65 });
        }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, width, height), "Visible blur")
            { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 4 } });
        ProjectStore.Save(ProjectSnapshot.FromDocument(document), Path.Combine(directory, "complex-viewport.comp"));
        using var cache = new ViewportTileCache();
        var uncached = new List<double>(); var cached = new List<double>();
        var pixelsEqual = true;
        using var process = Process.GetCurrentProcess();
        for (var i = 0; i < 8; i++)
        {
            var region = SKRectI.Create(128 + i * 13, 32 + i * 7, Math.Min(1536, width), Math.Min(1024, height));
            var start = Stopwatch.GetTimestamp();
            using var expected = ViewportRenderer.Render(document, region, 1024);
            uncached.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            start = Stopwatch.GetTimestamp();
            using var actual = ViewportRenderer.Render(document, region, 1024, tileCache: cache);
            cached.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            pixelsEqual &= expected.GetPixelSpan().SequenceEqual(actual.GetPixelSpan());
        }
        var markerPixels = Bitmaps.Allocate(Bitmaps.ColorInfo(48, 48)); markerPixels.Erase(SKColors.Lime);
        var marker = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(markerPixels, "Edit marker"), new LayerTransform(300, 200, 48, 48), "Edit marker"); document.Layers.Insert(document.Layers.Count - 1, marker);
        using var editBaseCache = new ViewportTileCache();
        var editRegion = SKRectI.Create(128, 32, Math.Min(1536, width), Math.Min(1024, height));
        using (ViewportRenderer.Render(document, editRegion, 1024, tileCache: editBaseCache)) { }
        using var before = document.Clone(); marker.Transform = marker.Transform with { X = marker.Transform.X + 16 };
        using var editedCache = editBaseCache.Fork(ViewportDamage.Between(before, document));
        var editStart = Stopwatch.GetTimestamp(); using var edited = ViewportRenderer.Render(document, editRegion, 1024, tileCache: editedCache);
        var editedMs = Stopwatch.GetElapsedTime(editStart).TotalMilliseconds;
        editStart = Stopwatch.GetTimestamp(); using var editReference = ViewportRenderer.Render(document, editRegion, 1024);
        var editReferenceMs = Stopwatch.GetElapsedTime(editStart).TotalMilliseconds;
        var editedEqual = editReference.GetPixelSpan().SequenceEqual(edited.GetPixelSpan());
        process.Refresh();
        Console.WriteLine(JsonSerializer.Serialize(new { Kind = "viewport-cache", Width = width, Height = height,
            OS = RuntimeInformation.OSDescription, ProcessorCount = Environment.ProcessorCount,
            Simd = System.Numerics.Vector.IsHardwareAccelerated, BudgetMiB = MemoryBudget.Current.Limit / 1048576,
            EditedMs = editedMs, EditReferenceMs = editReferenceMs, EditedPixelsEqual = editedEqual, EditTileHits = editedCache.Hits, EditTilesRendered = editedCache.RenderedTiles,
            PixelsEqual = pixelsEqual, UncachedMs = uncached, CachedMs = cached,
            ColdCacheMs = cached[0], WarmMedianMs = cached.Skip(1).Order().ElementAt(3),
            UncachedMedianMs = uncached.Order().ElementAt(4), cache.Hits, cache.RenderedTiles, cache.RetainedBytes,
            PeakWorkingSetMiB = process.PeakWorkingSet64 / 1048576.0 }));
        if (!editedEqual || editedCache.Hits == 0 || !pixelsEqual || cache.Hits == 0 || cache.RetainedBytes > Math.Min(128L << 20, MemoryBudget.Current.Limit / 8))
            throw new InvalidOperationException("Viewport cache correctness or budget gate failed.");
    }
}
