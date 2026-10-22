using System.Diagnostics;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

// Reproducible synthetic performance probes, isolated process per case; no user documents or preferences.
var kind = args[0];
var width = args.Length > 1 ? int.Parse(args[1]) : 4000;
var height = width * 3 / 4;
var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("COMPOSITOR_PERFORMANCE_DIR") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "performance"));
Directory.CreateDirectory(directory);
if (kind == "verify")
{
    using var png = ImageImporter.Decode(Path.Combine(directory, "synthetic-8bit.png"));
    using var tiff = ImageImporter.Decode(Path.Combine(directory, "synthetic-8bit.tiff"));
    using var test = new CanvasDocument(Guid.NewGuid(), 4000, 3000);
    test.Layers.Add(new ImageLayer(Guid.NewGuid(), png, new LayerTransform(0, 0, 4000, 3000), "Input"));
    using var before = DocumentRenderer.Preview(test, 256);
    test.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 4000, 3000), "Hidden")
        { IsVisible = false, Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 2 } });
    using var after = DocumentRenderer.Preview(test, 256);
    var pa = png.Image.GetPixelSpan(); var pb = tiff.Image.GetPixelSpan(); var maxDelta = 0; long changed = 0;
    for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++) { var delta = Math.Abs(pa[i] - pb[i]); maxDelta = Math.Max(maxDelta, delta); if (delta > 0) changed++; }
    Console.WriteLine(JsonSerializer.Serialize(new { PngDepth = png.BitDepth, TiffDepth = tiff.BitDepth,
        DecodedPixelsEqual = png.Image.GetPixelSpan().SequenceEqual(tiff.Image.GetPixelSpan()),
        PngPixel = png.Image.GetPixel(0, 0).ToString(), TiffPixel = tiff.Image.GetPixel(0, 0).ToString(), MaxChannelDelta = maxDelta, DifferentChannelSamples = changed,
        PreviewPixelsEqualWithHiddenAdjustment = before.GetPixelSpan().SequenceEqual(after.GetPixelSpan()) }));
    test.Layers.Clear(); return;
}
if (kind == "history-selection")
{
    using var test = new CanvasDocument(Guid.NewGuid(), 1024, 1024);
    var history = new DocumentHistory(100, 16 * 1024 * 1024);
    for (var i = 0; i < 20; i++)
    {
        history.Begin("Selection", test, null);
        var mask = new SKBitmap(Bitmaps.MaskInfo(1024, 1024)); mask.Erase(SKColors.White);
        test.Selection = DocumentSelection.FromCoverage(mask);
        history.End(test, null);
    }
    var field = typeof(DocumentSelection).GetField("_coverage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    var live = field.GetValue(test.Selection);
    var old = history.States(test, null).Select(s => s.Snapshot.Document?.Selection)
        .Where(s => s is not null).Select(s => field.GetValue(s!)).OfType<SKBitmap>()
        .Where(b => !ReferenceEquals(b, live)).Distinct().ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { history.UndoCount, history.RetainedByteLimit,
        ReportedRetainedBytes = history.RetainedBytes(test), UniqueOldCoverageBuffers = old.Length,
        ActualRetainedCoverageBytes = old.Sum(b => (long)b.RowBytes * b.Height) })); return;
}
if (kind == "export-failure")
{
    var output = Path.Combine(directory, "synthetic-export-failure.png");
    var original = new byte[] { 1, 3, 5, 7, 9 }; File.WriteAllBytes(output, original);
    using var test = new CanvasDocument(Guid.NewGuid(), 64, 64);
    var bitmap = new SKBitmap(Bitmaps.ColorInfo(64, 64)); bitmap.Erase(SKColors.Red);
    test.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Failure fixture"), new LayerTransform(0, 0, 64, 64), "Failure fixture")
        { Effects = new LayerEffects { Shadow = new ShadowEffect { Opacity = 2 } } });
    string? error = null;
    try { TiledPngWriter.Write(test, output); } catch (Exception ex) { error = ex.GetType().Name; }
    Console.WriteLine(JsonSerializer.Serialize(new { InjectedFault = "Invalid shadow opacity forces a rendering exception after output open", Error = error,
        OriginalPreserved = File.ReadAllBytes(output).SequenceEqual(original), ResultBytes = new FileInfo(output).Length })); return;
}
if (kind == "prepare")
{
    using var input = new MagickImage(MagickColors.CornflowerBlue, (uint)width, (uint)height);
    input.Depth = 8;
    input.Write(Path.Combine(directory, "synthetic-8bit.tiff"));
    input.Write(Path.Combine(directory, "synthetic-8bit.psd"));
    input.Write(Path.Combine(directory, "synthetic-8bit.png"));
    Console.WriteLine("Prepared synthetic 8-bit images."); return;
}
using var document = new CanvasDocument(Guid.NewGuid(), width, height);
var pixels = new SKBitmap(Bitmaps.ColorInfo(64, 64)); pixels.Erase(SKColors.CornflowerBlue);
document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Synthetic"),
    new LayerTransform(0, 0, width, height), "Synthetic"));
if (kind == "preview-hidden")
    document.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, width, height), "Hidden blur")
        { IsVisible = false, Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 2 } });
using var blur = new SKBitmap(Bitmaps.ColorInfo(kind.StartsWith("blur-") ? width : 1, kind.StartsWith("blur-") ? height : 1)); blur.Erase(SKColors.CornflowerBlue);
void Operation()
{
    switch (kind)
    {
        case "preview": case "preview-hidden": using (DocumentRenderer.Preview(document, 256)) { } break;
        case "render-fit": using (DocumentRenderer.RenderRegion(document, SKRectI.Create(width, height))) { } break;
        case "render-tile": using (DocumentRenderer.RenderRegion(document, SKRectI.Create(512, 512))) { } break;
        case "selection": SelectionBrushEdits.Paint(document, [new SKPoint(width / 2, height / 2)], 10, 1, SelectionPaintMode.New); break;
        case "blur-8": case "blur-9": GaussianBlur.Clamped(blur.GetPixelSpan(), width, height, 4, blur.RowBytes, kind == "blur-8" ? 8 : 9); break;
        case "decode-tiff": case "decode-png": using (ImageImporter.Decode(Path.Combine(directory, kind == "decode-tiff" ? "synthetic-8bit.tiff" : "synthetic-8bit.png"))) { } break;
        default: throw new ArgumentException(kind);
    }
}
using var process = Process.GetCurrentProcess();
process.Refresh(); var baseline = process.WorkingSet64;
var cold = Stopwatch.StartNew(); Operation(); cold.Stop();
var measurements = new List<object>();
for (var i = 0; i < 5; i++)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var allocated = GC.GetTotalAllocatedBytes(true); var cpu = process.TotalProcessorTime;
    var watch = Stopwatch.StartNew(); Operation(); watch.Stop();
    var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    process.Refresh();
    measurements.Add(new { Ms = watch.Elapsed.TotalMilliseconds, CpuMs = cpuMs,
        ManagedAllocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0,
        WorkingSetMiB = process.WorkingSet64 / 1048576.0, PrivateMiB = process.PrivateMemorySize64 / 1048576.0 });
}
process.Refresh();
Console.WriteLine(JsonSerializer.Serialize(new { Kind = kind, Width = width, Height = height,
    DisplaySceneSupported = DisplaySceneCache.Supports(document), ColdMs = cold.Elapsed.TotalMilliseconds,
    BaselineWorkingSetMiB = baseline / 1048576.0, PeakWorkingSetMiB = process.PeakWorkingSet64 / 1048576.0,
    GCBeforeEachMeasuredIteration = true, Samples = measurements }, new JsonSerializerOptions { WriteIndented = false }));
