using System.IO.Compression;
using System.Text;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Notes to and from Acrobat's comment formats, and written into PDF presentations.</summary>
public sealed class PdfNotesTests
{
    private static DocumentMark[] Sample() =>
    [
        new DocumentMark { Kind = MarkKind.Note, X = 144, Y = 72, Text = "检查高光 (highlights) \\ done", Author = "张", Color = 0xFF3366CC },
        new DocumentMark { Kind = MarkKind.Note, X = 10, Y = 200, Text = "Second", Author = "Reviewer", Color = 0xFFFFD54F },
    ];

    private static void Same(DocumentMark[] expected, DocumentMark[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Text, actual[i].Text);
            Assert.Equal(expected[i].Author, actual[i].Author);
            Assert.Equal(expected[i].Color, actual[i].Color);
            Assert.InRange(actual[i].X, expected[i].X - .01, expected[i].X + .01);
            Assert.InRange(actual[i].Y, expected[i].Y - .01, expected[i].Y + .01);
        }
    }

    [Fact]
    public void FdfAndXfdfRoundTrip()
    {
        var notes = Sample();
        foreach (var bytes in new[] { PdfNotes.WriteFdf(notes, 300, 144), PdfNotes.WriteXfdf(notes, 300, 144) })
        {
            var (read, pages) = PdfNotes.Read(bytes);
            Assert.Empty(pages);
            Same(notes, PdfNotes.ToMarks(read, pages, 400, 300, 144));
        }
        Assert.StartsWith("%FDF-1.2", Encoding.Latin1.GetString(PdfNotes.WriteFdf(notes, 300, 72)));
        Assert.Throws<InvalidDataException>(() => PdfNotes.WriteFdf([], 300, 72));
        Assert.Throws<InvalidDataException>(() => PdfNotes.Read("hello"u8.ToArray()));
    }

    private static byte[] Pdf(params (float Width, float Height)[] pages)
    {
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream))
        {
            foreach (var (width, height) in pages) { var canvas = document.BeginPage(width, height); canvas.Clear(SKColors.White); document.EndPage(); }
            document.Close();
        }
        return stream.ToArray();
    }

    [Fact]
    public void NotesWrittenIntoAPdfReadBackWhereTheyWere()
    {
        var notes = Sample();
        // A 400×300 document shown on a 200×150-point page under a 30-point caption.
        var pdf = PdfNotes.Annotate(Pdf((200, 180), (100, 100)), [(notes, 400, 300, 200, 180), ([], 1, 1, 100, 100)]);
        var (read, pages) = PdfNotes.Read(pdf);
        Assert.Equal(2, pages.Count);
        Assert.All(read, note => Assert.Equal(0, note.Page));
        // Read against that page, the positions come back in the document's pixels.
        var back = read.Select(n => new DocumentMark { Kind = MarkKind.Note, X = n.Left * 2, Y = (180 - n.Top) * 2, Text = n.Text, Author = n.Author, Color = n.Color }).ToArray();
        Same(notes, back);
        var written = Path.Combine(Path.GetTempPath(), $"annotated-{Guid.NewGuid():N}.pdf");
        try { File.WriteAllBytes(written, pdf); Assert.Equal(2, PdfImporter.Pages(written).Count); }
        finally { File.Delete(written); }
    }

    [Fact]
    public void NotesInsideACompressedObjectStreamAreFound()
    {
        // A PDF 1.5 file whose annotation lives in a FlateDecode object stream.
        var annot = "<< /Type /Annot /Subtype /Text /Rect [50 60 70 80] /Contents (Packed note) /T (Ann) /C [1 0 0] >>";
        var header = "5 0 ";
        var data = Encoding.Latin1.GetBytes(header + annot);
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, true)) z.Write(data);
        var text = new StringBuilder("%PDF-1.5\n");
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        text.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 200] >>\nendobj\n");
        text.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>\nendobj\n");
        text.Append($"4 0 obj\n<< /Type /ObjStm /N 1 /First {header.Length} /Filter /FlateDecode /Length {packed.Length} >>\nstream\n");
        var bytes = Encoding.Latin1.GetBytes(text.ToString()).Concat(packed.ToArray()).Concat(Encoding.Latin1.GetBytes("\nendstream\nendobj\ntrailer\n<< /Root 1 0 R /Size 6 >>\n%%EOF\n")).ToArray();
        var (read, pages) = PdfNotes.Read(bytes);
        var note = Assert.Single(read);
        Assert.Equal(("Packed note", "Ann", 0xFFFF0000u), (note.Text, note.Author, note.Color));
        var mark = Assert.Single(PdfNotes.ToMarks(read, pages, 1000, 2000, 72));
        Assert.Equal((500, 1200), ((int)mark.X, (int)mark.Y));
    }

    [Fact]
    public void APresentationCarriesItsSourcesNotesAsAnnotations()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 120, 80);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(120, 80)); image.Erase(SKColors.SteelBlue);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Page"), new LayerTransform(0, 0, 120, 80), "Page"));
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Note, X = 30, Y = 20, Text = "On the slide", Author = "Me" });
        var path = Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.pdf");
        try
        {
            FileAutomation.PdfPresentation(path, [new AutomationSource("Page", null, document)],
                new PdfPresentationOptions(SKColors.White, Presentation: true, Annotations: true));
            var bytes = File.ReadAllBytes(path);
            var (read, _) = PdfNotes.Read(bytes);
            var note = Assert.Single(read);
            Assert.Equal("On the slide", note.Text);
            Assert.Contains("/PageMode /FullScreen", Encoding.Latin1.GetString(bytes));
            Assert.Single(PdfImporter.Pages(path));
            FileAutomation.PdfPresentation(path, [new AutomationSource("Page", null, document)], new PdfPresentationOptions(SKColors.White));
            Assert.Empty(PdfNotes.Read(File.ReadAllBytes(path)).Notes);
        }
        finally { File.Delete(path); }
    }
}
