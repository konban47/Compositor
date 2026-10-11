using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// Laying out and drawing a text layer's pixels: the style is the truth and the PNG is what it looks like,
/// so an edit re-rasterises from the style rather than painting over the last picture.
/// <para>
/// Latin, Greek and Cyrillic are placed one character at a time, which is what gives every letter its own
/// face and colour and lets the tracking be added exactly. When the style asks for open-type features,
/// kerning, ligatures or a reading direction, or the words are written in a script that needs shaping —
/// Arabic, Hebrew, the Indic scripts — the run is shaped through HarfBuzz instead, so joined forms,
/// ligatures, kerning and right-to-left order come out as the font intends.
/// </para>
/// </summary>
public static partial class TextEdits
{
    /// <summary>The room left around the text inside its layer, as the Mac build leaves it.</summary>
    public const double Padding = 12;

    /// <summary>The smallest a text layer may be, so an empty line still has somewhere to put a caret.</summary>
    public const int LeastSide = 16;

    private static readonly ConcurrentDictionary<string, SKTypeface> Faces = new();

    /// <summary>A text layer's name: its first words on one line, so a paragraph does not make the row tall.</summary>
    public static string LayerName(string content)
    {
        var flattened = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flattened.Length == 0 ? "Text" : flattened[..Math.Min(40, flattened.Length)];
    }

    /// <summary>How big a text layer's pixels are: the paragraph's own box, or what point text measures.</summary>
    public static (int Width, int Height) BoxSize(LayerTextStyle style, TextVariables? variables = null)
    {
        if (style.BoxSize is { } box)
        {
            return (Math.Max(LeastSide, (int)Math.Ceiling(box.Width)), Math.Max(LeastSide, (int)Math.Ceiling(box.Height)));
        }
        Layout(style, variables, out var measuredWidth, out var measuredHeight);
        var width = Math.Max(LeastSide, (int)Math.Ceiling(measuredWidth + Padding * 2 + style.FontSize * 0.1));
        var height = Math.Max(LeastSide, (int)Math.Ceiling(Math.Max(measuredHeight, Math.Ceiling(style.LineHeight)) + Padding * 2));
        return (width, height);
    }

    /// <summary>The text drawn: straight alpha sRGB, the format every layer's pixels are held in.</summary>
    public static SKBitmap? Image(LayerTextStyle style, TextVariables? variables = null)
    {
        if (!style.IsValid) return null;
        var (width, height) = BoxSize(style, variables);
        if (width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide
            || (long)width * height > DocumentLimits.MaxSurfacePixels)
        {
            return null;
        }
        var pieces = Layout(style, variables, out _, out _);
        var image = new SKBitmap(Bitmaps.ColorInfo(width, height));
        image.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(image);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var piece in pieces)
        {
            paint.Color = piece.Colour;
            using var font = new SKFont(piece.Typeface, (float)(style.FontSize * piece.Scale))
            {
                Embolden = style.Bold == true,
                SkewX = style.Italic == true ? -.25f : 0,
            };
            if (piece.Shaped)
            {
                using var builder = new SKTextBlobBuilder();
                var run = builder.AllocatePositionedRun(font, piece.Glyphs!.Length);
                for (var index = 0; index < piece.Glyphs.Length; index++)
                {
                    run.Glyphs[index] = piece.Glyphs[index];
                    run.Positions[index] = piece.Positions![index];
                }
                using var blob = builder.Build();
                canvas.DrawText(blob, piece.X, piece.Y, paint);
            }
            else
            {
                canvas.DrawText(piece.Text, piece.X, piece.Y, SKTextAlign.Left, font, paint);
            }
            paint.StrokeWidth = Math.Max(1, (float)style.FontSize / 16);
            if (style.Underline == true) canvas.DrawLine(piece.X, piece.Y + (float)style.FontSize * .1f, piece.X + piece.Width, piece.Y + (float)style.FontSize * .1f, paint);
            if (style.Strikethrough == true) canvas.DrawLine(piece.X, piece.Y - (float)style.FontSize * .3f, piece.X + piece.Width, piece.Y - (float)style.FontSize * .3f, paint);
        }
        return image;
    }

    /// <summary>
    /// The text as one outline, in layer pixels: the same letters the image draws, as closed paths. This is
    /// what turns a text layer into a vector glyph layer, and what a text layer's pixels can be traced to.
    /// </summary>
    public static SKPath? Outline(LayerTextStyle style, TextVariables? variables = null)
    {
        if (!style.IsValid) return null;
        var pieces = Layout(style, variables, out _, out _);
        using var builder = new SKPathBuilder();
        foreach (var piece in pieces)
        {
            using var font = new SKFont(piece.Typeface, (float)(style.FontSize * piece.Scale))
            {
                Embolden = style.Bold == true,
                SkewX = style.Italic == true ? -.25f : 0,
            };
            if (piece.Shaped)
            {
                for (var index = 0; index < piece.Glyphs!.Length; index++)
                {
                    using var glyph = font.GetGlyphPath(piece.Glyphs[index]);
                    if (glyph is null) continue;
                    glyph.Transform(SKMatrix.CreateTranslation(piece.X + piece.Positions![index].X, piece.Y + piece.Positions[index].Y));
                    builder.AddPath(glyph);
                }
            }
            else
            {
                using var text = font.GetTextPath(piece.Text, new SKPoint(piece.X, piece.Y));
                if (text is not null) builder.AddPath(text);
            }
        }
        return builder.Detach();
    }

    /// <summary>
    /// A new text layer at <paramref name="origin"/>, holding the text and the style that drew it. The
    /// pixels and the style share one bitmap, which is what makes the layer still a live text layer.
    /// </summary>
    public static Guid? Add(CanvasDocument document, LayerTextStyle style, SKPoint origin)
    {
        if (document.Layers.Count >= LayerPlacement.MaxLayers) return null;
        if (Image(style, Variables(document, LayerName(style.Content))) is not { } image) return null;
        var name = LayerName(style.Content);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, name),
            new Model.LayerTransform(origin.X, origin.Y, image.Width, image.Height), name)
        {
            ParentID = null,
            Text = new LayerText(style, image),
        };
        document.Layers.Add(layer);
        return layer.ID;
    }

    /// <summary>Changes a text layer's style and draws it again, which is how editing live text works.</summary>
    public static bool SetStyle(CanvasDocument document, Guid layerID, LayerTextStyle style)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { } layer) return false;
        if (Image(style, Variables(document, LayerName(style.Content))) is not { } image) return false;
        // The box may have changed with the text, so the layer is re-placed about the middle it had.
        var centreX = layer.Transform.CenterX;
        var centreY = layer.Transform.CenterY;
        var keepsItsPlace = layer.Text is not null;
        var name = LayerName(style.Content);
        layer.Asset = ImportedImage.Create(image, name);
        layer.Text = new LayerText(style, image);
        layer.Name = name;
        layer.Transform = keepsItsPlace
            ? layer.Transform with
            {
                Width = image.Width,
                Height = image.Height,
                X = centreX - image.Width / 2.0,
                Y = centreY - image.Height / 2.0,
            }
            : new Model.LayerTransform(layer.Transform.X, layer.Transform.Y, image.Width, image.Height);
        return true;
    }

    /// <summary>
    /// Turns point text into a paragraph frame as big as the text is now, so it can then be reflowed inside
    /// that box. False when the layer is not live text or already has a frame.
    /// </summary>
    public static bool MakeFrame(CanvasDocument document, Guid layerID)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { Asset: { } asset, Text: { } text }) return false;
        if (text.Style.BoxSize is not null) return false;
        var framed = Copy(text.Style);
        framed.BoxSize = new JsonSize { Width = asset.Width, Height = asset.Height };
        return SetStyle(document, layerID, framed);
    }

    /// <summary>
    /// Turns a live text layer into a vector glyph layer: the letters become one outline shape that redraws
    /// at any size instead of a raster. False when the layer is not live text or the outline is empty.
    /// </summary>
    public static bool ToVectorShape(CanvasDocument document, Guid layerID)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { Asset: { } asset, Text: { } text } layer) return false;
        if (Outline(text.Style, Variables(document, layer.Name)) is not { } outline) return false;
        if (outline.IsEmpty || outline.Bounds.Width <= 0 || outline.Bounds.Height <= 0) return false;
        var bounds = outline.Bounds;
        using var normalized = new SKPath(outline);
        normalized.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
        normalized.Transform(SKMatrix.CreateScale(1 / bounds.Width, 1 / bounds.Height));
        var shape = new LayerShapeStyle
        {
            Kind = ShapeKind.Path,
            Path = normalized.ToSvgPathData(),
            Red = text.Style.Red, Green = text.Style.Green, Blue = text.Style.Blue,
        };
        if (ShapeEdits.Image(shape, asset.Width, asset.Height) is not { } image) return false;
        layer.Asset = ImportedImage.Create(image, layer.Name);
        layer.Shape = new LayerShape(shape, image);
        layer.Text = null;
        return true;
    }

    /// <summary>What the dynamic text tokens of a layer are filled in with.</summary>
    public static TextVariables Variables(CanvasDocument document, string name) =>
        new(name, document.Width, document.Height, document.Resolution, document.Layers.Count, DateTime.Now);

    /// <summary>Fills the <c>{name}</c>, <c>{width}</c>, <c>{date}</c> and other tokens of dynamic text.</summary>
    public static string Expand(string content, TextVariables variables)
    {
        if (content.IndexOf('{') < 0) return content;
        var builder = new StringBuilder(content.Length);
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character != '{') { builder.Append(character); continue; }
            var close = content.IndexOf('}', index + 1);
            if (close < 0) { builder.Append(character); continue; }
            var token = content[(index + 1)..close].Trim().ToLowerInvariant();
            builder.Append(token switch
            {
                "name" => variables.Name ?? "",
                "width" => variables.Width.ToString(CultureInfo.InvariantCulture),
                "height" => variables.Height.ToString(CultureInfo.InvariantCulture),
                "resolution" => variables.Resolution.ToString("0.##", CultureInfo.InvariantCulture),
                "layers" => variables.Layers.ToString(CultureInfo.InvariantCulture),
                "date" => variables.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "time" => variables.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                "datetime" => variables.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                _ => content[(index + 1)..(close + 1)],
            });
            index = close;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Where the caret sits when the text ends: after the last character of the last line, and on the first
    /// line's baseline when there is no text yet. Relative to the layer's own top left, in layer pixels.
    /// </summary>
    public static SKPoint Caret(LayerTextStyle style) => Caret(style, (style.Content ?? "").Length);

    /// <summary>
    /// Where the caret sits with <paramref name="index"/> characters in front of it: at the place the text
    /// before it ends, on the line that falls on. An index past the end is the end, and one on a character
    /// that a line break left out — the space a wrapped line breaks at — sits at the end of the line before.
    /// </summary>
    public static SKPoint Caret(LayerTextStyle style, int index)
    {
        var stops = new List<Stop>();
        Layout(style, null, out _, out _, stops);
        if (stops.Count == 0) return new SKPoint((float)Padding, (float)(Padding + Baseline(style)));
        var at = Math.Clamp(index, 0, (style.Content ?? "").Length);
        foreach (var stop in stops)
        {
            if (stop.Index >= at) return new SKPoint(stop.X, stop.Y);
        }
        var last = stops[^1];
        return new SKPoint(last.X, last.Y);
    }

    /// <summary>A place the caret can sit: how far into the words, and where that is drawn.</summary>
    private readonly record struct Stop(int Index, float X, float Y);

    /// <summary>How far below the top of a line its writing sits.</summary>
    private static double Baseline(LayerTextStyle style)
    {
        var metrics = Metrics(style.FontName, style.FontSize);
        var ascent = -metrics.Ascent;
        var descent = metrics.Descent;
        return (style.LineHeight - (ascent + descent)) / 2 + ascent;
    }

    /// <summary>What a text layer needs to be drawn: its face, its size and the leading between lines.</summary>
    public static SKFontMetrics Metrics(string fontName, double size) =>
        new SKFont(Typeface(fontName), (float)size).Metrics;

    /// <summary>The face to set text in, falling back to the system face as the Mac build falls back.</summary>
    public static SKTypeface Typeface(string? fontName) =>
        Faces.GetOrAdd(fontName ?? "", name => name.Length > 0
            ? SKTypeface.FromFamilyName(name) ?? SKTypeface.Default
            : SKTypeface.Default);

    /// <summary>One run to draw, with the face and colour it is set in, where it goes and how big it is.</summary>
    private readonly record struct Piece(string Text, SKTypeface Typeface, SKColor Colour, float X, float Y,
        float Scale, float Shift, float Width, bool Shaped, ushort[]? Glyphs, SKPoint[]? Positions);

    /// <summary>
    /// The text laid out: every run with its place, and how big the whole paragraph came out. Lines break
    /// where the content says so, and, in a paragraph box, wherever the next word would not fit.
    /// </summary>
    private static List<Piece> Layout(LayerTextStyle style, TextVariables? variables,
        out double measuredWidth, out double measuredHeight, List<Stop>? stops = null)
    {
        if (style.Vertical == true) return VerticalLayout(style, variables, out measuredWidth, out measuredHeight, stops);
        var pieces = new List<Piece>();
        var content = Content(style, variables);
        var colours = Colours(style, content.Length);
        var fonts = Fonts(style, content.Length);
        var lineHeight = style.LineHeight;
        // Where the lines have to fit: the paragraph box less its padding, or no limit at all for point text.
        var limit = style.BoxSize is { } box ? Math.Max(1, box.Width - Padding * 2) : double.PositiveInfinity;
        var baseline = Baseline(style);

        measuredWidth = 0;
        var line = 0;
        foreach (var (start, length) in Lines(content))
        {
            foreach (var (from, to) in Wrap(content, start, length, style, fonts, colours, limit))
            {
                var y = (float)(Padding + line * lineHeight + baseline);
                var linePieces = new List<Piece>();
                var lineStops = new List<Stop>();
                if (Shapes(style, content, from, to) || NeedsReorder(content, from, to))
                    BuildBidi(content, from, to, style, fonts, colours, y, linePieces, lineStops);
                else
                    BuildPlain(content, from, to, style, fonts, colours, y, linePieces, lineStops);

                var width = linePieces.Count == 0 ? 0 : linePieces[^1].X + linePieces[^1].Width;
                var offset = AlignmentOffset(style.Alignment, limit, width);
                var shift = (float)(Padding + offset);
                for (var index = 0; index < linePieces.Count; index++) linePieces[index] = linePieces[index] with { X = linePieces[index].X + shift };
                for (var index = 0; index < lineStops.Count; index++) lineStops[index] = lineStops[index] with { X = lineStops[index].X + shift };
                pieces.AddRange(linePieces);
                stops?.AddRange(lineStops);
                measuredWidth = Math.Max(measuredWidth, width);
                line++;
            }
        }
        measuredHeight = line * lineHeight;
        return pieces;
    }

    /// <summary>The content drawn: the stored words, or their dynamic tokens filled in.</summary>
    private static string Content(LayerTextStyle style, TextVariables? variables) =>
        style.Dynamic == true ? Expand(style.Content ?? "", variables ?? TextVariables.Empty) : style.Content ?? "";

    /// <summary>Whether a line has to be shaped rather than placed a character at a time.</summary>
    private static bool Shapes(LayerTextStyle style, string content, int from, int to)
    {
        if (style.Features is { Count: > 0 } || style.Ligatures is not null || style.Kerning is not null
            || style.Direction != TextDirection.Auto) return true;
        if (style.ComplexShaping == false) return false;
        for (var index = from; index < to; index++)
        {
            if (TextShaping.Complex(content[index])) return true;
        }
        return false;
    }

    /// <summary>Whether a line has anything the bidirectional algorithm has to order.</summary>
    private static bool NeedsReorder(string content, int from, int to)
    {
        var index = from;
        while (index < to)
        {
            var codepoint = char.ConvertToUtf32(content, index);
            if (Bidi.Classify(codepoint) is not (Bidi.Kind.L or Bidi.Kind.EN or Bidi.Kind.ES
                or Bidi.Kind.ET or Bidi.Kind.ON or Bidi.Kind.WS or Bidi.Kind.NSM or Bidi.Kind.CS
                or Bidi.Kind.AN or Bidi.Kind.B or Bidi.Kind.S))
            {
                return true;
            }
            index += char.IsHighSurrogate(content[index]) ? 2 : 1;
        }
        return false;
    }

    /// <summary>Places one character at a time, which is what gives each letter its own face and colour.</summary>
    private static void BuildPlain(string content, int from, int to, LayerTextStyle style,
        List<SKTypeface> fonts, List<SKColor> colours, float y, List<Piece> pieces, List<Stop> stops)
    {
        var x = 0.0;
        var index = from;
        while (index < to)
        {
            var text = Display(style, content, index, to, out var taken, out var scale, out var shift);
            var typeface = ForText(fonts[index], text);
            var width = (float)Advance(text, typeface, style, scale);
            pieces.Add(new Piece(text, typeface, colours[index], (float)x, y + shift, scale, shift, width, false, null, null));
            stops.Add(new Stop(index, (float)x, y));
            x += width;
            index += taken;
        }
        stops.Add(new Stop(to, (float)x, y));
    }

    /// <summary>One shaped run after reordering: the level it sits at, and where it lies in the words.</summary>
    private readonly record struct ShapedRun(string Text, SKTypeface Typeface, SKColor Colour, float Scale,
        float Shift, byte Level, TextShaping.Run Shaped, int Start, int End);

    /// <summary>
    /// Lays a line out the way the Unicode Bidirectional Algorithm says: the levels decide which runs read
    /// right to left, each run is shaped by its own direction, and the runs are put in visual order.
    /// </summary>
    private static void BuildBidi(string content, int from, int to, LayerTextStyle style,
        List<SKTypeface> fonts, List<SKColor> colours, float y, List<Piece> pieces, List<Stop> stops)
    {
        var codepoints = new List<int>();
        var starts = new List<int>();
        var index = from;
        while (index < to)
        {
            codepoints.Add(char.ConvertToUtf32(content, index));
            starts.Add(index);
            index += char.IsHighSurrogate(content[index]) ? 2 : 1;
        }
        var count = codepoints.Count;
        if (count == 0) { stops.Add(new Stop(to, 0, y)); return; }
        var levels = Bidi.Compute(codepoints, style.Direction).Levels;
        var removed = new bool[count];
        for (var at = 0; at < count; at++) removed[at] = Bidi.IsRemoved(codepoints[at]);

        var runs = new List<ShapedRun>();
        var cursor = 0;
        while (cursor < count)
        {
            if (removed[cursor]) { cursor++; continue; }
            var level = levels[cursor];
            var typeface = fonts[starts[cursor]];
            var colour = colours[starts[cursor]];
            var scale = DisplayScale(style, content, starts[cursor]);
            var shift = DisplayShift(style);
            var text = new StringBuilder();
            var start = cursor;
            while (cursor < count && !removed[cursor] && levels[cursor] == level
                && ReferenceEquals(fonts[starts[cursor]], typeface) && colours[starts[cursor]] == colour
                && Math.Abs(DisplayScale(style, content, starts[cursor]) - scale) < 1e-6)
            {
                var codepoint = (level & 1) == 1 ? Bidi.Mirror(codepoints[cursor]) : codepoints[cursor];
                text.Append(char.ConvertFromUtf32(codepoint));
                cursor++;
            }
            var runText = text.ToString();
            if (style.AllCaps == true || style.SmallCaps == true) runText = runText.ToUpperInvariant();
            var direction = (level & 1) == 1 ? TextDirection.RightToLeft : TextDirection.LeftToRight;
            var shaped = TextShaping.Shape(typeface, style.FontSize * scale, runText, direction,
                style.Ligatures ?? true, style.Kerning ?? true, style.Features, style.Language);
            runs.Add(new ShapedRun(runText, typeface, colour, scale, shift, level, shaped, start, cursor));
        }
        var visual = RunOrder(runs);

        var x = 0.0;
        var tracking = (float)style.Tracking;
        var lineStops = new List<Stop>();
        foreach (var run in visual)
        {
            var positions = run.Shaped.Positions;
            if (tracking != 0)
            {
                for (var at = 0; at < positions.Length; at++)
                    positions[at] = new SKPoint(positions[at].X + tracking * at, positions[at].Y);
            }
            var width = run.Shaped.Width + tracking * Math.Max(0, positions.Length - 1);
            pieces.Add(new Piece(run.Text, run.Typeface, run.Colour, (float)x, y + run.Shift, run.Scale, run.Shift,
                width, true, run.Shaped.Glyphs, positions));
            lineStops.Add(new Stop(starts[run.Start], (float)x, y + run.Shift));
            lineStops.Add(new Stop(run.End < count ? starts[run.End] : to, (float)(x + width), y + run.Shift));
            x += width;
        }
        lineStops.Sort((left, right) => left.Index.CompareTo(right.Index));
        stops.AddRange(lineStops);
    }

    /// <summary>Rule L2 over the runs: reverse at each level, from the highest down to the lowest odd.</summary>
    private static List<ShapedRun> RunOrder(List<ShapedRun> runs)
    {
        var order = new List<int>(runs.Count);
        for (var index = 0; index < runs.Count; index++) order.Add(index);
        var highest = 0;
        var lowestOdd = byte.MaxValue;
        foreach (var run in runs)
        {
            highest = Math.Max(highest, run.Level);
            if ((run.Level & 1) == 1) lowestOdd = Math.Min(lowestOdd, run.Level);
        }
        for (var level = highest; level >= lowestOdd; level--)
        {
            var index = 0;
            while (index < order.Count)
            {
                if (runs[order[index]].Level < level) { index++; continue; }
                var start = index;
                while (index < order.Count && runs[order[index]].Level >= level) index++;
                order.Reverse(start, index - start);
            }
        }
        var result = new List<ShapedRun>(runs.Count);
        foreach (var at in order) result.Add(runs[at]);
        return result;
    }

    /// <summary>The letters of one element after small-caps, all-caps and super/subscript are applied.</summary>
    private static string Display(LayerTextStyle style, string content, int index, int limit,
        out int taken, out float scale, out float shift)
    {
        var text = NextElement(content, index, limit, out taken);
        scale = DisplayScale(style, content, index);
        shift = DisplayShift(style);
        if (style.AllCaps == true || style.SmallCaps == true) text = text.ToUpperInvariant();
        return text;
    }

    private static float DisplayScale(LayerTextStyle style, string content, int index)
    {
        var scale = style.SmallCaps == true && index < content.Length && char.IsLower(content[index]) ? 0.8f : 1f;
        if (style.Superscript == true || style.Subscript == true) scale *= 0.58f;
        return scale;
    }

    private static float DisplayShift(LayerTextStyle style) => style.Superscript == true
        ? -(float)(style.FontSize * 0.33)
        : style.Subscript == true ? (float)(style.FontSize * 0.20) : 0f;

    /// <summary>Runs each line's pieces back to back again after a right-to-left paragraph reverses them.</summary>
    private static void Reflow(List<Piece> pieces)
    {
        var x = 0f;
        for (var index = 0; index < pieces.Count; index++)
        {
            pieces[index] = pieces[index] with { X = x };
            x += pieces[index].Width;
        }
    }

    /// <summary>How far a line starts from the left of its box: nothing, or what centring or righting leaves.</summary>
    private static double AlignmentOffset(TextAlignment alignment, double container, double width)
    {
        if (double.IsPositiveInfinity(container)) return 0;
        return alignment switch
        {
            TextAlignment.Center => (container - width) / 2,
            TextAlignment.Right => container - width,
            _ => 0,
        };
    }

    /// <summary>The advance of one element: what it measures at its scale, plus the tracking.</summary>
    private static double Advance(string text, SKTypeface typeface, LayerTextStyle style, float scale)
    {
        using var font = new SKFont(typeface, (float)(style.FontSize * scale)) { Embolden = style.Bold == true, SkewX = style.Italic == true ? -.25f : 0 };
        return font.MeasureText(text) + style.Tracking;
    }

    /// <summary>The lines of the content, as (start, length) into it. A break is a newline, in any of its forms.</summary>
    private static IEnumerable<(int Start, int Length)> Lines(string content)
    {
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character is not ('\n' or '\r' or '\u000b' or '\u000c' or '\u0085' or '\u2028' or '\u2029')) continue;
            yield return (start, index - start);
            // A carriage return followed by a line feed is one break, not two.
            if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
            start = index + 1;
        }
        yield return (start, content.Length - start);
    }

    /// <summary>
    /// The spans of one line that fit: the whole line for point text, or as many words as the box holds. A
    /// word wider than the box is left on a line of its own rather than being split. East Asian lines keep
    /// opening and closing punctuation with what it belongs to, as kinsoku requires.
    /// </summary>
    private static IEnumerable<(int From, int To)> Wrap(string content, int start, int length,
        LayerTextStyle style, List<SKTypeface> fonts, List<SKColor> colours, double limit)
    {
        if (double.IsPositiveInfinity(limit) || length == 0)
        {
            yield return (start, start + length);
            yield break;
        }
        var from = start;
        var at = start;
        var end = start + length;
        var width = 0.0;
        while (at < end)
        {
            var tokenEnd = at;
            // CJK scripts break between graphemes; Latin words break at whitespace.
            var text = NextElement(content, tokenEnd, end, out var count);
            tokenEnd += count;
            if (!IsCjk(text) && !char.IsWhiteSpace(text[0]))
                while (tokenEnd < end)
                {
                    var next = NextElement(content, tokenEnd, end, out count);
                    if (IsCjk(next) || char.IsWhiteSpace(next[0])) break;
                    tokenEnd += count;
                }
            var tokenWidth = Width(content, at, tokenEnd, style, fonts);
            if (width + tokenWidth > limit && at > from)
            {
                var breakAt = at;
                while (breakAt < end && NoLineStart(content[breakAt]))
                {
                    NextElement(content, breakAt, end, out var step);
                    breakAt += step;
                }
                if (breakAt > from && breakAt <= at && NoLineEnd(content[breakAt - 1]))
                {
                    NextElement(content, breakAt - 1, end, out var back);
                    breakAt -= back;
                }
                if (breakAt <= from) breakAt = at;
                yield return (from, breakAt);
                from = breakAt;
                width = 0;
                if (at < breakAt) at = breakAt;
                continue;
            }
            width += tokenWidth;
            at = tokenEnd;
        }
        yield return (from, end);
    }

    /// <summary>Punctuation that may not begin a line, so it is pulled up onto the one before.</summary>
    private static bool NoLineStart(char character) => character is
        '、' or '。' or '，' or '．' or '！' or '？' or '：' or '；' or '）' or '］' or '｝' or '〕' or '〉' or '》'
        or '」' or '』' or '】' or '〙' or '〗' or '〟' or '’' or '”' or '·' or '…' or 'ー' or '々'
        or ',' or '.' or '!' or '?' or ':' or ';' or ')' or ']' or '}' or '%';

    /// <summary>Punctuation that may not end a line, so it is pushed down to the next one.</summary>
    private static bool NoLineEnd(char character) => character is
        '（' or '［' or '｛' or '〔' or '〈' or '《' or '「' or '『' or '【' or '〘' or '〖' or '〝' or '‘' or '“'
        or '(' or '[' or '{';

    private static bool IsCjk(string text)
    {
        var scalar = char.ConvertToUtf32(text, 0);
        return scalar is >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7AF
            or >= 0xF900 and <= 0xFAFF or >= 0xFF00 and <= 0xFFEF or >= 0x20000 and <= 0x323AF;
    }

    private static readonly ConcurrentDictionary<(string Family, int Scalar), SKTypeface> Fallbacks = new();

    /// <summary>Fallback is a render-time choice, so a .comp retains its requested font name.</summary>
    public static SKTypeface ForText(SKTypeface preferred, string text)
    {
        if (text.Length == 0) return preferred;
        using var font = new SKFont(preferred);
        if (font.ContainsGlyphs(text)) return preferred;
        var scalar = char.ConvertToUtf32(text, 0);
        return Fallbacks.GetOrAdd((preferred.FamilyName, scalar), key =>
        {
            var matched = SKFontManager.Default.MatchCharacter(key.Family, preferred.FontStyle, ["zh-CN", "en"], key.Scalar)
                ?? SKFontManager.Default.MatchCharacter(key.Scalar);
            if (matched is not null) return matched;
            return BundledChinese.Value ?? preferred;
        });
    }

    private static double Width(string content, int from, int to, LayerTextStyle style, List<SKTypeface> fonts)
    {
        var width = 0.0;
        var index = from;
        while (index < to)
        {
            var text = NextElement(content, index, to, out var taken);
            width += Advance(text, ForText(fonts[index], text), style, DisplayScale(style, content, index));
            index += taken;
        }
        return width;
    }

    /// <summary>One character out of the content: a whole surrogate pair where there is one.</summary>
    private static string NextElement(string content, int index, int limit, out int taken)
    {
        var length = Math.Min(StringInfo.GetNextTextElementLength(content, index), limit - index);
        taken = length;
        return content.Substring(index, length);
    }

    /// <summary>Each character's colour: the style's, unless a colour run covers it.</summary>
    private static List<SKColor> Colours(LayerTextStyle style, int length)
    {
        var colour = ToColour(style.Red, style.Green, style.Blue);
        var colours = new List<SKColor>(length);
        for (var index = 0; index < length; index++) colours.Add(colour);
        foreach (var run in style.ColorRuns ?? [])
        {
            if (!Covers(run.Location, run.Length, length)) continue;
            var set = ToColour(run.Red, run.Green, run.Blue);
            for (var index = run.Location; index < run.Location + run.Length; index++) colours[index] = set;
        }
        return colours;
    }

    /// <summary>Each character's face: the style's, unless a font run covers it.</summary>
    private static List<SKTypeface> Fonts(LayerTextStyle style, int length)
    {
        var font = Typeface(style.FontName);
        var fonts = new List<SKTypeface>(length);
        for (var index = 0; index < length; index++) fonts.Add(font);
        foreach (var run in style.FontRuns ?? [])
        {
            if (!Covers(run.Location, run.Length, length)) continue;
            var set = Typeface(run.FontName);
            for (var index = run.Location; index < run.Location + run.Length; index++) fonts[index] = set;
        }
        return fonts;
    }

    private static readonly Lazy<SKTypeface?> BundledChinese = new(() =>
        SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSansSC.ttf")));

    private static bool Covers(int location, int length, int total) =>
        length > 0 && location >= 0 && location <= total - length;

    private static SKColor ToColour(double red, double green, double blue) => new(
        (byte)Math.Clamp(Math.Round(red * 255), 0, 255),
        (byte)Math.Clamp(Math.Round(green * 255), 0, 255),
        (byte)Math.Clamp(Math.Round(blue * 255), 0, 255));

    /// <summary>A copy of a style, so an edit does not change the layer it came from.</summary>
    internal static LayerTextStyle Copy(LayerTextStyle style) => new()
    {
        Content = style.Content,
        FontName = style.FontName,
        FontSize = style.FontSize,
        Red = style.Red, Green = style.Green, Blue = style.Blue,
        Alignment = style.Alignment,
        Tracking = style.Tracking,
        Leading = style.Leading,
        BoxSize = style.BoxSize,
        ColorRuns = style.ColorRuns,
        FontRuns = style.FontRuns,
        Bold = style.Bold, Italic = style.Italic, Underline = style.Underline, Strikethrough = style.Strikethrough,
        SmallCaps = style.SmallCaps, AllCaps = style.AllCaps, Superscript = style.Superscript, Subscript = style.Subscript,
        Ligatures = style.Ligatures, Kerning = style.Kerning, Features = style.Features, Direction = style.Direction,
        ComplexShaping = style.ComplexShaping, Language = style.Language, Dynamic = style.Dynamic,
    };
}

/// <summary>The values a dynamic text layer fills its <c>{token}</c>s in with.</summary>
public sealed record TextVariables(string? Name, int Width, int Height, double Resolution, int Layers, DateTime Now)
{
    public static readonly TextVariables Empty = new(null, 0, 0, 72, 0, DateTime.UnixEpoch);
}
