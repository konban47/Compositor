using System.Text;
using System.Text.RegularExpressions;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class PdfSlideshowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "compositor-pdf-slideshow-" + Guid.NewGuid().ToString("N"));

    public PdfSlideshowTests() => Directory.CreateDirectory(_directory);
    public void Dispose() { try { Directory.Delete(_directory, true); } catch (IOException) { } }

    private static CanvasDocument Source(SKColor color)
    {
        var pixels = new SKBitmap(16, 12);
        pixels.Erase(color);
        return ImageImporter.NewDocument(ImportedImage.Create(pixels, "Slide"));
    }

    private string Write(PdfPresentationOptions options, int pages = 3)
    {
        var documents = Enumerable.Range(0, pages).Select(i => Source(i % 2 == 0 ? SKColors.Red : SKColors.Blue)).ToArray();
        try
        {
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".pdf");
            FileAutomation.PdfPresentation(path, documents.Select((d, i) => new AutomationSource($"Slide {i}.png", Document: d)).ToArray(), options);
            return path;
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }

    private static string Text(string path) => Encoding.Latin1.GetString(File.ReadAllBytes(path));

    [Fact]
    public void MultiPageDocumentsHaveNoPresentationEntries()
    {
        var text = Text(Write(new PdfPresentationOptions(SKColors.White)));
        Assert.DoesNotContain("/Dur", text);
        Assert.DoesNotContain("/FullScreen", text);
    }

    [Fact]
    public void PresentationAddsDurationTransitionAndFullScreenThatViewersStillOpen()
    {
        var path = Write(new PdfPresentationOptions(SKColors.White, Presentation: true, AdvanceSeconds: 7, Transition: PdfTransition.Dissolve));
        var text = Text(path);
        Assert.Equal(3, Regex.Matches(text, @"/Dur 7\b").Count);
        Assert.Equal(3, Regex.Matches(text, @"/S /Dissolve").Count);
        Assert.Contains("/PageMode /FullScreen", text);
        Assert.DoesNotContain("app.fs.loop", text);
        Assert.Contains("/Prev ", text);
        // PDFium follows the appended cross-reference section to the same three pages.
        Assert.Equal(3, PdfImporter.Pages(path).Count);
    }

    [Fact]
    public void LoopIsRequestedWithTheFullScreenScriptAndManualAdvanceHasNoDuration()
    {
        var text = Text(Write(new PdfPresentationOptions(SKColors.Black, Presentation: true, AutoAdvance: false, Loop: true), 2));
        Assert.DoesNotContain("/Dur", text);
        Assert.Contains("app.fs.loop = true;", text);
        Assert.DoesNotContain("app.fs.timeDelay", text);
        Assert.DoesNotContain("/Trans <<", text);
    }

    [Fact]
    public void RandomTransitionsAreValidAndRepeatable()
    {
        var path = Write(new PdfPresentationOptions(SKColors.White, Presentation: true, Transition: PdfTransition.Random), 6);
        var first = Regex.Matches(Text(path), @"/Trans (<<.*?>>)").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(6, first.Length);
        var known = Enum.GetValues<PdfTransition>().Select(PdfSlideshow.TransitionDictionary).OfType<string>().ToHashSet();
        Assert.All(first, value => Assert.Contains(value, known));
        var again = Regex.Matches(Text(Write(new PdfPresentationOptions(SKColors.White, Presentation: true, Transition: PdfTransition.Random), 6)),
            @"/Trans (<<.*?>>)").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(first, again);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(3601)]
    [InlineData(double.NaN)]
    public void InvalidAdvanceTimesAreRefusedBeforeWriting(double seconds)
    {
        var path = Path.Combine(_directory, "invalid.pdf");
        using var document = Source(SKColors.Red);
        Assert.Throws<ArgumentException>(() => FileAutomation.PdfPresentation(path, [new("Slide", Document: document)],
            new PdfPresentationOptions(SKColors.White, Presentation: true, AdvanceSeconds: seconds)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CaptionsFollowTheChosenFileInfoFields()
    {
        using var document = Source(SKColors.Red);
        document.Metadata = new DocumentMetadata { Title = "Harbor", Author = "Ann", Copyright = "© 2026", CameraMake = "Maker", CameraModel = "M1", Description = "Line\none" };
        var source = new AutomationSource("Harbor.jpg", Document: document);
        Assert.Empty(FileAutomation.PdfCaptionLines(source, PdfCaption.None));
        Assert.Equal(["Harbor"], FileAutomation.PdfCaptionLines(source, PdfCaption.FileName));
        Assert.Equal(["Harbor.jpg", "Harbor", "Line one", "Ann", "© 2026", "Maker  M1"],
            FileAutomation.PdfCaptionLines(source, (PdfCaption)127));
        // The earlier switch keeps writing the name with its extension.
        Assert.Equal(PdfCaption.FileName | PdfCaption.Extension, new PdfPresentationOptions(SKColors.White, FileNames: true).EffectiveCaptions);
    }
}
