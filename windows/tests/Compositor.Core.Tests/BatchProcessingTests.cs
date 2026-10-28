using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class BatchProcessingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "compositor-batch-" + Guid.NewGuid().ToString("N"));
    private BatchOptions Options(bool keepGoing = false) => new(_directory, BatchAction.None,
        [new(ExportFormat.Png)], [new(FileNamePart.DocumentName)], ContinueOnError: keepGoing);
    private static CanvasDocument Source()
    {
        var pixels = new SKBitmap(20, 10);
        pixels.Erase(SKColors.Red);
        return ImageImporter.NewDocument(ImportedImage.Create(pixels, "Example"));
    }

    [Fact]
    public void NamingHandlesSerialsDatesReservedNamesAndInvalidCharacters()
    {
        var name = BatchProcessing.BuildName("Photo.JPG", 7, new DateTime(2026, 10, 28),
            [new(FileNamePart.LowerName), new(FileNamePart.Literal, "-"), new(FileNamePart.Serial3),
             new(FileNamePart.Literal, "-"), new(FileNamePart.Date)]);
        Assert.Equal("photo-007-20261028", name);
        Assert.Equal("_CON", BatchProcessing.BuildName("CON.png", 1, DateTime.Today, [new(FileNamePart.DocumentName)]));
        Assert.Equal("a_b_c_", BatchProcessing.BuildName("x", 1, DateTime.Today, [new(FileNamePart.Literal, "a/b:c?")]));
        Assert.Equal("Image", BatchProcessing.BuildName("x", 1, DateTime.Today, [new(FileNamePart.Literal, "...")]));
    }

    [Fact]
    public void MultipleFormatsResizeCopiesAndPreserveExistingFilesAndSource()
    {
        using var document = Source();
        var options = Options() with { Action = BatchAction.Desaturate,
            Outputs = [new(ExportFormat.Png, Resize: true, MaximumWidth: 8, MaximumHeight: 8), new(ExportFormat.Jpeg)] };
        Directory.CreateDirectory(Path.Combine(_directory, "Png"));
        var existing = Path.Combine(_directory, "Png", "Example.png");
        File.WriteAllText(existing, "existing");
        var result = BatchProcessing.Run([new("Example.png", Document: document)], options);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Items[0].Files.Count);
        Assert.Equal("existing", File.ReadAllText(existing));
        using var png = SKBitmap.Decode(result.Items[0].Files[0]);
        Assert.Equal((8, 4), (png.Width, png.Height));
        Assert.Equal(png.GetPixel(2, 2).Red, png.GetPixel(2, 2).Green);
        Assert.Equal(SKColors.Red, document.Layers[0].Asset!.WorkingImage.GetPixel(2, 2));
        Assert.Equal(20, document.Width);
    }

    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(true, 2, 1)]
    public void FailedDecodeHonorsStopOrContinue(bool keepGoing, int count, int succeeded)
    {
        using var document = Source();
        var result = BatchProcessing.Run([new("Missing", Path.Combine(_directory, "missing.png")),
            new("Valid", Document: document)], Options(keepGoing));
        Assert.Equal(count, result.Items.Count);
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.False(string.IsNullOrWhiteSpace(result.Items[0].Error));
    }

    [Fact]
    public void CancellationKeepsCompletedOutputsAndStopsBeforeNextSource()
    {
        using var document = Source();
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(_ => cancellation.Cancel());
        Assert.Throws<OperationCanceledException>(() => BatchProcessing.Run(
            [new("First", Document: document), new("Second", Document: document)], Options(), progress, cancellation.Token));
        Assert.True(File.Exists(Path.Combine(_directory, "First.png")));
        Assert.False(File.Exists(Path.Combine(_directory, "Second.png")));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void InvalidRecipesAreRejectedBeforeWriting()
    {
        Assert.Throws<ArgumentException>(() => BatchProcessing.Validate(Options() with { Outputs = [] }));
        Assert.Throws<ArgumentException>(() => BatchProcessing.Validate(Options() with { Naming = [null!] }));
        Assert.Throws<ArgumentException>(() => BatchProcessing.Validate(Options() with { Outputs = [new(ExportFormat.Png), new(ExportFormat.Png)] }));
        Assert.Throws<ArgumentException>(() => BatchProcessing.Validate(Options() with { Outputs = [new(ExportFormat.Png, Resize: true, MaximumWidth: 0)] }));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void BatchRecipeRoundTripsSettingsAndRejectsUnsupportedOrOversizedFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "recipe.json");
        new BatchRecipe(1, Options(), true).Save(path);
        var restored = BatchRecipe.Read(path);
        Assert.True(restored.IncludeSubfolders);
        Assert.Equal(Options().OutputDirectory, restored.Options.OutputDirectory);
        Assert.Equal(Options().Outputs, restored.Options.Outputs);
        File.WriteAllText(path, "{\"Version\":2,\"Options\":null}");
        Assert.Throws<InvalidDataException>(() => BatchRecipe.Read(path));
        File.WriteAllText(path, new string(' ', 1024 * 1024 + 1));
        Assert.Throws<InvalidDataException>(() => BatchRecipe.Read(path));
    }

    private sealed class ImmediateProgress(Action<BatchProgress> report) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => report(value);
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
