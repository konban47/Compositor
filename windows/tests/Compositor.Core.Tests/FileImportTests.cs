using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class FileImportTests : ProjectTestBase
{
    [Fact]
    public void SamplingIncludesStartAndExcludesEnd()
    {
        var times = VideoFrameImport.Times(new(16, 9, 5, false), new(1, 2, 10, 3));
        Assert.Equal(4, times.Length);
        Assert.Equal(1, times[0]);
        Assert.Equal(1.9, times[^1], 6);
    }

    [Theory]
    [InlineData(0, 1, 0, 1)]
    [InlineData(1, 1, 30, 1)]
    [InlineData(0, 11, 30, 1)]
    [InlineData(0, 1, 30, 0)]
    public void InvalidSamplingFailsBeforeDecoding(double start, double end, double rate, int every) =>
        Assert.Throws<ArgumentException>(() => VideoFrameImport.Times(new(16, 9, 10, false), new(start, end, rate, every)));

    [Fact]
    public void ExcessiveFramesAreRejectedInsteadOfSilentlyTruncated() =>
        Assert.Throws<InvalidDataException>(() => VideoFrameImport.Times(new(16, 9, 10, false), new(0, 10, 30)));

    [Fact]
    public void FrameAnimationDisplaysExactlyOneImportedLayerAtEachTime()
    {
        var requestedSizes = new List<int>();
        using var doc = VideoFrameImport.Build(new(4, 2, 1, false), new(0, 1, 2, MaxSide: 1920),
            (time, size, _) => { requestedSizes.Add(size); return Picture(4, 2, time == 0 ? SKColors.Red : SKColors.Blue); });
        Assert.Equal([4, 4], requestedSizes);
        Assert.Equal(4, doc.Width);
        Assert.True(doc.Timeline!.IsValid);
        Assert.Equal(2, doc.Layers.Count);
        Assert.True(doc.Layers[0].IsVisible);
        Assert.False(doc.Layers[1].IsVisible);
        using var view = doc.Clone();
        TimelineEdits.Evaluate(view, doc.Timeline, .75);
        Assert.False(view.Layers[0].IsVisible);
        Assert.True(view.Layers[1].IsVisible);
        Assert.Equal(SKColors.Blue, view.Layers[1].Asset!.Image.GetPixel(0, 0));
        Assert.Equal(1, doc.Timeline.Duration);
    }

    [Fact]
    public void ImportCancellationDoesNotDecodeRemainingFrames()
    {
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        Assert.Throws<OperationCanceledException>(() => VideoFrameImport.Build(new(4, 2, 1, false), new(0, 1, 3),
            (_, _, _) => { count++; return Picture(4, 2, SKColors.Red); },
            _ => cancellation.Cancel(), cancellation.Token));
        Assert.Equal(1, count);
    }

    [Fact]
    public void ImportedNotesKeepContentButGetNewIdsAndExcludeOtherMarks()
    {
        var note = new DocumentMark { Kind = MarkKind.Note, X = 12, Y = 34, Text = "中文\nannotation", Author = "Author", Color = 0xff123456 };
        var path = PathIn("notes.comp-notes.json");
        File.WriteAllBytes(path, DocumentNotesFile.Encode([note, new DocumentMark { Kind = MarkKind.Count }]));
        var imported = Assert.Single(DocumentNotesFile.Read(path));
        Assert.NotEqual(note.ID, imported.ID);
        Assert.Equal(note with { ID = imported.ID }, imported);
    }

    [Theory]
    [InlineData("{\"version\":2,\"notes\":[]}")]
    [InlineData("{\"version\":1,\"notes\":[null]}")]
    public void InvalidNoteEnvelopeIsRejected(string json)
    {
        var path = PathIn("notes.json"); File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => DocumentNotesFile.Read(path));
    }
}
