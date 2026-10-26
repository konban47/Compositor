using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// Text being typed on the canvas: the words so far, the layer they are drawn on, and what that layer was
/// before. The layer is made when the first character arrives and drawn again on every letter after it, so
/// what is on the canvas is always what will be saved; the whole session is one change to the document, and
/// letting it go puts the layer back the way it was.
/// </summary>
public sealed class TextSession
{
    private readonly LayerTextStyle? _original;
    private readonly SKPoint _origin;
    private LayerTextStyle _style;

    private TextSession(LayerTextStyle style, LayerTextStyle? original, Guid? layer, SKPoint origin)
    {
        _style = style;
        _original = original;
        _origin = origin;
        LayerID = layer;
        // The caret starts after whatever is already there, so a session that is typed into rather than
        // clicked into adds to the end — which is where it has always gone.
        AnchorIndex = CaretIndex = style.Content?.Length ?? 0;
    }

    /// <summary>The layer the words are on, once they have one: a session with no characters has no layer.</summary>
    public Guid? LayerID { get; private set; }

    /// <summary>What has been typed so far.</summary>
    public string Content => _style.Content;

    /// <summary>How many characters are in front of the caret, from 0 to the length of the words.</summary>
    public int CaretIndex { get; private set; }
    public int AnchorIndex { get; private set; }
    public int SelectionStart => Math.Min(AnchorIndex, CaretIndex);
    public int SelectionLength => Math.Abs(AnchorIndex - CaretIndex);
    public string SelectedText => Content.Substring(SelectionStart, SelectionLength);
    public void SelectAll() { AnchorIndex = 0; CaretIndex = Content.Length; }
    public void SelectRange(int start, int length)
    {
        var stops = System.Globalization.StringInfo.ParseCombiningCharacters(Content).Append(Content.Length).ToArray();
        AnchorIndex = stops.Last(n => n <= Math.Clamp(start, 0, Content.Length));
        CaretIndex = stops.First(n => n >= Math.Clamp(start + length, AnchorIndex, Content.Length));
    }
    public bool ApplyStyle(CanvasDocument document, Action<LayerTextStyle> change, bool wholeLayer = false)
    {
        var previous = _style;
        _style = SelectionLength > 0 && !wholeLayer ? TextRanges.Apply(_style, SelectionStart, SelectionLength, change) : TextEdits.Copy(_style);
        if (SelectionLength == 0 || wholeLayer) { change(_style); if (!wholeLayer) _style.StyleRuns = _style.StyleRuns?.Select(r => { var value = TextRanges.Flat(r.Style); change(value); return new LayerTextStyleRun { Location = r.Location, Length = r.Length, Style = value }; }).ToList(); }
        if (_style.IsValid && Draw(document)) { AnchorIndex = Math.Min(AnchorIndex, Content.Length); CaretIndex = Math.Min(CaretIndex, Content.Length); return true; }
        _style = previous; return false;
    }

    /// <summary>The style the words are being drawn with, as they stand.</summary>
    public LayerTextStyle Style => _style;

    /// <summary>Text that starts where it is clicked. Nothing is added to the document until a character is.</summary>
    public static TextSession New(LayerTextStyle style, SKPoint origin) =>
        new(Copy(style), null, null, origin);

    /// <summary>
    /// More words on a layer that is already text, from where the layer is. A copy of its style is taken, so
    /// that letting the session go can put the original back.
    /// </summary>
    public static TextSession Editing(ImageLayer layer)
    {
        if (layer.Text is null) throw new ArgumentException("That layer is not text.", nameof(layer));
        var style = Copy(layer.Text.Style);
        return new TextSession(style, Copy(layer.Text.Style), layer.ID,
            new SKPoint((float)layer.Transform.X, (float)layer.Transform.Y));
    }

    /// <summary>
    /// Adds what was typed — a character, or a newline — where the caret is, and draws the layer again.
    /// False when the text cannot be drawn at all, which leaves the session as it was.
    /// </summary>
    public bool Type(CanvasDocument document, string typed)
    {
        if (typed.Length == 0) return false;
        return Splice(document, SelectionStart, SelectionLength, typed);
    }

    /// <summary>
    /// The backspace key: the character before the caret goes, which for a surrogate pair is both of its
    /// halves. Nothing happens at the start of the words.
    /// </summary>
    public bool Backspace(CanvasDocument document)
    {
        if (SelectionLength > 0) return Splice(document, SelectionStart, SelectionLength, "");
        if (CaretIndex == 0) return false;
        var previous = System.Globalization.StringInfo.ParseCombiningCharacters(Content).Last(i => i < CaretIndex);
        var length = CaretIndex - previous;
        return Splice(document, CaretIndex - length, length, "");
    }

    /// <summary>The delete key: the character after the caret goes. Nothing happens at the end.</summary>
    public bool Delete(CanvasDocument document)
    {
        if (SelectionLength > 0) return Splice(document, SelectionStart, SelectionLength, "");
        if (CaretIndex >= _style.Content.Length) return false;
        var length = System.Globalization.StringInfo.GetNextTextElementLength(Content, CaretIndex);
        return Splice(document, CaretIndex, length, "");
    }

    /// <summary>
    /// Moves the caret through the words, and answers whether it went anywhere. Up and down move it a line,
    /// keeping its place along the line as far as that line reaches — counted by characters rather than by
    /// what is drawn, so on a wrapped line it can land a character or two from where the eye expects.
    /// </summary>
    public bool MoveCaret(TextMove move, bool extend = false)
    {
        var content = _style.Content;
        if (!extend && SelectionLength > 0 && move is TextMove.Left or TextMove.Right)
        { CaretIndex = move == TextMove.Left ? SelectionStart : SelectionStart + SelectionLength; AnchorIndex = CaretIndex; return true; }
        var graphemes = System.Globalization.StringInfo.ParseCombiningCharacters(content).Append(content.Length).ToArray();
        if (_style.Vertical == true) move = move switch { TextMove.Up => TextMove.Left, TextMove.Down => TextMove.Right, TextMove.Left => TextMove.Down, TextMove.Right => TextMove.Up, _ => move };
        var wanted = move switch
        {
            TextMove.Left => CaretIndex > 0 ? graphemes.Last(i => i < CaretIndex) : 0,
            TextMove.Right => CaretIndex < content.Length ? graphemes.First(i => i > CaretIndex) : content.Length,
            TextMove.Home => StartOfLine(content, CaretIndex),
            TextMove.End => EndOfLine(content, CaretIndex),
            TextMove.Up => NeighbouringLine(content, CaretIndex, -1),
            _ => NeighbouringLine(content, CaretIndex, 1),
        };
        if (wanted == CaretIndex) return false;
        CaretIndex = Math.Clamp(wanted, 0, content.Length);
        if (!extend) AnchorIndex = CaretIndex;
        return true;
    }

    /// <summary>Where a click puts the caret: at the nearest place on the line it was clicked on.</summary>
    public bool PlaceCaret(CanvasDocument document, SKPoint point, bool extend = false)
    {
        var transform = Transform(document);
        if (!transform.TryInvert(out var inverse)) return false;
        var local = inverse.MapPoint(point);
        var caret = TextEdits.CaretStops(_style).MinBy(s => _style.Vertical == true
            ? Math.Abs(s.Point.X - local.X) * 1000 + Math.Abs(s.Point.Y - local.Y)
            : Math.Abs(s.Point.Y - local.Y) * 1000 + Math.Abs(s.Point.X - local.X));
        var changed = caret.Index != CaretIndex || !extend && SelectionLength > 0;
        CaretIndex = caret.Index; if (!extend) AnchorIndex = CaretIndex;
        return changed;
    }

    public SKMatrix Transform(CanvasDocument document)
    {
        var layer = document.Layers.FirstOrDefault(l => l.ID == LayerID);
        return layer?.Asset is { } asset ? BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height)
            : SKMatrix.CreateTranslation(_origin.X, _origin.Y);
    }

    public IReadOnlyList<SKPoint[]> SelectionPolygons(CanvasDocument document)
    {
        if (SelectionLength == 0) return [];
        var stops = TextEdits.CaretStops(_style); var transform = Transform(document); var polygons = new List<SKPoint[]>();
        for (var i = 0; i + 1 < stops.Count; i++)
        {
            var a = stops[i]; var b = stops[i + 1];
            if (a.Index < SelectionStart || a.Index >= SelectionStart + SelectionLength) continue;
            var size = (float)TextRanges.At(_style, a.Index).FontSize;
            var rect = _style.Vertical == true ? SKRect.Create(a.Point.X - size / 2, a.Point.Y, size, Math.Abs(a.Point.X - b.Point.X) < 1 ? Math.Max(2, b.Point.Y - a.Point.Y) : size)
                : SKRect.Create(Math.Min(a.Point.X, b.Point.X), a.Point.Y - size * .85f, Math.Abs(a.Point.Y - b.Point.Y) < 1 ? Math.Max(2, Math.Abs(b.Point.X - a.Point.X)) : Math.Max(2, size * .35f), size * 1.1f);
            polygons.Add(transform.MapPoints([new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom)]));
        }
        return polygons;
    }

    /// <summary>Takes characters out and puts others in their place, and draws the layer again.</summary>
    private bool Splice(CanvasDocument document, int at, int remove, string insert)
    {
        var next = TextRanges.Splice(_style, at, remove, insert);
        var before = _style;
        var caret = CaretIndex; var anchor = AnchorIndex;
        _style = next;
        AnchorIndex = CaretIndex = at + insert.Length;
        if (Draw(document)) return true;
        _style = before;
        CaretIndex = caret; AnchorIndex = anchor;
        return false;
    }

    private static bool PairAt(string content, int index) =>
        index + 1 < content.Length && char.IsHighSurrogate(content[index]) && char.IsLowSurrogate(content[index + 1]);

    private static bool PairBefore(string content, int index) =>
        index >= 2 && char.IsLowSurrogate(content[index - 1]) && char.IsHighSurrogate(content[index - 2]);

    private static int StartOfLine(string content, int index)
    {
        if (content.Length == 0) return 0;
        var at = content.LastIndexOf('\n', Math.Max(0, Math.Min(index, content.Length) - 1));
        return at < 0 ? 0 : at + 1;
    }

    /// <summary>Where a line's text ends: before its newline, and before the carriage return of a CR-LF.</summary>
    private static int EndOfLine(string content, int index)
    {
        var at = content.IndexOf('\n', Math.Min(index, content.Length));
        if (at < 0) return content.Length;
        return at > 0 && content[at - 1] == '\r' ? at - 1 : at;
    }

    /// <summary>
    /// The caret's place on the line above or below: the same distance along that line, or its end when the
    /// caret was further along than that line reaches.
    /// </summary>
    private static int NeighbouringLine(string content, int index, int direction)
    {
        var line = StartOfLine(content, index);
        var column = index - line;
        if (direction < 0)
        {
            if (line == 0) return index;
            var above = StartOfLine(content, line - 1);
            return Math.Min(above + column, EndOfLine(content, above));
        }
        var breakAt = content.IndexOf('\n', Math.Min(index, content.Length));
        if (breakAt < 0) return index;
        var below = breakAt + 1;
        return Math.Min(below + column, EndOfLine(content, below));
    }

    /// <summary>
    /// Keeps the words and answers with the layer that stayed: null when there was nothing worth keeping, in
    /// which case the session lets go of what it started.
    /// </summary>
    public Guid? Commit(CanvasDocument document)
    {
        if (LayerID is { } id && (_original is not null || !string.IsNullOrWhiteSpace(_style.Content))) return id;
        Cancel(document);
        return null;
    }

    /// <summary>
    /// Lets the words go: a layer that was only being started is taken away, and one that was already there
    /// gets its old words back. False when there is nothing to put back.
    /// </summary>
    public bool Cancel(CanvasDocument document)
    {
        if (LayerID is not { } id) return false;
        if (_original is null) return LayerEdits.Delete(document, id);
        return TextEdits.SetStyle(document, id, _original);
    }

    /// <summary>The rectangle the text occupies on the document, whether it has a layer yet or not.</summary>
    public SKRectI Box(CanvasDocument document)
    {
        if (LayerID is { } id && document.Layers.FirstOrDefault(layer => layer.ID == id) is { } layer)
        {
            return SKRectI.Create((int)layer.Transform.X, (int)layer.Transform.Y,
                Math.Max(1, (int)layer.Transform.Width), Math.Max(1, (int)layer.Transform.Height));
        }
        var (width, height) = TextEdits.BoxSize(_style);
        return SKRectI.Create((int)_origin.X, (int)_origin.Y, width, height);
    }

    /// <summary>Whether a document point is inside the text, so a click there stays in the session.</summary>
    public bool Contains(CanvasDocument document, SKPoint point)
    {
        if (!Transform(document).TryInvert(out var inverse)) return false;
        var local = inverse.MapPoint(point); var size = TextEdits.BoxSize(_style);
        return local.X >= 0 && local.Y >= 0 && local.X < size.Width && local.Y < size.Height;
    }

    /// <summary>The caret, in document pixels: a thin box where it sits, as tall as the type.</summary>
    public SKRect Caret(CanvasDocument document)
    {
        var caret = TextEdits.Caret(_style, CaretIndex);
        var size = (float)TextRanges.At(_style, Math.Max(0, CaretIndex - 1)).FontSize;
        var transform = Transform(document);
        var first = transform.MapPoint(_style.Vertical == true ? new SKPoint(caret.X - size / 2, caret.Y) : new SKPoint(caret.X, caret.Y - size * .8f));
        var last = transform.MapPoint(_style.Vertical == true ? new SKPoint(caret.X + size / 2, caret.Y) : new SKPoint(caret.X, caret.Y + size * .2f));
        return new SKRect(first.X, first.Y, last.X, last.Y);
    }

    /// <summary>Draws the words: a new layer on the first character, and the same layer drawn again after.</summary>
    private bool Draw(CanvasDocument document)
    {
        if (LayerID is { } id && document.Layers.Any(layer => layer.ID == id))
        {
            return TextEdits.SetStyle(document, id, _style);
        }
        if (_style.Content.Length == 0) return true;
        LayerID = TextEdits.Add(document, _style, _origin);
        return LayerID is not null;
    }

    /// <summary>Which way an editing key sends the caret.</summary>
    public enum TextMove
    {
        Left,
        Right,
        Up,
        Down,
        Home,
        End,
    }

    /// <summary>A copy, so that editing a session does not change the layer it came from.</summary>
    private static LayerTextStyle Copy(LayerTextStyle style) => TextEdits.Copy(style);
}
