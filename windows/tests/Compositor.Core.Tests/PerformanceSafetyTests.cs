using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class PerformanceSafetyTests
{
    [Fact]
    public void FailedAndCanceledOutputPreserveExistingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "compositor-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "image.png"); byte[] old = [1, 2, 3]; File.WriteAllBytes(file, old);
        try
        {
            Assert.Throws<IOException>(() => AtomicFile.Write(file, stream => { stream.WriteByte(99); throw new IOException("Injected failure"); }));
            Assert.Equal(old, File.ReadAllBytes(file));
            using var cancel = new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(() => AtomicFile.Write(file, stream => { stream.WriteByte(88); cancel.Cancel(); }, cancel.Token));
            Assert.Equal(old, File.ReadAllBytes(file));
            Assert.Single(Directory.GetFiles(directory));
            AtomicFile.WriteBytes(file, [7, 8]); Assert.Equal(new byte[] {7, 8}, File.ReadAllBytes(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void HistoryCountsSharedRasterSelectionsAndEvictsThem()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 256, 256);
        var history = new DocumentHistory(100, 2 * 256 * 256);
        for (var i = 0; i < 8; i++)
        {
            history.Begin("Select", doc, null);
            var mask = new SKBitmap(Bitmaps.MaskInfo(256, 256)); mask.Erase(SKColors.White);
            doc.Selection = DocumentSelection.FromCoverage(mask);
            history.End(doc, null);
        }
        Assert.InRange(history.RetainedBytes(doc), 1, history.RetainedByteLimit);
        Assert.True(history.UndoCount < 8);
        history.ClearSteps(); Assert.Equal(0, history.RetainedBytes(doc));
    }

    [Theory]
    [InlineData(SelectionPaintMode.New)]
    [InlineData(SelectionPaintMode.Add)]
    [InlineData(SelectionPaintMode.Subtract)]
    [InlineData(SelectionPaintMode.Intersect)]
    public void LocalSelectionMatchesFullCanvasCombination(SelectionPaintMode mode)
    {
        using var local = new CanvasDocument(Guid.NewGuid(), 90, 70);
        using var full = new CanvasDocument(Guid.NewGuid(), 90, 70);
        local.Selection = full.Selection = DocumentSelection.Rectangular(SKRectI.Create(9, 8, 45, 40), 90, 70).WithFeather(3);
        var source = new SKBitmap(Bitmaps.MaskInfo(17, 13)); source.Erase(new SKColor(160, 160, 160));
        var expanded = new SKBitmap(Bitmaps.MaskInfo(90, 70)); expanded.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(expanded)) canvas.DrawBitmap(source, new SKPoint(31, 26), new SKSamplingOptions(SKFilterMode.Nearest));
        SelectionBrushEdits.Combine(full, expanded, .4, mode);
        SelectionBrushEdits.Combine(local, source, .4, mode, new SKPointI(31, 26));
        using var a = full.Selection.Coverage(SKRectI.Create(90, 70));
        using var b = local.Selection.Coverage(SKRectI.Create(90, 70));
        Assert.True(a!.GetPixelSpan().SequenceEqual(b!.GetPixelSpan()));
    }

    [Fact]
    public void TinySelectionDoesNotAllocateACanvasSizedMask()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 4000, 3000);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(SelectionBrushEdits.Paint(doc, [new SKPoint(2000, 1500)], 10, 1, SelectionPaintMode.New));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000);
        Assert.InRange(doc.Selection.CoverageRect(doc.Width, doc.Height).Width, 1, 20);
        Assert.True(doc.Selection.Contains(2000, 1500));
        Assert.False(doc.Selection.Contains(10, 10));
    }

    [Fact]
    public void InvisibleAdjustmentDoesNotDisableSceneButVisibleOneDoes()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 10, 10);
        var layer = new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 10, 10), "Hidden")
            { IsVisible = false, Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 2 } };
        doc.Layers.Add(layer); Assert.True(DisplaySceneCache.Supports(doc));
        layer.IsVisible = true; Assert.False(DisplaySceneCache.Supports(doc));
    }
}
