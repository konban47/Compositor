using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class TextRangeTests
{
    [Fact]
    public void SelectionFormattingSurvivesEditingSavingAndLoading()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 600, 300);
        var id = TextEdits.Add(doc, new() { Content = "甲乙 ABC", FontName = "Arial", FontSize = 24 }, new(30, 40))!.Value;
        var session = TextSession.Editing(doc.Layers.Single()); session.SelectRange(1, 3);
        Assert.True(session.ApplyStyle(doc, s => { s.Bold = true; s.FontSize = 40; s.Red = 1; }));
        Assert.Null(TextRanges.At(session.Style, 0).Bold); Assert.True(TextRanges.At(session.Style, 2).Bold);
        Assert.Equal(40, TextRanges.At(session.Style, 3).FontSize); Assert.Equal(24, TextRanges.At(session.Style, 4).FontSize);
        Assert.True(session.Type(doc, "丙")); Assert.Equal("甲丙BC", session.Content); Assert.True(session.Style.IsValid);
        Assert.Equal(24, TextRanges.At(session.Style, 2).FontSize);
        session.SelectRange(1, 1); Assert.True(session.ApplyStyle(doc, s => s.Green = .75)); session.Commit(doc);
        var root = Path.Combine(Path.GetTempPath(), "CompositorTextRanges-" + Guid.NewGuid());
        try
        {
            using var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(20, snapshot.Manifest.Version);
            ProjectStore.Save(snapshot, root); using var loaded = ProjectStore.Load(root); using var result = loaded.ToDocument();
            Assert.Equal(.75, TextRanges.At(result.Layers.Single().LiveText!, 1).Green);
            Assert.Equal("甲丙BC", result.Layers.Single().LiveText!.Content);
            loaded.Manifest.Version = 19; Assert.Throws<ProjectException>(() => ProjectStore.Validate(loaded.Manifest));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Assert.Equal(id, doc.Layers.Single().ID);
    }

    [Fact]
    public void GraphemeSelectionAndDeletionKeepCombiningMarksAndEmojiTogether()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 400, 200);
        var id = TextEdits.Add(doc, new() { Content = "A👨‍👩‍👧e\u0301中", FontSize = 24 }, default);
        var session = TextSession.Editing(doc.Layers.Single());
        session.SelectRange(3, 1); Assert.Equal("👨‍👩‍👧", session.SelectedText);
        Assert.True(session.Delete(doc)); Assert.Equal("Ae\u0301中", session.Content);
        session.MoveCaret(TextSession.TextMove.Right); Assert.Equal(3, session.CaretIndex);
        session.Backspace(doc); Assert.Equal("A中", session.Content);
        Assert.NotNull(id);
    }

    [Fact]
    public void ShapedTextHasInteriorCaretStopsWithoutSplittingTheDrawnRun()
    {
        var style = new LayerTextStyle { Content = "office العربية", FontName = "Arial", FontSize = 30, Ligatures = true };
        var stops = TextEdits.CaretStops(style);
        Assert.Contains(stops, s => s.Index == 2); Assert.Contains(stops, s => s.Index == 3);
        Assert.True(stops.Single(s => s.Index == 2).Point.X < stops.Single(s => s.Index == 3).Point.X);
        Assert.Contains(stops, s => s.Index == style.Content.Length);
        using var pixels = TextEdits.Image(style); Assert.NotNull(pixels);
    }

    [Fact]
    public void RotatedTextHitTestingUsesTheLayerTransform()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 400, 400);
        TextEdits.Add(doc, new() { Content = "ABCD", FontName = "Arial", FontSize = 26 }, new(80, 90));
        var layer = doc.Layers.Single(); layer.Transform = layer.Transform with { Rotation = 90, FlipX = true };
        var session = TextSession.Editing(layer); var at = TextEdits.Caret(session.Style, 2);
        var point = session.Transform(doc).MapPoint(at);
        Assert.True(session.PlaceCaret(doc, point)); Assert.Equal(2, session.CaretIndex);
        session.SelectRange(1, 2); Assert.NotEmpty(session.SelectionPolygons(doc));
    }

    [Fact]
    public void RichTextValidationRejectsNestedOrOverlappingRuns()
    {
        var style = new LayerTextStyle { Content = "AB" };
        style.StyleRuns = [new() { Location = 0, Length = 2, Style = TextRanges.Flat(style) }, new() { Location = 1, Length = 1, Style = TextRanges.Flat(style) }];
        Assert.False(style.IsValid);
        style.StyleRuns = [new() { Location = 0, Length = 1, Style = style }]; Assert.False(style.IsValid);
    }

    [Fact]
    public void ImportedPostScriptNameFindsTheInstalledTypeface()
    {
        using var face = SKTypeface.FromFamilyName("Arial");
        Assert.True(FontCatalog.IsInstalled(face.PostScriptName));
        Assert.Equal(face.FamilyName, FontCatalog.Family(face.PostScriptName));
    }
}
