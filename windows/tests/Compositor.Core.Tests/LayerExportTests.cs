using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class LayerExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "compositor-layers-" + Guid.NewGuid().ToString("N"));
    private LayerExportOptions Options(LayerExportKind kind = LayerExportKind.Layers) => new(_directory, kind);
    private static ImageLayer Layer(SKColor color, string name = "Layer", int width = 8)
    {
        var bitmap = new SKBitmap(width, 8);
        bitmap.Erase(color);
        return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, name), new(0, 0, width, 8), name);
    }

    [Fact]
    public void VisibilityFilterIncludesParentVisibilityAndHiddenLayersCanBeExplicitlyExported()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var group = new ImageLayer(Guid.NewGuid(), null, new(0, 0, 8, 8), "Group") { IsGroup = true, IsVisible = false };
        var child = Layer(SKColors.Red); child.ParentID = group.ID;
        document.Layers.Add(child); document.Layers.Add(group);
        Assert.Empty(LayerExport.Items(document, Options()));
        var files = LayerExport.Run(document, Options() with { VisibleOnly = false }, [child.ID]);
        using var result = SKBitmap.Decode(Assert.Single(files));
        Assert.Equal(SKColors.Red, result.GetPixel(3, 3));
        Assert.False(group.IsVisible); Assert.True(child.IsVisible);
    }

    [Fact]
    public void GroupExportIncludesVisibleChildrenWithoutOtherSiblings()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var group = new ImageLayer(Guid.NewGuid(), null, new(0, 0, 8, 8), "Group") { IsGroup = true };
        var child = Layer(SKColors.Red); child.ParentID = group.ID;
        var hidden = Layer(SKColors.Green); hidden.ParentID = group.ID; hidden.IsVisible = false;
        document.Layers.Add(child); document.Layers.Add(hidden); document.Layers.Add(group);
        document.Layers.Add(Layer(SKColors.Blue));
        using var result = SKBitmap.Decode(Assert.Single(LayerExport.Run(document, Options(), [group.ID])));
        Assert.Equal(SKColors.Red, result.GetPixel(3, 3));
        Assert.True(document.Layers[^1].IsVisible); Assert.False(hidden.IsVisible);
    }

    [Fact]
    public void ClippedLayerRetainsBaseCoverageWithoutBaseColor()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var basis = Layer(SKColors.Red, width: 4);
        var clipped = Layer(SKColors.Blue); clipped.MaskSourceID = basis.ID;
        document.Layers.Add(basis); document.Layers.Add(clipped);
        using var result = SKBitmap.Decode(Assert.Single(LayerExport.Run(document, Options(), [clipped.ID])));
        Assert.Equal(SKColors.Blue, result.GetPixel(1, 3));
        Assert.Equal(0, result.GetPixel(6, 3).Alpha);
        Assert.True(basis.IsVisible); Assert.Equal(basis.ID, clipped.MaskSourceID);
    }

    [Fact]
    public void CompsRenderSavedPropertiesEvenForLockedLayersWithoutChangingSource()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var red = Layer(SKColors.Red, "Red"); var blue = Layer(SKColors.Blue, "Blue");
        document.Layers.Add(red); document.Layers.Add(blue);
        document.LayerComps.Add(LayerComps.Capture(document, "Blue"));
        blue.IsVisible = false;
        document.LayerComps.Add(LayerComps.Capture(document, "Red"));
        blue.Locks = LayerLocks.All;
        var files = LayerExport.Run(document, Options(LayerExportKind.LayerComps));
        using var first = SKBitmap.Decode(files[0]); using var second = SKBitmap.Decode(files[1]);
        Assert.Equal(SKColors.Blue, first.GetPixel(2, 2)); Assert.Equal(SKColors.Red, second.GetPixel(2, 2));
        Assert.False(blue.IsVisible); Assert.Equal(LayerLocks.All, blue.Locks);
    }

    [Fact]
    public void ExistingNamesArePreservedAndCancellationKeepsOnlyCompletedOutputs()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        document.Layers.Add(Layer(SKColors.Red)); document.Layers.Add(Layer(SKColors.Blue));
        Directory.CreateDirectory(_directory); var original = Path.Combine(_directory, "Layer.png");
        File.WriteAllText(original, "keep");
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => LayerExport.Run(document, Options(),
            progress: new ImmediateProgress(_ => cancellation.Cancel()), cancellationToken: cancellation.Token));
        Assert.Equal("keep", File.ReadAllText(original));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
        Assert.All(document.Layers, layer => Assert.True(layer.IsVisible));
    }

    [Fact]
    public void EmptySelectionCannotCreateOutputDirectory()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8); document.Layers.Add(Layer(SKColors.Red));
        Assert.Throws<InvalidOperationException>(() => LayerExport.Run(document, Options(), []));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void CombinedPdfKeepsSourceStateAndPublishesAllCompsTogether()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        var layer = Layer(SKColors.Blue); document.Layers.Add(layer);
        document.LayerComps.Add(LayerComps.Capture(document, "Visible"));
        layer.IsVisible = false;
        document.LayerComps.Add(LayerComps.Capture(document, "Hidden"));
        var files = LayerExport.Run(document, Options(LayerExportKind.LayerCompsPdf));
        Assert.Single(files); Assert.EndsWith(".pdf", files[0]);
        var pdf = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(files[0]));
        Assert.StartsWith("%PDF-", pdf);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(pdf, @"/Type /Page\b").Count);
        Assert.False(layer.IsVisible);
    }

    [Fact]
    public void CancelledCombinedPdfDoesNotPublishAPartialDocument()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        document.Layers.Add(Layer(SKColors.Blue));
        document.LayerComps.Add(LayerComps.Capture(document, "Visible"));
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => LayerExport.Run(document, Options(LayerExportKind.LayerCompsPdf),
            progress: new ImmediateProgress(_ => cancellation.Cancel()), cancellationToken: cancellation.Token));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    private sealed class ImmediateProgress(Action<BatchProgress> report) : IProgress<BatchProgress>
    { public void Report(BatchProgress value) => report(value); }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
