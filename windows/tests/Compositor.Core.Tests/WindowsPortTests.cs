using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class WindowsPortTests
{
    [Fact]
    public void SubjectOutlineKeepsHolesAndOnlyTheClickedComponent()
    {
        using var matte = Bitmaps.Allocate(Bitmaps.MaskInfo(12, 8));
        matte.Erase(SKColors.Black);
        var pixels = matte.GetPixelSpan();
        for (var y = 1; y < 7; y++)
            for (var x = 1; x < 5; x++) pixels[y * matte.RowBytes + x] = 255;
        pixels[3 * matte.RowBytes + 3] = 0;
        pixels[2 * matte.RowBytes + 9] = 255;
        using var whole = SubjectEdits.Outline(matte);
        using var clicked = SubjectEdits.Outline(matte, new SKPoint(2, 2));
        Assert.True(whole.Contains(9.5f, 2.5f));
        Assert.False(clicked.Contains(9.5f, 2.5f));
        Assert.True(clicked.Contains(1.5f, 1.5f));
        Assert.False(clicked.Contains(3.5f, 3.5f));
        using var background = SubjectEdits.Outline(matte, new SKPoint(0, 0));
        Assert.True(background.IsEmpty);
    }

    [Fact]
    public void RemovingBackgroundPreservesPixelsAndExistingMaskAndCanBeUndone()
    {
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(4, 4)); bitmap.Erase(SKColors.Red);
        using var document = ImageImporter.NewDocument(ImportedImage.Create(bitmap, "subject"));
        var layer = document.Layers[0];
        var originalMask = Bitmaps.Allocate(Bitmaps.MaskInfo(4, 4)); originalMask.Erase(new SKColor(128, 128, 128));
        layer.Mask = LayerMask.AssetFrom(originalMask);
        using var matte = Bitmaps.Allocate(Bitmaps.MaskInfo(4, 4)); matte.Erase(new SKColor(128, 128, 128));
        var history = new DocumentHistory(); history.Begin("Remove Background", document, layer.ID);
        Assert.True(SubjectEdits.RemoveBackground(document, layer.ID, matte));
        history.End(document, layer.ID);
        Assert.Same(bitmap, layer.Asset!.Image);
        Assert.InRange(layer.Mask!.Asset.Image.GetPixel(2, 2).Red, 63, 65);
        Assert.Equal(128, originalMask.GetPixel(2, 2).Red);
        document.Adopt(history.Undo()!.Value.Document!);
        Assert.Same(originalMask, document.Layers[0].Mask!.Asset.Image);
    }

    [Fact]
    public void ExistingMaskPlacementIsMappedIntoLayerPixels()
    {
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(4, 4)); bitmap.Erase(SKColors.Red);
        using var document = ImageImporter.NewDocument(ImportedImage.Create(bitmap, "subject"));
        var layer = document.Layers[0];
        layer.Mask = LayerMask.Solid(true);
        layer.Mask!.Placement = new LayerTransform(2, 0, 2, 4);
        layer.Mask.IsLinked = false;
        using var matte = Bitmaps.Allocate(Bitmaps.MaskInfo(4, 4)); matte.Erase(SKColors.White);
        Assert.True(SubjectEdits.RemoveBackground(document, layer.ID, matte));
        Assert.Equal(0, layer.Mask!.Asset.Image.GetPixel(0, 2).Red);
        Assert.Equal(255, layer.Mask.Asset.Image.GetPixel(3, 2).Red);
    }

    [Fact]
    public void BundledModelRunsOnCpuAndExcludesTransparentPixels()
    {
        using var detector = new SubjectSegmentation();
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(48, 32)); source.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Red }) canvas.DrawCircle(24, 16, 10, paint);
        using var matte = detector.Predict(source);
        Assert.True(Bitmaps.IsValidMask(matte));
        Assert.Equal(48, matte.Width); Assert.Equal(32, matte.Height);
        Assert.Equal(0, matte.GetPixel(0, 0).Red);
        Assert.Contains(matte.GetPixelSpan().ToArray(), value => value > 128);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => detector.Predict(source, cancelled.Token));
    }

    [Fact]
    public void AlteredModelIsRejectedBeforeNativeLoading()
    {
        var file = Path.GetTempFileName();
        try { File.WriteAllText(file, "not the model"); Assert.Throws<InvalidDataException>(() => new SubjectSegmentation(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void CompletingAnOlderSaveKeepsNewerEditsDirty()
    {
        using var document = LayerPlacement.NewDocument(16, 16)!;
        var history = new DocumentHistory(); history.MarkUnsaved();
        Assert.True(history.IsModified);
        var revision = history.CurrentRevision;
        history.Begin("Rename", document, null); document.Layers[0].Name = "Changed"; history.End(document, null);
        history.MarkSaved(revision);
        Assert.True(history.IsModified);
        document.Adopt(history.Undo()!.Value.Document!);
        Assert.False(history.IsModified);
    }

    [Fact]
    public void SavingCannotReplaceAnUnrelatedDirectory()
    {
        var folder = Path.Combine(Path.GetTempPath(), "compositor-save-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "keep.txt"), "user data");
            using var document = LayerPlacement.NewDocument(16, 16)!;
            Assert.ThrowsAny<Exception>(() => ProjectStore.Save(ProjectSnapshot.FromDocument(document), folder));
            Assert.Equal("user data", File.ReadAllText(Path.Combine(folder, "keep.txt")));
            Assert.False(File.Exists(Path.Combine(folder, "manifest.json")));
        }
        finally { Directory.Delete(folder, true); }
    }
}
