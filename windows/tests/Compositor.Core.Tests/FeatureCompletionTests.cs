using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>
/// The features filled in after 1.4.8.2: open-type and script text, super/subscript and small caps, dynamic
/// text, text-to-frame and text-to-vector, vector path nodes, the history brush and non-linear history.
/// </summary>
public class FeatureCompletionTests
{
    private static LayerTextStyle Style(string content, double size = 48) => new()
    {
        Content = content,
        FontName = "Arial",
        FontSize = size,
        Red = 0,
        Green = 0,
        Blue = 0,
    };

    private static (int Count, int Top, int Bottom, int Left, int Right) Ink(SKBitmap image)
    {
        var count = 0;
        int top = image.Height, bottom = -1, left = image.Width, right = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            if (image.GetPixel(x, y).Alpha == 0) continue;
            count++;
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            left = Math.Min(left, x); right = Math.Max(right, x);
        }
        return (count, top, bottom, left, right);
    }

    [Fact]
    public void AllCapsDrawsTheSameAsTheCapitalsItStandsFor()
    {
        using var plain = TextEdits.Image(Style("ABC"));
        var capsStyle = Style("abc");
        capsStyle.AllCaps = true;
        using var caps = TextEdits.Image(capsStyle);
        Assert.NotNull(plain);
        Assert.NotNull(caps);
        var expected = Ink(plain!);
        var actual = Ink(caps!);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Left, actual.Left);
        Assert.Equal(expected.Top, actual.Top);
    }

    [Fact]
    public void SmallCapsSetsLowercaseSmallerThanCapitals()
    {
        using var capitals = TextEdits.Image(Style("AB"));
        var smallStyle = Style("ab");
        smallStyle.SmallCaps = true;
        using var small = TextEdits.Image(smallStyle);
        Assert.NotNull(capitals);
        Assert.NotNull(small);
        Assert.True(Ink(small!).Bottom - Ink(small!).Top < Ink(capitals!).Bottom - Ink(capitals!).Top);
    }

    [Fact]
    public void SuperscriptRaisesTheLettersAndSubscriptLowersThem()
    {
        using var plain = TextEdits.Image(Style("x"));
        var upStyle = Style("x");
        upStyle.Superscript = true;
        var downStyle = Style("x");
        downStyle.Subscript = true;
        using var up = TextEdits.Image(upStyle);
        using var down = TextEdits.Image(downStyle);
        Assert.NotNull(plain);
        Assert.NotNull(up);
        Assert.NotNull(down);
        Assert.True(Ink(up!).Top < Ink(plain!).Top);
        Assert.True(Ink(down!).Bottom > Ink(plain!).Bottom);
    }

    [Fact]
    public void OpenTypeFeaturesAndKerningShapeTheRun()
    {
        var styled = Style("office");
        styled.Features = ["liga", "dlig"];
        styled.Kerning = true;
        styled.Ligatures = true;
        using var image = TextEdits.Image(styled);
        Assert.NotNull(image);
        Assert.True(Ink(image!).Count > 0);
    }

    [Fact]
    public void ArabicTextShapesAndDrawsRightToLeft()
    {
        var rightToLeft = Style("مرحبا");
        rightToLeft.Direction = TextDirection.RightToLeft;
        using var image = TextEdits.Image(rightToLeft);
        Assert.NotNull(image);
        Assert.True(Ink(image!).Count > 0);

        // Auto follows the first strong character.
        using var auto = TextEdits.Image(Style("مرحبا"));
        Assert.NotNull(auto);
        Assert.True(Ink(auto!).Count > 0);
    }

    [Fact]
    public void MixedBidirectionalTextLaysOutAndDraws()
    {
        // Latin, Arabic and European digits in one line, laid out by the full bidi algorithm and shaped.
        var mixed = Style("abc مرحبا 123 (אבג)");
        using var image = TextEdits.Image(mixed);
        Assert.NotNull(image);
        Assert.True(Ink(image!).Count > 0);
        using var outline = TextEdits.Outline(mixed);
        Assert.NotNull(outline);
        Assert.False(outline!.IsEmpty);

        var rightToLeft = Style("abc مرحبا");
        rightToLeft.Direction = TextDirection.RightToLeft;
        using var rtl = TextEdits.Image(rightToLeft);
        Assert.NotNull(rtl);
        Assert.True(Ink(rtl!).Count > 0);
    }

    [Fact]
    public void DynamicTextFillsInItsTokens()
    {
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var variables = new TextVariables("Layer 1", 800, 600, 300, 4, when);
        Assert.Equal("800x600", TextEdits.Expand("{width}x{height}", variables));
        Assert.Equal("Layer 1", TextEdits.Expand("{name}", variables));
        Assert.Equal("2024-05-06 07:08", TextEdits.Expand("{datetime}", variables));
        var dynamicStyle = Style("{width}");
        dynamicStyle.Dynamic = true;
        using var image = TextEdits.Image(dynamicStyle, variables);
        Assert.NotNull(image);
    }

    [Fact]
    public void TextCanBeTurnedIntoAFrameAndIntoAVectorShape()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 200);
        var id = TextEdits.Add(document, Style("Wg"), new SKPoint(20, 20));
        Assert.NotNull(id);
        var layer = document.Layers.Single();

        Assert.True(TextEdits.MakeFrame(document, id!.Value));
        Assert.NotNull(layer.Text!.Style.BoxSize);

        Assert.True(TextEdits.ToVectorShape(document, id.Value));
        Assert.Null(layer.Text);
        Assert.NotNull(layer.LiveShape);
        Assert.Equal(ShapeKind.Path, layer.LiveShape!.Kind);
        Assert.Equal(14, ProjectSnapshot.FromDocument(document).Manifest.Version);
    }

    [Fact]
    public void PlainTextStillSavesAtVersionEleven()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 200);
        Assert.NotNull(TextEdits.Add(document, Style("Hello"), new SKPoint(20, 20)));
        Assert.Equal(11, ProjectSnapshot.FromDocument(document).Manifest.Version);
    }

    [Fact]
    public void OutlineFollowsTheDrawnLetters()
    {
        var style = Style("Hi");
        using var image = TextEdits.Image(style);
        using var outline = TextEdits.Outline(style);
        Assert.NotNull(image);
        Assert.NotNull(outline);
        Assert.False(outline!.IsEmpty);
        // The outline lives inside the drawn box.
        Assert.True(outline.Bounds.Left >= 0 && outline.Bounds.Right <= image!.Width + 1);
    }

    [Fact]
    public void VectorPathNodesParseEditAndRoundTrip()
    {
        var nodes = PathNodes.Parse("M10 10L50 10L50 40Z");
        Assert.NotNull(nodes);
        Assert.Single(nodes!.Subpaths);
        Assert.Equal(3, nodes.Subpaths[0].Nodes.Count);

        Assert.True(nodes.MoveNode(0, 0, new SKPoint(5, -5)));
        Assert.Equal(15, nodes.Subpaths[0].Nodes[0].Point.X);
        Assert.Equal(5, nodes.Subpaths[0].Nodes[0].Point.Y);

        var again = PathNodes.Parse(nodes.ToSvg());
        Assert.NotNull(again);
        Assert.Equal(3, again!.Subpaths[0].Nodes.Count);
        Assert.Equal(15, again.Subpaths[0].Nodes[0].Point.X);

        Assert.True(nodes.InsertAfter(0, 0));
        Assert.Equal(4, nodes.Subpaths[0].Nodes.Count);
        Assert.True(nodes.RemoveAt(0, 1));
        Assert.Equal(3, nodes.Subpaths[0].Nodes.Count);
    }

    [Fact]
    public void CubicNodesKeepTheirHandles()
    {
        var nodes = PathNodes.Parse("M0 0C10 0 10 10 20 10");
        Assert.NotNull(nodes);
        Assert.Equal(2, nodes!.Subpaths[0].Nodes.Count);
        Assert.NotNull(nodes.Subpaths[0].Nodes[0].Out);
        Assert.NotNull(nodes.Subpaths[0].Nodes[1].In);
        using var image = SKPath.ParseSvgPathData(nodes.ToSvg());
        Assert.NotNull(image);
    }

    [Fact]
    public void NonLinearHistoryKeepsTheStatesAhead()
    {
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(4, 4)), "L"),
            new Model.LayerTransform(0, 0, 4, 4), "L");
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8);
        document.Layers.Add(layer);
        var history = new DocumentHistory();

        history.Begin("Move", document, layer.ID);
        layer.Transform = layer.Transform with { X = 1 };
        history.End(document, layer.ID);
        Assert.True(history.CanUndo);
        history.Undo();
        Assert.True(history.CanRedo);

        history.AllowNonLinear = true;
        history.Begin("Move again", document, layer.ID);
        layer.Transform = layer.Transform with { Y = 2 };
        history.End(document, layer.ID);
        Assert.True(history.CanRedo);

        // Linear history, by contrast, throws the states ahead away.
        history.Undo();
        history.AllowNonLinear = false;
        history.Begin("Linear", document, layer.ID);
        layer.Transform = layer.Transform with { Y = 3 };
        history.End(document, layer.ID);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void TheHistoryBrushPaintsBackAChosenState()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 20, 20);
        var blank = new SKBitmap(Bitmaps.ColorInfo(20, 20));
        blank.Erase(SKColors.Transparent);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(blank, "L"),
            new Model.LayerTransform(0, 0, 20, 20), "L");
        document.Layers.Add(layer);

        // What an earlier state looked like: a solid red square, premultiplied as a render is.
        using var source = new SKBitmap(Bitmaps.ColorInfo(20, 20));
        source.Erase(new SKColor(255, 0, 0, 255));

        var settings = new BrushSettings(Diameter: 30, Hardness: 1, Mode: BrushMode.History, History: source);
        Assert.True(BrushEdits.Paint(document, layer.ID, [new SKPoint(10, 10)], settings));

        var pixel = layer.Asset!.Image.GetPixel(10, 10);
        Assert.True(pixel.Alpha > 0);
        Assert.True(pixel.Red > 200);
    }
}
