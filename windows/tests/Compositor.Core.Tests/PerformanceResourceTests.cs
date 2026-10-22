using System.Buffers.Binary;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.IO.PSD;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class PerformanceResourceTests
{
    [Fact]
    public void DeepPaintingRetainsUnassociatedAlphaConversion()
    {
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(8, 8)); source.Erase(new SKColor(120, 60, 30, 128));
        using var before = ImportedImage.CreateDeep(DeepPixels.From(source), "Before", 32);
        var painted = Bitmaps.Allocate(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        painted.Erase(new SKColor(30, 150, 80, 64));
        using var after = ImportedImage.Create(painted, "After");
        using var expected = DeepPixels.From(after.Image);
        using var actual = DeepPixels.Painted(before, after);
        Assert.True(expected.GetPixelSpan().SequenceEqual(actual.Deep!.GetPixelSpan()));
    }

    [Fact]
    public void FloatSidecarLimitIsValidatedBeforeCreatingAnUnsavableAsset()
    {
        FloatPixels.ValidateSize(8192, 4096);
        Assert.Throws<ProjectException>(() => FloatPixels.ValidateSize(8192, 4097));
    }

    [Fact]
    public void SharedReservationsAccountForCommittedMemoryAndReleaseAfterFailure()
    {
        long committed = 20;
        var budget = new MemoryBudget(100, () => committed);
        using (budget.Reserve(50))
        {
            Assert.Equal(50, budget.ReservedBytes);
            Assert.Throws<OutOfMemoryException>(() => budget.Reserve(31));
            committed = 40; Assert.Throws<OutOfMemoryException>(() => budget.Check(11));
        }
        Assert.Equal(0, budget.ReservedBytes); budget.Check(60);
        Assert.Equal(64_000_000, MemoryBudget.PixelBytes(2000, 2000, 16));
        Assert.Throws<OverflowException>(() => MemoryBudget.PixelBytes(int.MaxValue, int.MaxValue, 16));
    }

    [Fact]
    public void BudgetUsesBytesAndPreventsIncompleteBlur()
    {
        using var limit = new MemoryBudget(100_000, () => 0).Enter();
        using var rgba = Bitmaps.Allocate(Bitmaps.ColorInfo(100, 100));
        Assert.Throws<OutOfMemoryException>(() => Bitmaps.Allocate(DeepPixels.Info(100, 100)));
        byte[] pixels = Enumerable.Repeat((byte)40, 400 * 400 * 4).ToArray();
        var original = pixels.ToArray();
        Assert.Throws<OutOfMemoryException>(() => GaussianBlur.Clamped(pixels, 400, 400, 4, 1600, 8));
        Assert.Equal(original, pixels);
    }

    [Fact]
    public void BackgroundLeaseKeepsPixelsAliveAfterDocumentCloses()
    {
        var document = Sample(32, 24); var asset = document.Layers[0].Asset!;
        using (var snapshot = new DocumentLease(document))
        {
            document.Dispose(); document.Dispose();
            using var rendered = DocumentRenderer.Render(snapshot.Document);
            Assert.Equal((byte)255, rendered.GetPixel(4, 4).Alpha);
            Assert.NotEqual(IntPtr.Zero, asset.Image.Handle);
        }
        Assert.Equal(IntPtr.Zero, asset.Image.Handle);
    }

    [Fact]
    public void CanceledProjectSaveLeavesThePublishedPackageUnchanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "compositor-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "image.comp");
        using var document = Sample(32, 24); using var snapshot = ProjectSnapshot.FromDocument(document);
        try
        {
            ProjectStore.Save(snapshot, path);
            var manifest = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => ProjectStore.Save(snapshot, path, cancellation: cancel.Token));
            Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
            Assert.Single(Directory.GetDirectories(directory));
            using var loaded = ProjectStore.Load(path); Assert.Single(loaded.Images);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void StreamEncodersPreserveEightAndSixteenBitAssets()
    {
        using var document = Sample(43, 29); var source = document.Layers[0].Asset!.Image;
        using var png = new MemoryStream(); PngCodec.Encode(source, png);
        using var decoded = PngCodec.DecodeImage(PngCodec.ReadHeader(png.ToArray()), png.ToArray());
        Assert.True(source.GetPixelSpan().SequenceEqual(decoded.GetPixelSpan()));
        using var deep = DeepPixels.From(source);
        using var output = new MemoryStream(); DeepImageCodec.EncodePng(deep, output);
        using var restored = DeepImageCodec.Decode(output.ToArray(), "deep");
        Assert.Equal(16, restored.BitDepth); Assert.Equal(source.Width, restored.Width);
        using var floats = new MemoryStream(); FloatPixels.Encode(deep, floats);
        using var restoredFloat = FloatPixels.Decode(floats.ToArray(), source.Width, source.Height);
        Assert.True(deep.GetPixelSpan().SequenceEqual(restoredFloat.GetPixelSpan()));
    }

    [Fact]
    public void EightBitTiffDoesNotCreateADeepAssetAndPreservesPixels()
    {
        using var document = Sample(127, 65); var source = document.Layers[0].Asset!.Image;
        using var image = DeepImageCodec.Magick(source); image.Depth = 8;
        using var decoded = DeepImageCodec.Decode(image.ToByteArray(MagickFormat.Tiff), "eight");
        Assert.Null(decoded.Deep); Assert.Equal(8, decoded.BitDepth);
        var original = source.GetPixelSpan(); var actual = decoded.Image.GetPixelSpan();
        for (var i = 0; i < original.Length; i++) Assert.InRange(Math.Abs(original[i] - actual[i]), 0, 1);
    }

    [Fact]
    public void ExactViewportTilesHaveNoBlurSeamAndCacheIsBounded()
    {
        using var document = Sample(1030, 70);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 1030, 70), "Blur")
            { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 4 } });
        using var whole = DocumentRenderer.Render(document);
        using var tiled = ViewportRenderer.Render(document, SKRectI.Create(1030, 70), 1030);
        var a = whole.GetPixelSpan(); var b = tiled.GetPixelSpan();
        for (var i = 0; i < a.Length; i++) Assert.InRange(Math.Abs(a[i] - b[i]), 0, 1);
        using var cache = new RenderCache(32);
        using var raster = new EffectRaster(Bitmaps.Allocate(Bitmaps.ColorInfo(8, 8)), 0, 0);
        Assert.False(cache.Store(document.Layers[0], raster)); Assert.Equal(0, cache.RetainedBytes);
    }

    [Fact]
    public void ViewportCancellationStopsBetweenTiles()
    {
        using var document = Sample(1030, 70); using var cancel = new CancellationTokenSource(); var tiles = 0;
        Assert.Throws<OperationCanceledException>(() => ViewportRenderer.Render(document, SKRectI.Create(1030, 70), 400,
            (_, _) => { tiles++; cancel.Cancel(); }, cancel.Token));
        Assert.Equal(1, tiles);
    }

    [Fact]
    public void PreviewForkIsIndependentOfTheDisplayedPreview()
    {
        using var document = Sample(32, 24); var id = document.Layers[0].ID;
        using var preview = FilterPreview.Begin(document, id)!;
        using var worker = preview.Fork()!;
        var original = preview.Document.Layers[0].Asset;
        Assert.True(worker.Show((target, layer) => FilterEdits.ApplyAdjustment(target, layer,
            new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 2 })));
        Assert.Same(original, preview.Document.Layers[0].Asset);
        Assert.NotSame(original, worker.Document.Layers[0].Asset);
    }

    [Fact]
    public void PsbSkipsMultiGigabyteBlocksWithOnlySmallReads()
    {
        using var prefix = new MemoryStream();
        prefix.Write("8BPS"u8); U16(prefix, 2); prefix.Write(new byte[6]); U16(prefix, 3);
        U32(prefix, 1); U32(prefix, 1); U16(prefix, 8); U16(prefix, 3); U32(prefix, 0);
        const uint skipped = 0x80000040; U32(prefix, skipped);
        using var source = new SparseInput(prefix.ToArray(), prefix.Length + skipped, [0,0,0,0,0,0,0,0, 0,0, 12,34,56]);
        var document = PsdReader.Read(source, 100, "large");
        try
        {
            Assert.Single(document.Layers);
            Assert.Equal(new SKColor(12, 34, 56), document.Layers[0].Image!.GetPixel(0, 0));
            Assert.InRange(source.BytesRead, 1, 256);
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); source.Position = 0;
            Assert.Throws<OperationCanceledException>(() => PsdReader.Read(source, 100, "large", cancel.Token));
        }
        finally { foreach (var layer in document.Layers) { layer.Image?.Dispose(); layer.Mask?.Dispose(); } }
    }

    [Fact]
    public void HiddenFolderDoesNotDisableDisplayAcceleration()
    {
        using var document = Sample(20, 20);
        var folder = new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 20, 20), "Group") { IsGroup = true, IsVisible = false };
        document.Layers.Add(folder);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 20, 20), "Blur")
            { ParentID = folder.ID, Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 4 } });
        Assert.True(DisplaySceneCache.Supports(document)); folder.IsVisible = true;
        Assert.False(DisplaySceneCache.Supports(document));
    }

    private static CanvasDocument Sample(int width, int height)
    {
        var document = new CanvasDocument(Guid.NewGuid(), width, height);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height));
        var bytes = image.GetPixelSpan();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var p = y * image.RowBytes + x * 4; bytes[p] = (byte)(x * 17); bytes[p + 1] = (byte)(y * 23); bytes[p + 2] = 56; bytes[p + 3] = 255; }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Layer"), new Model.LayerTransform(0, 0, width, height), "Layer"));
        return document;
    }
    private static void U16(Stream output, ushort value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); output.Write(bytes); }
    private static void U32(Stream output, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); output.Write(bytes); }
    private sealed class SparseInput(byte[] prefix, long tailAt, byte[] tail) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => tailAt + tail.Length;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, Length - Position); BytesRead += count;
            for (var i = 0; i < count; i++, Position++) buffer[i] = Position < prefix.Length ? prefix[(int)Position]
                : Position >= tailAt ? tail[(int)(Position - tailAt)] : (byte)0;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) { Position = offset + (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? Position : Length); return Position; }
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
