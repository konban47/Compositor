using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.Document;

public static partial class TextEdits
{
    private static List<Piece> VerticalLayout(LayerTextStyle style, TextVariables? variables, out double width, out double height, List<Stop>? stops)
    {
        var content = Content(style, variables); var fonts = Fonts(style, content.Length); var colors = Colours(style, content.Length);
        var pieces = new List<Piece>(); var positions = new List<Stop>();
        var limit = style.BoxSize is { } box ? Math.Max(style.FontSize, box.Height - 2 * Padding) : double.PositiveInfinity;
        var advance = Math.Max(1, style.FontSize + style.Tracking); var y = 0.0; var column = 0; height = 0;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(content);
        while (elements.MoveNext())
        {
            var text = elements.GetTextElement(); var index = elements.ElementIndex;
            if (text.Contains('\n') || y > 0 && y + advance > limit)
            { height = Math.Max(height, y); column++; y = 0; if (text.Contains('\n')) continue; }
            var run = new List<Piece>(); var ignored = new List<Stop>();
            BuildPlain(content, index, index + text.Length, style, fonts, colors, (float)(Padding + y + Baseline(style)), run, ignored);
            var runWidth = run.Sum(p => p.Width); var x = -column * style.LineHeight;
            pieces.AddRange(run.Select(p => p with { X = (float)(Padding + x + (style.LineHeight - runWidth) / 2 + p.X) }));
            positions.Add(new Stop(index, (float)(Padding + x + style.LineHeight / 2), (float)(Padding + y)));
            y += advance; positions.Add(new Stop(index + text.Length, (float)(Padding + x + style.LineHeight / 2), (float)(Padding + y)));
        }
        width = (column + 1) * style.LineHeight; height = Math.Max(height, y);
        var shift = (float)(column * style.LineHeight);
        for (var i = 0; i < pieces.Count; i++) pieces[i] = pieces[i] with { X = pieces[i].X + shift };
        stops?.AddRange(positions.Select(s => s with { X = s.X + shift }));
        return pieces;
    }
}
