using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class ChineseTextTests
{
    [Fact]
    public void ChineseUsesInstalledGlyphsWhenTheRequestedLatinFontLacksThem()
    {
        var preferred = TextEdits.Typeface("Arial");
        var actual = TextEdits.ForText(preferred, "中");
        using var font = new SKFont(actual);
        Assert.True(font.ContainsGlyphs("中"));
        var style = new LayerTextStyle { Content = "中文图层", FontName = "Arial", FontSize = 24 };
        using var pixels = TextEdits.Image(style)!;
        Assert.Contains(pixels.Pixels, color => color.Alpha > 0);
        Assert.Equal("Arial", style.FontName);
    }

    [Fact]
    public void ChineseParagraphWrapsWithoutSpacesAndRetainsCharacterOrder()
    {
        var style = new LayerTextStyle { Content = "中文图层蒙版选区", FontName = "Microsoft YaHei", FontSize = 24,
            BoxSize = new JsonSize(80, 240) };
        var first = TextEdits.Caret(style, 0);
        var third = TextEdits.Caret(style, 3);
        Assert.True(third.Y > first.Y);
        for (var i = 0; i < style.Content.Length; i++)
            Assert.InRange(TextEdits.Caret(style, i).X, (float)TextEdits.Padding, 80 - (float)TextEdits.Padding);
        Assert.True(TextEdits.Caret(style).Y > third.Y);
    }

    [Fact]
    public void ChineseTextAndFilenameSurviveSaveReloadUndoAndExport()
    {
        var folder = Path.Combine(Path.GetTempPath(), "compositor-zh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var document = new CanvasDocument(Guid.NewGuid(), 320, 160);
            var session = TextSession.New(new LayerTextStyle { Content = "", FontName = "Microsoft YaHei", FontSize = 24 }, new SKPoint(10, 10));
            var history = new DocumentHistory();
            history.Begin("Text", document, null);
            Assert.True(session.Type(document, "中文图层𠀀"));
            Assert.True(session.Backspace(document));
            Assert.Equal("中文图层", session.Content);
            history.End(document, session.LayerID);
            var path = Path.Combine(folder, "汉化测试.comp");
            ProjectStore.Save(ProjectSnapshot.FromDocument(document), path);
            using var restored = ProjectStore.Load(path).ToDocument();
            Assert.Equal("中文图层", restored.Layers[0].Text!.Style.Content);
            Assert.Equal("中文图层", restored.Layers[0].Name);
            using var rendered = Compositor.Core.Rendering.DocumentRenderer.Render(restored);
            using var png = rendered.Encode(SKEncodedImageFormat.Png, 100);
            Assert.True(png.Size > 100);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
