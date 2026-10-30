using System.Collections.Immutable;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Save for Web's HTML and slices (Output Settings) and its GIF animation.</summary>
public sealed class WebPageTests
{
    private static CanvasDocument Page()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 100, 60);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(100, 60));
        image.Erase(new SKColor(30, 120, 200));
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Back"), new LayerTransform(0, 0, 100, 60), "Back"));
        return document;
    }

    private static WebExportOptions Options(int width = 100, int height = 60) => new(width, height, WebFormat.Png24);

    [Fact]
    public void WithoutSlicesThePageIsOneImageNamedAfterIt()
    {
        using var document = Page();
        var slices = WebPage.Slices(document, Options(), WebHtmlSettings.Default, "My Page");
        var only = Assert.Single(slices);
        Assert.Equal("My_Page", only.FileName);
        Assert.Equal(SKRectI.Create(100, 60), only.Bounds);
    }

    [Fact]
    public void UserSlicesAndTheGapsBetweenThemMakeATableWithSpans()
    {
        using var document = Page();
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 0, Y = 0, Width = 100, Height = 20, Name = "header", Url = "https://example.com/", Target = "_blank", AltText = "Logo & name" });
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 0, Y = 20, Width = 40, Height = 40 });
        var slices = WebPage.Slices(document, Options(), WebHtmlSettings.Default, "page");
        Assert.Equal(3, slices.Count);
        Assert.Equal("header", slices[0].FileName);
        Assert.Equal(["page_02", "page_03"], slices.Skip(1).Select(s => s.FileName));
        var html = WebPage.Html(WebHtmlSettings.Default with { SpacerCells = WebSpacerCells.Never }, "page", 100, 60, slices, "png");
        Assert.Contains("<td colspan=\"2\" width=\"100\" height=\"20\">", html);
        Assert.Contains("<a href=\"https://example.com/\" target=\"_blank\">", html);
        Assert.Contains("alt=\"Logo &amp; name\"", html);
        Assert.Contains("src=\"images/page_03.png\"", html);
        Assert.Contains("<!-- Save for Web Slices (page) -->", html);
        Assert.Contains("\r\n\t<head>", html);
        Assert.DoesNotContain("spacer.gif", html);
        // A slice spanning two columns asks for spacer cells when they're left to Auto.
        Assert.Contains("spacer.gif", WebPage.Html(WebHtmlSettings.Default, "page", 100, 60, slices, "png"));
    }

    [Fact]
    public void FormattingFollowsTheSettings()
    {
        using var document = Page();
        var slices = WebPage.Slices(document, Options(), WebHtmlSettings.Default, "page");
        var settings = WebHtmlSettings.Default with
        {
            TagCase = HtmlTagCase.Upper, AttributeCase = HtmlAttributeCase.Mixed, Indent = HtmlIndent.TwoSpaces,
            LineEnding = HtmlLineEnding.Unix, IncludeComments = false, AlwaysQuote = false, CloseAllTags = true,
        };
        var html = WebPage.Html(settings, "page", 100, 60, slices, "png");
        Assert.Contains("\n  <HEAD>", html);
        Assert.DoesNotContain("\r", html);
        Assert.Contains("CellPadding=0", html);
        Assert.Contains("BgColor=\"#FFFFFF\"", html);
        Assert.DoesNotContain("<!--", html);
        Assert.Contains(" />", html);
        var lowerFirst = WebPage.Html(settings with { AttributeCase = HtmlAttributeCase.MixedLowerFirst }, "page", 100, 60, slices, "png");
        Assert.Contains("cellPadding=0", lowerFirst);
    }

    [Fact]
    public void XhtmlAndCssLayoutPlaceEachSliceInABox()
    {
        using var document = Page();
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 10, Y = 10, Width = 30, Height = 20, Name = "button", Background = 0xFFFF0000 });
        var slices = WebPage.Slices(document, Options(), WebHtmlSettings.XhtmlPreset, "page", WebSliceScope.AllUserSlices);
        var html = WebPage.Html(WebHtmlSettings.XhtmlPreset, "page", 100, 60, slices, "png");
        Assert.StartsWith("<!DOCTYPE html PUBLIC", html);
        Assert.Contains("#button_ {", html);
        Assert.Contains("left:10px; top:10px; width:30px; height:20px; background-color:#FF0000;", html);
        Assert.Contains("<div id=\"button_\">", html);
        var inline = WebPage.Html(WebHtmlSettings.XhtmlPreset with { CssReference = WebCssReference.Inline }, "page", 100, 60, slices, "png");
        Assert.DoesNotContain("<style", inline);
        Assert.Contains("style=\"position:absolute; left:10px;", inline);
    }

    [Fact]
    public void SlicesScaleWithTheOutputSize()
    {
        using var document = Page();
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 50, Y = 0, Width = 50, Height = 60 });
        var slices = WebPage.Slices(document, Options(50, 30), WebHtmlSettings.Default, "page", WebSliceScope.AllUserSlices);
        Assert.Equal(SKRectI.Create(25, 0, 25, 30), Assert.Single(slices).Bounds);
    }

    [Fact]
    public void FileNamesAreSafeOnTheChosenSystem()
    {
        Assert.Equal("a_b_c", WebPage.FileName("a:b c", WebFileNames.Windows));
        Assert.Equal("NUL_", WebPage.FileName("NUL", WebFileNames.Windows));
        Assert.Equal("Untitled", WebPage.FileName("  ", WebFileNames.Unix));
        Assert.Equal(27, WebPage.FileName(new string('x', 40), WebFileNames.MacOS).Length);
    }

    [Fact]
    public void SavingWritesThePageImagesAndSpacer()
    {
        using var document = Page();
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 0, Y = 0, Width = 100, Height = 20 });
        document.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = 0, Y = 20, Width = 40, Height = 40, NoImage = true, Text = "Hello" });
        var folder = Directory.CreateTempSubdirectory("web-page-").FullName;
        try
        {
            var page = Path.Combine(folder, "index.html");
            var result = WebPage.Save(document, Options(), WebHtmlSettings.Default, page, WebSaveKind.HtmlAndImages, "index");
            Assert.Equal(page, result.Files[0]);
            Assert.True(File.Exists(Path.Combine(folder, "images", "index_01.png")));
            Assert.False(File.Exists(Path.Combine(folder, "images", "index_02.png")), "a No Image slice has no file");
            Assert.True(File.Exists(Path.Combine(folder, "images", "spacer.gif")));
            var html = File.ReadAllText(page);
            Assert.Contains("Hello", html);
            using var decoded = SKBitmap.Decode(Path.Combine(folder, "images", "index_03.png"));
            Assert.Equal((60, 40), (decoded.Width, decoded.Height));
            var imagesOnly = Path.Combine(folder, "only", "x.html");
            WebPage.Save(document, Options(), WebHtmlSettings.Default, imagesOnly, WebSaveKind.ImagesOnly, "x");
            Assert.False(File.Exists(imagesOnly));
            Assert.True(File.Exists(Path.Combine(folder, "only", "x_01.png")));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AFrameAnimationIsOneAnimatedGif()
    {
        using var document = Page();
        var top = Bitmaps.Allocate(Bitmaps.ColorInfo(100, 60)); top.Erase(SKColors.Red);
        var red = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(top, "Red"), new LayerTransform(0, 0, 100, 60), "Red");
        document.Layers.Add(red);
        var shown = LayerComps.Capture(document, "shown");
        red.IsVisible = false;
        var hidden = LayerComps.Capture(document, "hidden");
        document.Timeline = new DocumentTimeline { Mode = TimelineMode.Frames, Frames = [new(Guid.NewGuid(), .5, shown), new(Guid.NewGuid(), .2, hidden)] };
        var options = new WebExportOptions(100, 60, WebFormat.Gif);
        Assert.Equal(2, WebExport.Frames(document).Count);
        var bytes = WebExport.EncodeAnimation(document, options, 3)!;
        using var frames = new MagickImageCollection(bytes);
        Assert.Equal(2, frames.Count);
        Assert.Equal(50u, frames[0].AnimationDelay);
        Assert.Equal(20u, frames[1].AnimationDelay);
        Assert.Equal(3u, frames[0].AnimationIterations);
        using var first = WebExport.Encode(document, options, frame: 0);
        using var second = WebExport.Encode(document, options, frame: 1);
        Assert.True(first.Preview.GetPixel(50, 30).Red > 200);
        Assert.True(second.Preview.GetPixel(50, 30).Blue > 150);
        Assert.Null(WebExport.EncodeAnimation(document, options with { Format = WebFormat.Png24 }, 0));
    }

    [Fact]
    public void OutputSettingsSaveAndLoad()
    {
        var settings = WebHtmlSettings.XhtmlPreset with { Indent = HtmlIndent.FourSpaces, ImagesFolder = "pics" };
        Assert.Equal(settings, WebHtmlSettings.Load(settings.Save()));
        Assert.False((settings with { ImagesFolder = ".." }).IsValid);
        Assert.Throws<InvalidDataException>(() => WebHtmlSettings.Load("{\"Version\":2,\"Settings\":{}}"u8.ToArray()));
    }
}
